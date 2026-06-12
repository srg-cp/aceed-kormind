using System;

namespace pjtSPEF.Models.Entities
{
    // Resultado de la IA para una pregunta de la entrega de un estudiante.
    // Guarda una copia (snapshot) del enunciado y del puntaje máximo para que el detalle
    // de la calificación siga siendo legible aunque luego se recalibre la clave del examen.
    public class RespuestaEstudiante
    {
        public int Id { get; set; }
        public int EvaluacionEstudianteId { get; set; }
        public int Numero { get; set; }
        public string Enunciado { get; set; }

        // Lo que la IA leyó como respuesta del alumno (transcripción).
        public string RespuestaTexto { get; set; }
        public decimal PuntajeMaximo { get; set; }
        public decimal PuntajeObtenido { get; set; }
        // Justificación breve de la IA: por qué se asignó/descontó puntaje.
        public string Comentario { get; set; }
        public DateTime FechaCreacion { get; set; }

        public virtual EvaluacionEstudiante EvaluacionEstudiante { get; set; }
    }
}
