using System.ComponentModel.DataAnnotations;

namespace pjtSPEF.Models.ViewModels
{
    public class UnidadFormViewModel
    {
        public int? Id { get; set; }
        public int CursoId { get; set; }

        [Required(ErrorMessage = "El número de unidad es obligatorio.")]
        [Range(1, 100, ErrorMessage = "El número de unidad debe estar entre 1 y 100.")]
        [Display(Name = "Número de unidad")]
        public int? Numero { get; set; }

        [Required(ErrorMessage = "El nombre de la unidad es obligatorio.")]
        [StringLength(200)]
        [Display(Name = "Nombre de la unidad")]
        public string Nombre { get; set; }
    }
}
