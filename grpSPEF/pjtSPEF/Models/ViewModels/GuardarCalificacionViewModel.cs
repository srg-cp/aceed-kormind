using System.Collections.Generic;

namespace pjtSPEF.Models.ViewModels
{
    // Edición en bloque del detalle de una entrega: el docente revisa lo que calificó la IA,
    // ajusta transcripción y puntaje de cada pregunta y guarda todo de una con GUARDAR.
    // El puntaje se valida en el servidor contra el PuntajeMaximo de cada pregunta (clave calibrada).
    public class GuardarCalificacionViewModel
    {
        public int EvaluacionId { get; set; }
        public List<RespuestaEditViewModel> Respuestas { get; set; }
    }

    public class RespuestaEditViewModel
    {
        public int Id { get; set; }
        public string RespuestaTexto { get; set; }
        public decimal PuntajeObtenido { get; set; }
    }
}
