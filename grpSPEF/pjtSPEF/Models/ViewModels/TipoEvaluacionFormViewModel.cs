using System.ComponentModel.DataAnnotations;

namespace pjtSPEF.Models.ViewModels
{
    public class TipoEvaluacionFormViewModel
    {
        public int? Id { get; set; }
        public int UnidadId { get; set; }

        [Required(ErrorMessage = "El nombre del tipo de evaluación es obligatorio.")]
        [StringLength(200)]
        [Display(Name = "Nombre (p. ej. Examen teórico, Práctica calificada)")]
        public string Nombre { get; set; }
    }
}
