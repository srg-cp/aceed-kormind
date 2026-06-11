using System.ComponentModel.DataAnnotations;

namespace pjtSPEF.Models.ViewModels
{
    public class CursoFormViewModel
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "El nombre del curso es obligatorio.")]
        [StringLength(200)]
        [Display(Name = "Nombre del curso")]
        public string Nombre { get; set; }

        [StringLength(50)]
        [Display(Name = "Periodo (p. ej. 2026-I)")]
        public string Periodo { get; set; }
    }
}
