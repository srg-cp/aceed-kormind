using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    public class TipoEvaluacion
    {
        public int Id { get; set; }
        public int UnidadId { get; set; }
        public string Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }

        public virtual Unidad Unidad { get; set; }
        public virtual ICollection<ExamenBase> ExamenesBase { get; set; }
    }
}
