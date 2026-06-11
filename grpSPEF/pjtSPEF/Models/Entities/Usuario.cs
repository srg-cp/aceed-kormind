using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    public class Usuario
    {
        public int Id { get; set; }
        public string GoogleId { get; set; }
        public string Email { get; set; }
        public string Nombre { get; set; }
        public byte[] RefreshToken { get; set; }
        public DateTime FechaCreacion { get; set; }
        public bool Activo { get; set; }

        public virtual ICollection<Curso> Cursos { get; set; }
    }
}
