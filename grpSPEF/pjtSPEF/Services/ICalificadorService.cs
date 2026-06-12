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
