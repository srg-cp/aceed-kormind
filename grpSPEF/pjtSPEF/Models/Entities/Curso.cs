using System;
using System.Collections.Generic;

namespace pjtSPEF.Models.Entities
{
    // Curso/materia (p. ej. "Matemáticas"). Cuelga de un Periodo: el periodo dejó de ser
    // un campo del curso y pasó a ser la raíz independiente de la jerarquía.
    public class Curso
    {
        public int Id { get; set; }
        public int PeriodoId { get; set; }
        public string Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }

        // Propiedades de navegación pobladas a mano al cargar (no hay lazy-load con Sheets).
        public Periodo Periodo { get; set; }
        public ICollection<Unidad> Unidades { get; set; }
    }
}
