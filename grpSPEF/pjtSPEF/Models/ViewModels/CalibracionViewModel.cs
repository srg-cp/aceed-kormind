using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace pjtSPEF.Models.ViewModels
{
    public class PreguntaClaveFormViewModel
    {
        public int Numero { get; set; }

        [Required(ErrorMessage = "El enunciado es obligatorio.")]
        [Display(Name = "Enunciado")]
        public string Enunciado { get; set; }

        [Display(Name = "Respuesta esperada / criterios")]
        public string RespuestaEsperada { get; set; }

        [Required(ErrorMessage = "El puntaje es obligatorio.")]
        [Range(0.01, 999.99, ErrorMessage = "El puntaje debe ser mayor que cero.")]
        [Display(Name = "Puntaje")]
        public decimal? Puntaje { get; set; }
    }

    public class CalibracionViewModel
    {
        public int ExamenBaseId { get; set; }
        public List<PreguntaClaveFormViewModel> Preguntas { get; set; } = new List<PreguntaClaveFormViewModel>();
    }
}
