using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    public class GeminiCalificadorService : ICalificadorService
    {
        // HttpClient compartido: crear uno por request agota sockets bajo carga.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        private const string ModeloPorDefecto = "gemini-2.5-pro";

        private readonly string _apiKey;
        // Modelo del paso 1 (leer el examen del alumno): es lo crítico, conviene el más preciso.
        private readonly string _modelo;
        // Modelo del paso 2 (calificar la transcripción contra la clave): tarea de solo texto y
        // sencilla, así que puede ser un modelo más rápido. Si no se configura, usa el mismo del paso 1.
        private readonly string _modeloCalificacion;

        public GeminiCalificadorService()
        {
            _apiKey = ConfigurationManager.AppSettings["Gemini:ApiKey"];
            _modelo = ConfigurationManager.AppSettings["Gemini:Modelo"];
            if (string.IsNullOrWhiteSpace(_modelo))
                _modelo = ModeloPorDefecto;

            _modeloCalificacion = ConfigurationManager.AppSettings["Gemini:ModeloCalificacion"];
            if (string.IsNullOrWhiteSpace(_modeloCalificacion))
                _modeloCalificacion = _modelo;
        }

        public async Task<ResultadoCalificacion> CalificarAsync(Stream pdfEstudiante, IList<PreguntaClave> clave)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                return ResultadoCalificacion.Fallo(
                    "No hay clave de Gemini configurada. Copia Secrets.config.example como Secrets.config y completa Gemini:ApiKey.");

            if (clave == null || clave.Count == 0)
                return ResultadoCalificacion.Fallo(
                    "El examen base no tiene clave calibrada. Calíbrala antes de calificar entregas.");

            string pdfBase64;
            using (var ms = new MemoryStream())
            {
                if (pdfEstudiante.CanSeek)
                    pdfEstudiante.Position = 0;
                pdfEstudiante.CopyTo(ms);
                pdfBase64 = Convert.ToBase64String(ms.ToArray());
            }

            var claveOrdenada = clave.OrderBy(p => p.Numero).ToList();

            // Paso 1: transcribir lo que el alumno marcó, sin conocer la clave.
            var extraccion = await ExtraerRespuestasAsync(pdfBase64, claveOrdenada);
            if (!extraccion.Exito)
                return ResultadoCalificacion.Fallo(extraccion.Error);

            // Paso 2: calificar la transcripción contra la clave, sin el PDF.
            var calificacion = await CalificarRespuestasAsync(claveOrdenada, extraccion.PorNumero);
            if (!calificacion.Exito)
                return ResultadoCalificacion.Fallo(calificacion.Error);

            var respuestas = new List<RespuestaCalificada>();
            foreach (var pregunta in claveOrdenada)
            {
                extraccion.PorNumero.TryGetValue(pregunta.Numero, out var respuestaAlumno);
                extraccion.MarcasPorNumero.TryGetValue(pregunta.Numero, out var marca);
                calificacion.PorNumero.TryGetValue(pregunta.Numero, out var notaItem);

                var obtenido = (decimal?)notaItem?["puntajeObtenido"] ?? 0;
                if (obtenido < 0) obtenido = 0;
                if (obtenido > pregunta.Puntaje) obtenido = pregunta.Puntaje;

                respuestas.Add(new RespuestaCalificada
                {
                    Numero = pregunta.Numero,
                    RespuestaTexto = (respuestaAlumno ?? string.Empty).Trim(),
                    PuntajeObtenido = obtenido,
                    Comentario = ((string)notaItem?["comentario"] ?? string.Empty).Trim(),
                    Pagina = marca?.Pagina ?? 0,
                    MarcaX = marca?.X,
                    MarcaY = marca?.Y,
                    Dudoso = marca?.Dudoso ?? false
                });
            }

            return ResultadoCalificacion.Ok(extraccion.Nombre, respuestas);
        }

        // ---- Paso 1: extracción de lo que marcó el alumno (sin clave) -----------------------

        private sealed class MarcaExtraida
        {
            public int Pagina;
            public decimal? X;
            public decimal? Y;
            public bool Dudoso;
        }

        private sealed class ResultadoExtraccion
        {
            public bool Exito;
            public string Error;
            public string Nombre = string.Empty;
            // Respuesta transcrita del alumno por número de pregunta.
            public Dictionary<int, string> PorNumero = new Dictionary<int, string>();
            // Ubicación de la marca del alumno por número de pregunta (para el ✔/✗ sobre el PDF).
            public Dictionary<int, MarcaExtraida> MarcasPorNumero = new Dictionary<int, MarcaExtraida>();

            public static ResultadoExtraccion Fallo(string error) => new ResultadoExtraccion { Exito = false, Error = error };
        }

        private async Task<ResultadoExtraccion> ExtraerRespuestasAsync(string pdfBase64, IList<PreguntaClave> clave)
        {
            // Solo enunciados: NO se incluye la respuesta correcta para no sesgar la lectura.
            var enunciados = new StringBuilder();
            foreach (var p in clave)
                enunciados.AppendFormat(CultureInfo.InvariantCulture, "Pregunta {0}: {1}\n", p.Numero, p.Enunciado);

            var prompt =
                "Tu ÚNICA tarea es TRANSCRIBIR fielmente lo que un estudiante respondió en su examen. " +
                "NO eres el calificador y NO conoces las respuestas correctas. " +
                "El PDF adjunto es la entrega del alumno (manuscrita o impresa, en español, de CUALQUIER tipo: " +
                "respuestas escritas, selección de opciones, cálculos, código, diagramas, etc.). " +
                "Abajo está la lista de preguntas del examen (solo los enunciados).\n\n" +
                "Trabaja pregunta por pregunta y MIRA CON CUIDADO el papel. Para cada pregunta:\n" +
                "1) En 'observacion' describe primero qué escribió o marcó el alumno en esa zona del examen " +
                "(por ejemplo: una marca o selección sobre una opción, una letra o palabra manuscrita, un texto, " +
                "un procedimiento, o que quedó en blanco). Describe lo que ves ANTES de decidir.\n" +
                "2) Recién entonces, en 'respuestaTexto', escribe la respuesta que dio el alumno de forma legible:\n" +
                "   - Si marcó o seleccionó una o más opciones (con una X, un check, un círculo, un subrayado, etc.), " +
                "indica QUÉ opción señaló: su letra y/o su texto. El símbolo de la marca (✔, ✗, X, círculo) NO es la " +
                "respuesta; la respuesta es la opción a la que apunta esa marca.\n" +
                "   - Si escribió texto, un número, una letra o un valor a mano, cópialo tal cual.\n" +
                "   - Si dejó la pregunta en blanco, deja respuestaTexto vacío.\n" +
                "3) UBICACIÓN de la marca (para poder señalarla luego): indica 'pagina' (número de página del PDF, " +
                "empezando en 1) y la posición del CENTRO de la marca/respuesta del alumno como fracción de 0 a 1 desde la " +
                "esquina SUPERIOR IZQUIERDA de esa página: 'xMarca' (0 = borde izquierdo, 1 = borde derecho) y " +
                "'yMarca' (0 = borde superior, 1 = borde inferior). Sé lo más preciso posible.\n" +
                "4) Si el alumno marcó MÁS DE UNA opción, o la marca es ambigua/dudosa, pon 'dudoso' = true; si la marca es " +
                "única y clara, pon 'dudoso' = false. Si la pregunta quedó en blanco, pon pagina = 0, xMarca = 0, yMarca = 0, dudoso = false.\n\n" +
                "Presta especial atención a marcas o escritura tenues, a lápiz, pequeñas o poco claras, y revisa TODAS " +
                "las preguntas de principio a fin; es fácil saltarse la última.\n" +
                "REGLA CLAVE: copia lo que el alumno REALMENTE escribió o marcó, aunque sea incorrecto. " +
                "NO infieras, NO corrijas y NO completes la respuesta. Solo deja respuestaTexto vacío si de verdad no hay nada. " +
                "Detecta también el nombre del estudiante si aparece escrito (nombreEstudiante; vacío si no se ve).\n\n" +
                "PREGUNTAS DEL EXAMEN:\n" + enunciados;

            var request = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["parts"] = new JArray
                        {
                            new JObject { ["text"] = prompt },
                            new JObject
                            {
                                ["inline_data"] = new JObject
                                {
                                    ["mime_type"] = "application/pdf",
                                    ["data"] = pdfBase64
                                }
                            }
                        }
                    }
                },
                ["generationConfig"] = new JObject
                {
                    ["temperature"] = 0,
                    ["responseMimeType"] = "application/json",
                    ["responseSchema"] = new JObject
                    {
                        ["type"] = "OBJECT",
                        ["properties"] = new JObject
                        {
                            ["nombreEstudiante"] = new JObject { ["type"] = "STRING" },
                            ["respuestas"] = new JObject
                            {
                                ["type"] = "ARRAY",
                                ["items"] = new JObject
                                {
                                    ["type"] = "OBJECT",
                                    ["properties"] = new JObject
                                    {
                                        ["numero"] = new JObject { ["type"] = "INTEGER" },
                                        // 'observacion' va antes que 'respuestaTexto' a propósito: obliga al
                                        // modelo a describir la marca que ve antes de decidir, lo que sube
                                        // mucho la precisión al leer marcas tenues o manuscritas.
                                        ["observacion"] = new JObject { ["type"] = "STRING" },
                                        ["respuestaTexto"] = new JObject { ["type"] = "STRING" },
                                        ["pagina"] = new JObject { ["type"] = "INTEGER" },
                                        ["xMarca"] = new JObject { ["type"] = "NUMBER" },
                                        ["yMarca"] = new JObject { ["type"] = "NUMBER" },
                                        ["dudoso"] = new JObject { ["type"] = "BOOLEAN" }
                                    },
                                    ["required"] = new JArray { "numero", "observacion", "respuestaTexto", "pagina", "xMarca", "yMarca", "dudoso" }
                                }
                            }
                        },
                        ["required"] = new JArray { "nombreEstudiante", "respuestas" }
                    }
                }
            };

            var (texto, error) = await LlamarGeminiAsync(request, _modelo);
            if (error != null)
                return ResultadoExtraccion.Fallo(error);

            JObject json;
            try
            {
                json = JObject.Parse(texto);
            }
            catch
            {
                return ResultadoExtraccion.Fallo("Gemini no devolvió la transcripción en el formato esperado. Inténtalo de nuevo.");
            }

            var resultado = new ResultadoExtraccion
            {
                Exito = true,
                Nombre = ((string)json["nombreEstudiante"] ?? string.Empty).Trim()
            };
            foreach (var item in json["respuestas"] as JArray ?? new JArray())
            {
                var numero = (int?)item["numero"] ?? 0;
                if (numero <= 0) continue;
                resultado.PorNumero[numero] = ((string)item["respuestaTexto"] ?? string.Empty).Trim();
                resultado.MarcasPorNumero[numero] = LeerMarca(item);
            }

            return resultado;
        }

        // Lee la ubicación de la marca (página + fracción 0..1) de un item de respuesta.
        // Devuelve coordenadas null si la página no es válida o las fracciones quedan fuera de [0,1].
        private static MarcaExtraida LeerMarca(JToken item)
        {
            var pagina = (int?)item["pagina"] ?? 0;
            var x = (decimal?)item["xMarca"];
            var y = (decimal?)item["yMarca"];
            var dudoso = (bool?)item["dudoso"] ?? false;

            var valida = pagina >= 1 && x.HasValue && y.HasValue
                         && x.Value >= 0 && x.Value <= 1 && y.Value >= 0 && y.Value <= 1;
            return new MarcaExtraida
            {
                Pagina = valida ? pagina : 0,
                X = valida ? x : null,
                Y = valida ? y : null,
                Dudoso = dudoso
            };
        }

        // ---- Paso 2: calificación de la transcripción contra la clave (sin el PDF) -----------

        private sealed class ResultadoNotas
        {
            public bool Exito;
            public string Error;
            public Dictionary<int, JObject> PorNumero = new Dictionary<int, JObject>();

            public static ResultadoNotas Fallo(string error) => new ResultadoNotas { Exito = false, Error = error };
        }

        private async Task<ResultadoNotas> CalificarRespuestasAsync(IList<PreguntaClave> clave, IDictionary<int, string> respuestasAlumno)
        {
            var datos = new StringBuilder();
            foreach (var p in clave)
            {
                respuestasAlumno.TryGetValue(p.Numero, out var respuesta);
                datos.AppendFormat(CultureInfo.InvariantCulture,
                    "Pregunta {0} (puntaje máximo {1:0.##}):\n  Enunciado: {2}\n  Respuesta correcta / criterios: {3}\n  Respuesta del alumno (ya transcrita): {4}\n",
                    p.Numero, p.Puntaje, p.Enunciado,
                    string.IsNullOrWhiteSpace(p.RespuestaEsperada) ? "(no especificada)" : p.RespuestaEsperada,
                    string.IsNullOrWhiteSpace(respuesta) ? "(sin respuesta)" : respuesta);
            }

            var prompt =
                "Eres un docente universitario que califica un examen YA TRANSCRITO. " +
                "No tienes el examen original; solo te doy, por pregunta: el enunciado, la respuesta correcta o los criterios, " +
                "el puntaje máximo y la respuesta que el alumno escribió (ya transcrita por otra persona). " +
                "Para CADA pregunta, compara la respuesta del alumno con la correcta y asigna un puntajeObtenido entre 0 y el " +
                "puntaje máximo de esa pregunta (puede ser parcial). Explica brevemente en comentario por qué diste o descontaste puntos. " +
                "Si la respuesta del alumno es \"(sin respuesta)\", asigna 0 y comentario \"Pregunta sin respuesta.\". " +
                "No modifiques la transcripción del alumno ni inventes respuestas.\n\n" +
                "DATOS A CALIFICAR:\n" + datos;

            var request = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["parts"] = new JArray
                        {
                            new JObject { ["text"] = prompt }
                        }
                    }
                },
                ["generationConfig"] = new JObject
                {
                    ["temperature"] = 0,
                    ["responseMimeType"] = "application/json",
                    ["responseSchema"] = new JObject
                    {
                        ["type"] = "ARRAY",
                        ["items"] = new JObject
                        {
                            ["type"] = "OBJECT",
                            ["properties"] = new JObject
                            {
                                ["numero"] = new JObject { ["type"] = "INTEGER" },
                                ["puntajeObtenido"] = new JObject { ["type"] = "NUMBER" },
                                ["comentario"] = new JObject { ["type"] = "STRING" }
                            },
                            ["required"] = new JArray { "numero", "puntajeObtenido", "comentario" }
                        }
                    }
                }
            };

            var (texto, error) = await LlamarGeminiAsync(request, _modeloCalificacion);
            if (error != null)
                return ResultadoNotas.Fallo(error);

            JArray items;
            try
            {
                items = JArray.Parse(texto);
            }
            catch
            {
                return ResultadoNotas.Fallo("Gemini no devolvió la calificación en el formato esperado. Inténtalo de nuevo.");
            }

            var resultado = new ResultadoNotas { Exito = true };
            foreach (var item in items)
            {
                var numero = (int?)item["numero"] ?? 0;
                if (numero <= 0) continue;
                resultado.PorNumero[numero] = (JObject)item;
            }

            return resultado;
        }

        // ---- Plomería HTTP compartida por ambos pasos ---------------------------------------

        // Envía la petición a Gemini y devuelve el texto de la primera parte del candidato,
        // o un mensaje de error listo para mostrar (texto == null cuando hay error).
        private async Task<(string texto, string error)> LlamarGeminiAsync(JObject request, string modelo)
        {
            var url = string.Format(CultureInfo.InvariantCulture,
                "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent", modelo);

            try
            {
                using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    // La clave va en cabecera (no en la URL) para que no quede en logs de proxies.
                    httpRequest.Headers.Add("x-goog-api-key", _apiKey);
                    httpRequest.Content = new StringContent(request.ToString(), Encoding.UTF8, "application/json");

                    using (var response = await Http.SendAsync(httpRequest))
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                            return (null, DescribirErrorHttp((int)response.StatusCode, body));

                        JObject json;
                        try
                        {
                            json = JObject.Parse(body);
                        }
                        catch
                        {
                            return (null, "Gemini devolvió una respuesta que no se pudo interpretar.");
                        }

                        var candidato = json["candidates"]?.FirstOrDefault();
                        var texto = (string)candidato?["content"]?["parts"]?.FirstOrDefault()?["text"];
                        if (string.IsNullOrWhiteSpace(texto))
                        {
                            var bloqueo = (string)json["promptFeedback"]?["blockReason"];
                            return (null, bloqueo == null
                                ? "Gemini no devolvió contenido. Inténtalo de nuevo."
                                : "Gemini bloqueó la solicitud (" + bloqueo + ").");
                        }

                        return (texto, null);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return (null, "Gemini tardó demasiado en responder. Inténtalo de nuevo.");
            }
            catch (HttpRequestException)
            {
                return (null, "No se pudo conectar con Gemini. Revisa tu conexión a internet.");
            }
        }

        private static string DescribirErrorHttp(int statusCode, string body)
        {
            string detalle = null;
            try
            {
                detalle = (string)JObject.Parse(body)?["error"]?["message"];
            }
            catch
            {
                // El cuerpo no era JSON; se usa el mensaje genérico.
            }

            switch (statusCode)
            {
                case 400 when detalle != null && detalle.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0:
                case 401:
                case 403:
                    return "Gemini rechazó la clave de API. Verifica Gemini:ApiKey en Secrets.config.";
                case 429:
                    return string.IsNullOrEmpty(detalle)
                        ? "Se alcanzó el límite de uso de Gemini. Espera un minuto y vuelve a intentar."
                        : "Gemini rechazó la solicitud por límite de uso: " + detalle;
                default:
                    return string.IsNullOrEmpty(detalle)
                        ? string.Format(CultureInfo.InvariantCulture, "Gemini devolvió un error (HTTP {0}).", statusCode)
                        : string.Format(CultureInfo.InvariantCulture, "Gemini devolvió un error (HTTP {0}): {1}", statusCode, detalle);
            }
        }
    }
}
