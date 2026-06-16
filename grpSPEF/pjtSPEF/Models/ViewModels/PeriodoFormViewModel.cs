using System.ComponentModel.DataAnnotations;

namespace pjtSPEF.Models.ViewModels
{
    public class PeriodoFormViewModel
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "El año es obligatorio.")]
        [Range(2000, 2100, ErrorMessage = "El año debe estar entre 2000 y 2100.")]
        [Display(Name = "Año")]
        public int? Anio { get; set; }

        // Solo existen tres tipos por año: I, II, REC.
        [Required(ErrorMessage = "El tipo de periodo es obligatorio.")]
        [Display(Name = "Tipo (I, II o REC)")]
        public string Tipo { get; set; }
    }
}
