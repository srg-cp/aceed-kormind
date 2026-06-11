using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    public class Unidad
    {
        public int Id { get; set; }
        public int CursoId { get; set; }
        public int Numero { get; set; }
        public string Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }

        public virtual Curso Curso { get; set; }
        public virtual ICollection<TipoEvaluacion> TiposEvaluacion { get; set; }
    }
}
