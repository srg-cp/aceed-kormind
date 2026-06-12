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

namespace pjtSPEF.Services
{
    public class PreguntaExtraida
    {
        public int Numero { get; set; }
        public string Enunciado { get; set; }
        public string RespuestaEsperada { get; set; }
        public decimal Puntaje { get; set; }
    }

    public class ResultadoExtraccionClave
    {
        public bool Exito { get; private set; }
        public string Error { get; private set; }
        public IList<PreguntaExtraida> Preguntas { get; private set; }

        public static ResultadoExtraccionClave Ok(IList<PreguntaExtraida> preguntas)
        {
            return new ResultadoExtraccionClave { Exito = true, Preguntas = preguntas };
        }

        public static ResultadoExtraccionClave Fallo(string error)
        {
            return new ResultadoExtraccionClave { Exito = false, Error = error, Preguntas = new List<PreguntaExtraida>() };
        }
    }

    // Extrae la clave de corrección (preguntas, respuestas y puntajes) del PDF de un examen base.
    public class GeminiExtractorClaveService
    {
        // HttpClient compartido: crear uno por request agota sockets bajo carga.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        private const string ModeloPorDefecto = "gemini-2.5-pro";

        private readonly string _apiKey;
        private readonly string _modelo;

        public GeminiExtractorClaveService()
        {
            _apiKey = ConfigurationManager.AppSettings["Gemini:ApiKey"];
            _modelo = ConfigurationManager.AppSettings["Gemini:Modelo"];
            if (string.IsNullOrWhiteSpace(_modelo))
                _modelo = ModeloPorDefecto;
        }

        public async Task<ResultadoExtraccionClave> ExtraerClaveAsync(Stream pdf, decimal notaMaxima)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                return ResultadoExtraccionClave.Fallo(
                    "No hay clave de Gemini configurada. Copia Secrets.config.example como Secrets.config y completa Gemini:ApiKey.");

            string pdfBase64;
            using (var ms = new MemoryStream())
            {
                if (pdf.CanSeek)
                    pdf.Position = 0;
                pdf.CopyTo(ms);
                pdfBase64 = Convert.ToBase64String(ms.ToArray());
            }

            var prompt =
                "Eres un asistente que digitaliza la clave de corrección de un examen universitario. " +
                "El PDF adjunto es el examen base resuelto por el docente: contiene las preguntas y sus respuestas correctas (puede ser manuscrito o impreso, en español). " +
                "Extrae cada pregunta con: numero (orden en el examen, empezando en 1), enunciado (texto de la pregunta), " +
                "respuestaEsperada (la respuesta correcta o los criterios de corrección que aparezcan; si no hay respuesta visible, cadena vacía) y " +
                "puntaje (los puntos de la pregunta si están indicados, p. ej. \"(3 pts)\"). " +
                string.Format(CultureInfo.InvariantCulture,
                    "La nota máxima del examen es {0:0.##}. Si el PDF no indica puntajes, reparte la nota máxima en partes iguales entre las preguntas. ", notaMaxima) +
                "Transcribe fielmente, sin inventar preguntas que no estén en el documento.";

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
                        ["type"] = "ARRAY",
                        ["items"] = new JObject
                        {
                            ["type"] = "OBJECT",
                            ["properties"] = new JObject
                            {
                                ["numero"] = new JObject { ["type"] = "INTEGER" },
                                ["enunciado"] = new JObject { ["type"] = "STRING" },
                                ["respuestaEsperada"] = new JObject { ["type"] = "STRING" },
                                ["puntaje"] = new JObject { ["type"] = "NUMBER" }
                            },
                            ["required"] = new JArray { "numero", "enunciado", "respuestaEsperada", "puntaje" }
                        }
                    }
                }
            };

            var url = string.Format(CultureInfo.InvariantCulture,
                "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent", _modelo);

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
                            return ResultadoExtraccionClave.Fallo(DescribirErrorHttp((int)response.StatusCode, body));

                        return InterpretarRespuesta(body);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return ResultadoExtraccionClave.Fallo("Gemini tardó demasiado en responder. Inténtalo de nuevo.");
            }
            catch (HttpRequestException)
            {
                return ResultadoExtraccionClave.Fallo("No se pudo conectar con Gemini. Revisa tu conexión a internet.");
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

        private static ResultadoExtraccionClave InterpretarRespuesta(string body)
        {
            JToken json;
            try
            {
                json = JObject.Parse(body);
            }
            catch
            {
                return ResultadoExtraccionClave.Fallo("Gemini devolvió una respuesta que no se pudo interpretar.");
            }

            var candidato = json["candidates"]?.FirstOrDefault();
            var texto = (string)candidato?["content"]?["parts"]?.FirstOrDefault()?["text"];
            if (string.IsNullOrWhiteSpace(texto))
            {
                var bloqueo = (string)json["promptFeedback"]?["blockReason"];
                return ResultadoExtraccionClave.Fallo(bloqueo == null
                    ? "Gemini no devolvió contenido. Inténtalo de nuevo."
                    : "Gemini bloqueó la solicitud (" + bloqueo + ").");
            }

            JArray items;
            try
            {
                items = JArray.Parse(texto);
            }
            catch
            {
                return ResultadoExtraccionClave.Fallo("Gemini no devolvió la clave en el formato esperado. Inténtalo de nuevo.");
            }

            var preguntas = new List<PreguntaExtraida>();
            foreach (var item in items)
            {
                var enunciado = ((string)item["enunciado"] ?? string.Empty).Trim();
                if (enunciado.Length == 0)
                    continue;

                preguntas.Add(new PreguntaExtraida
                {
                    Numero = (int?)item["numero"] ?? preguntas.Count + 1,
                    Enunciado = enunciado,
                    RespuestaEsperada = ((string)item["respuestaEsperada"] ?? string.Empty).Trim(),
                    Puntaje = (decimal?)item["puntaje"] ?? 0
                });
            }

            if (preguntas.Count == 0)
                return ResultadoExtraccionClave.Fallo("Gemini no encontró preguntas en el PDF. Verifica que el documento sea legible.");

            // Renumerar por si el modelo repitió o salteó números: el orden del documento manda.
            var ordenadas = preguntas.OrderBy(p => p.Numero).ToList();
            for (var i = 0; i < ordenadas.Count; i++)
                ordenadas[i].Numero = i + 1;

            return ResultadoExtraccionClave.Ok(ordenadas);
        }
    }
}
