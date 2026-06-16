using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    // Raíz de la jerarquía: un periodo académico del docente (año + tipo I/II/REC).
    // De cada periodo cuelgan los cursos/materias que el docente crea.
    public class Periodo
    {
        public int Id { get; set; }
        public int Anio { get; set; }
        public TipoPeriodo Tipo { get; set; }
        public DateTime FechaCreacion { get; set; }

        // Nombre mostrado, p. ej. "2026-I". Se deriva de Anio + Tipo (no se persiste).
        public string Nombre => Anio + "-" + Tipo.Sufijo();

        // Se puebla manualmente al cargar el detalle (Sheets no tiene lazy-load).
        public ICollection<Curso> Cursos { get; set; }
    }
}
