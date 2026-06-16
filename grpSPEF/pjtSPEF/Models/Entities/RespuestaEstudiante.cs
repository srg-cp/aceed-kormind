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

        // Ubicación aproximada de la marca del alumno (para estampar el ✔/✗ sobre el PDF).
        // Pagina 1-based; MarcaX/MarcaY en fracción 0..1 desde la esquina superior izquierda.
        public int? Pagina { get; set; }
        public decimal? MarcaX { get; set; }
        public decimal? MarcaY { get; set; }
        // true si la IA detectó marca múltiple o ambigua: no se dibuja el visto, se avisa al docente.
        public bool Dudoso { get; set; }

        public DateTime FechaCreacion { get; set; }

        public virtual EvaluacionEstudiante EvaluacionEstudiante { get; set; }
    }
}
