using System.ComponentModel.DataAnnotations;
using System.Web;

namespace pjtSPEF.Models.ViewModels
{
    public class ExamenBaseFormViewModel
    {
        public int? Id { get; set; }
        public int TipoEvaluacionId { get; set; }

        [Required(ErrorMessage = "El título del examen es obligatorio.")]
        [StringLength(200)]
        [Display(Name = "Título del examen")]
        public string Titulo { get; set; }

        [Required(ErrorMessage = "La nota máxima es obligatoria.")]
        [Range(0.5, 999.99, ErrorMessage = "La nota máxima debe ser mayor que cero.")]
        [Display(Name = "Nota máxima (20 = escala vigesimal)")]
        public decimal? NotaMaxima { get; set; }

        // Obligatorio al crear; opcional al editar (si se envía, reemplaza el PDF anterior).
        [Display(Name = "PDF del examen con respuestas (máx. 10 páginas)")]
        public HttpPostedFileBase ArchivoPdf { get; set; }
    }
}
