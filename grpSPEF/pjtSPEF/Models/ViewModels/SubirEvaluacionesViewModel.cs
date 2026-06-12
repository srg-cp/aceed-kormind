using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web;

namespace pjtSPEF.Models.ViewModels
{
    public class SubirEvaluacionesViewModel
    {
        public int ExamenBaseId { get; set; }

        [Display(Name = "PDFs de las entregas (uno por estudiante)")]
        public IEnumerable<HttpPostedFileBase> Archivos { get; set; }
    }
}
