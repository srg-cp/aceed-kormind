using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    public class RespuestaCalificada
    {
        public int Numero { get; set; }
        public string RespuestaTexto { get; set; }
        public decimal PuntajeObtenido { get; set; }
        public string Comentario { get; set; }

        // Ubicación (aproximada) de la marca/respuesta del alumno, para dibujar el ✔/✗ encima del PDF.
        // Pagina es 1-based; MarcaX/MarcaY son fracciones 0..1 desde la esquina superior izquierda
        // de esa página. Quedan null si la IA no ubicó una marca clara (p. ej. pregunta en blanco).
        public int Pagina { get; set; }
        public decimal? MarcaX { get; set; }
        public decimal? MarcaY { get; set; }
        // true si el alumno marcó más de una opción o la marca es ambigua: no se dibuja, solo se avisa.
        public bool Dudoso { get; set; }
    }

    public class ResultadoCalificacion
    {
        public bool Exito { get; private set; }
        public string Error { get; private set; }
        // Nombre del alumno detectado en el PDF; vacío si la IA no lo encontró.
        public string NombreDetectado { get; private set; }
        public IList<RespuestaCalificada> Respuestas { get; private set; }

        public static ResultadoCalificacion Ok(string nombreDetectado, IList<RespuestaCalificada> respuestas)
        {
            return new ResultadoCalificacion
            {
                Exito = true,
                NombreDetectado = nombreDetectado,
                Respuestas = respuestas
            };
        }

        public static ResultadoCalificacion Fallo(string error)
        {
            return new ResultadoCalificacion
            {
                Exito = false,
                Error = error,
                Respuestas = new List<RespuestaCalificada>()
            };
        }
    }

    // Califica la entrega (PDF resuelto) de un estudiante contra la clave del examen base.
    // Hoy lo implementa Gemini; la interfaz permite cambiar de proveedor sin tocar el controlador.
    public interface ICalificadorService
    {
        Task<ResultadoCalificacion> CalificarAsync(Stream pdfEstudiante, IList<PreguntaClave> clave);
    }
}
