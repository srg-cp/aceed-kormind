using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    public class Curso
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string Nombre { get; set; }
        public string Periodo { get; set; }
        public DateTime FechaCreacion { get; set; }

        public virtual Usuario Usuario { get; set; }
        public virtual ICollection<Unidad> Unidades { get; set; }
    }
}
