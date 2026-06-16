using System.ComponentModel.DataAnnotations;

namespace pjtSPEF.Models.ViewModels
{
    public class CursoFormViewModel
    {
        public int? Id { get; set; }

        // Periodo (raíz) al que pertenece el curso. El periodo ya no es un campo de texto del curso.
        public int PeriodoId { get; set; }

        [Required(ErrorMessage = "El nombre del curso es obligatorio.")]
        [StringLength(200)]
        [Display(Name = "Nombre del curso")]
        public string Nombre { get; set; }
    }
}
