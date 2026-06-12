using System;

namespace pjtSPEF.Models.Entities
{
    public class PreguntaClave
    {
        public int Id { get; set; }
        public int ExamenBaseId { get; set; }
        public int Numero { get; set; }
        public string Enunciado { get; set; }

        // Respuesta correcta o criterios de corrección que se le darán a Gemini
        // al calificar las entregas de los alumnos.
        public string RespuestaEsperada { get; set; }
        public decimal Puntaje { get; set; }
        public DateTime FechaCreacion { get; set; }

        public virtual ExamenBase ExamenBase { get; set; }
    }
}
