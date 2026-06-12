using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    // Entrega de un estudiante para un examen base: el PDF resuelto del alumno
    // que la IA califica contra la clave (PreguntaClave) de ese examen.
    public class EvaluacionEstudiante
    {
        public int Id { get; set; }
        public int ExamenBaseId { get; set; }

        // Nombre del estudiante: lo escribe el docente o lo detecta la IA del propio examen.
        public string NombreEstudiante { get; set; }

        // Misma convención que ExamenBase.ArchivoRef: hoy DriveFileId, antes ruta local.
        public string ArchivoRef { get; set; }
        public string ArchivoNombreOriginal { get; set; }
        public int? TotalPaginas { get; set; }

        // Suma de los puntajes obtenidos; null mientras no esté calificada.
        public decimal? NotaTotal { get; set; }
        public EstadoEvaluacion Estado { get; set; }
        // Motivo del fallo cuando Estado == Error (para mostrarlo y permitir reintento).
        public string MensajeError { get; set; }

        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaCalificacion { get; set; }

        public virtual ExamenBase ExamenBase { get; set; }
        public virtual ICollection<RespuestaEstudiante> Respuestas { get; set; }
    }
}
