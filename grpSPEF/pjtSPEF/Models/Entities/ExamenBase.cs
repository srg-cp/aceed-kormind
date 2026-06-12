using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    public class ExamenBase
    {
        public int Id { get; set; }
        public int TipoEvaluacionId { get; set; }
        public string Titulo { get; set; }

        // Referencia genérica al archivo: hoy ruta relativa en App_Data/storage,
        // cuando llegue Google Drive será el DriveFileId.
        public string ArchivoRef { get; set; }
        public string ArchivoNombreOriginal { get; set; }
        public int? TotalPaginas { get; set; }

        public decimal NotaMaxima { get; set; }
        public EstadoExamen Estado { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public virtual TipoEvaluacion TipoEvaluacion { get; set; }
        public virtual ICollection<PreguntaClave> PreguntasClave { get; set; }
        public virtual ICollection<EvaluacionEstudiante> Evaluaciones { get; set; }
    }
}
