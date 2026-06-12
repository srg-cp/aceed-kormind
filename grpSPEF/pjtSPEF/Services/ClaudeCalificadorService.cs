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
    public class ClaudeCalificadorService : ICalificadorService
    {
        // Opus puede tardar más que Gemini al razonar sobre imágenes; damos margen amplio.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };

        private const string ModeloPorDefecto = "claude-opus-4-8";
        private const string VersionApi = "2023-06-01";
        private const string Url = "https://api.anthropic.com/v1/messages";

        private readonly string _apiKey;
        // Modelo del paso 1 (leer el examen del alumno): es lo crítico, conviene el más preciso.
        private readonly string _modelo;
        // Modelo del paso 2 (calificar la transcripción contra la clave): tarea de solo texto y
        // sencilla, así que puede ser un modelo más rápido. Si no se configura, usa el mismo del paso 1.
        private readonly string _modeloCalificacion;

        public ClaudeCalificadorService()
        {
            _apiKey = ConfigurationManager.AppSettings["Claude:ApiKey"];
            _modelo = ConfigurationManager.AppSettings["Claude:Modelo"];
            if (string.IsNullOrWhiteSpace(_modelo))
                _modelo = ModeloPorDefecto;

            _modeloCalificacion = ConfigurationManager.AppSettings["Claude:ModeloCalificacion"];
            if (string.IsNullOrWhiteSpace(_modeloCalificacion))
                _modeloCalificacion = _modelo;
        }

        public async Task<ResultadoCalificacion> CalificarAsync(Stream pdfEstudiante, IList<PreguntaClave> clave)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                return ResultadoCalificacion.Fallo(
                    "No hay clave de Claude configurada. Completa Claude:ApiKey en Secrets.config.");

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
                calificacion.PorNumero.TryGetValue(pregunta.Numero, out var notaItem);

                var obtenido = (decimal?)notaItem?["puntajeObtenido"] ?? 0;
                if (obtenido < 0) obtenido = 0;
                if (obtenido > pregunta.Puntaje) obtenido = pregunta.Puntaje;

                respuestas.Add(new RespuestaCalificada
                {
                    Numero = pregunta.Numero,
                    RespuestaTexto = (respuestaAlumno ?? string.Empty).Trim(),
                    PuntajeObtenido = obtenido,
                    Comentario = ((string)notaItem?["comentario"] ?? string.Empty).Trim()
                });
            }

            return ResultadoCalificacion.Ok(extraccion.Nombre, respuestas);
        }

        // ---- Paso 1: extracción de lo que marcó el alumno (sin clave) -----------------------

        private sealed class ResultadoExtraccion
        {
            public bool Exito;
            public string Error;
            public string Nombre = string.Empty;
            // Respuesta transcrita del alumno por número de pregunta.
            public Dictionary<int, string> PorNumero = new Dictionary<int, string>();

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
                "   - Si dejó la pregunta en blanco, deja respuestaTexto vacío.\n\n" +
                "Presta especial atención a marcas o escritura tenues, a lápiz, pequeñas o poco claras, y revisa TODAS " +
                "las preguntas de principio a fin; es fácil saltarse la última.\n" +
                "REGLA CLAVE: copia lo que el alumno REALMENTE escribió o marcó, aunque sea incorrecto. " +
                "NO infieras, NO corrijas y NO completes la respuesta. Solo deja respuestaTexto vacío si de verdad no hay nada. " +
                "Detecta también el nombre del estudiante si aparece escrito (nombreEstudiante; vacío si no se ve).\n\n" +
                "Devuelve el resultado llamando a la herramienta 'registrar_respuestas'.\n\n" +
                "PREGUNTAS DEL EXAMEN:\n" + enunciados;

            // input_schema en JSON Schema estándar (minúsculas), no el dialecto de Gemini.
            var esquema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["nombreEstudiante"] = new JObject { ["type"] = "string" },
                    ["respuestas"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["numero"] = new JObject { ["type"] = "integer" },
                                // 'observacion' antes que 'respuestaTexto' a propósito: obliga al modelo a
                                // describir la marca que ve antes de decidir, subiendo la precisión de lectura.
                                ["observacion"] = new JObject { ["type"] = "string" },
                                ["respuestaTexto"] = new JObject { ["type"] = "string" }
                            },
                            ["required"] = new JArray { "numero", "observacion", "respuestaTexto" }
                        }
                    }
                },
                ["required"] = new JArray { "nombreEstudiante", "respuestas" }
            };

            var contenido = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = prompt },
                new JObject
                {
                    ["type"] = "document",
                    ["source"] = new JObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = "application/pdf",
                        ["data"] = pdfBase64
                    }
                }
            };

            var request = ConstruirRequest(_modelo, "registrar_respuestas", "Registra la transcripción de las respuestas del alumno.", esquema, contenido);

            var (input, error) = await LlamarClaudeAsync(request, "registrar_respuestas");
            if (error != null)
                return ResultadoExtraccion.Fallo(error);

            var resultado = new ResultadoExtraccion
            {
                Exito = true,
                Nombre = ((string)input["nombreEstudiante"] ?? string.Empty).Trim()
            };
            foreach (var item in input["respuestas"] as JArray ?? new JArray())
            {
                var numero = (int?)item["numero"] ?? 0;
                if (numero <= 0) continue;
                resultado.PorNumero[numero] = ((string)item["respuestaTexto"] ?? string.Empty).Trim();
            }

            return resultado;
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
                "Devuelve el resultado llamando a la herramienta 'registrar_calificacion'.\n\n" +
                "DATOS A CALIFICAR:\n" + datos;

            // La API exige que el input de la herramienta sea un objeto en la raíz: envolvemos el
            // arreglo de notas en 'items'.
            var esquema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["items"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["numero"] = new JObject { ["type"] = "integer" },
                                ["puntajeObtenido"] = new JObject { ["type"] = "number" },
                                ["comentario"] = new JObject { ["type"] = "string" }
                            },
                            ["required"] = new JArray { "numero", "puntajeObtenido", "comentario" }
                        }
                    }
                },
                ["required"] = new JArray { "items" }
            };

            var contenido = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = prompt }
            };

            var request = ConstruirRequest(_modeloCalificacion, "registrar_calificacion", "Registra la nota y el comentario de cada pregunta.", esquema, contenido);

            var (input, error) = await LlamarClaudeAsync(request, "registrar_calificacion");
            if (error != null)
                return ResultadoNotas.Fallo(error);

            var resultado = new ResultadoNotas { Exito = true };
            foreach (var item in input["items"] as JArray ?? new JArray())
            {
                var numero = (int?)item["numero"] ?? 0;
                if (numero <= 0) continue;
                resultado.PorNumero[numero] = (JObject)item;
            }

            return resultado;
        }

        // ---- Plomería HTTP compartida por ambos pasos ---------------------------------------

        private JObject ConstruirRequest(string modelo, string nombreHerramienta, string descripcionHerramienta, JObject inputSchema, JArray contenidoUsuario)
        {
            // Sin 'temperature' ni 'thinking': Opus 4.8 no admite sampling params, y al forzar una
            // herramienta (tool_choice) el "thinking" tampoco aplica. Forzar la tool da JSON fiable.
            return new JObject
            {
                ["model"] = modelo,
                ["max_tokens"] = 8000,
                ["tools"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = nombreHerramienta,
                        ["description"] = descripcionHerramienta,
                        ["input_schema"] = inputSchema
                    }
                },
                ["tool_choice"] = new JObject
                {
                    ["type"] = "tool",
                    ["name"] = nombreHerramienta
                },
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = contenidoUsuario
                    }
                }
            };
        }

        // Envía la petición a Claude y devuelve el input del bloque tool_use esperado,
        // o un mensaje de error listo para mostrar (input == null cuando hay error).
        private async Task<(JObject input, string error)> LlamarClaudeAsync(JObject request, string nombreHerramienta)
        {
            try
            {
                using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, Url))
                {
                    httpRequest.Headers.Add("x-api-key", _apiKey);
                    httpRequest.Headers.Add("anthropic-version", VersionApi);
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
                            return (null, "Claude devolvió una respuesta que no se pudo interpretar.");
                        }

                        // Clasificadores de seguridad pueden declinar la solicitud (HTTP 200).
                        var stopReason = (string)json["stop_reason"];
                        if (string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase))
                            return (null, "Claude rechazó procesar este documento por sus políticas de seguridad.");

                        // Buscar el bloque tool_use con la herramienta que forzamos.
                        var bloques = json["content"] as JArray ?? new JArray();
                        var toolUse = bloques.FirstOrDefault(b =>
                            (string)b["type"] == "tool_use" &&
                            (string)b["name"] == nombreHerramienta);

                        var input = toolUse?["input"] as JObject;
                        if (input == null)
                            return (null, "Claude no devolvió el resultado en el formato esperado. Inténtalo de nuevo.");

                        return (input, null);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return (null, "Claude tardó demasiado en responder. Inténtalo de nuevo.");
            }
            catch (HttpRequestException)
            {
                return (null, "No se pudo conectar con Claude. Revisa tu conexión a internet.");
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
                case 401:
                case 403:
                    return "Claude rechazó la clave de API. Verifica Claude:ApiKey en Secrets.config.";
                case 400 when detalle != null && detalle.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0:
                    return "La cuenta de Claude no tiene saldo suficiente. Recarga créditos en console.anthropic.com.";
                case 429:
                    return string.IsNullOrEmpty(detalle)
                        ? "Se alcanzó el límite de uso de Claude. Espera un momento y vuelve a intentar."
                        : "Claude rechazó la solicitud por límite de uso: " + detalle;
                case 529:
                    return "Los servidores de Claude están saturados ahora mismo. Inténtalo de nuevo en un momento.";
                default:
                    return string.IsNullOrEmpty(detalle)
                        ? string.Format(CultureInfo.InvariantCulture, "Claude devolvió un error (HTTP {0}).", statusCode)
                        : string.Format(CultureInfo.InvariantCulture, "Claude devolvió un error (HTTP {0}): {1}", statusCode, detalle);
            }
        }
    }
}
