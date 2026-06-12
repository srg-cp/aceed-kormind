namespace pjtSPEF.Models.Entities
{
    public enum EstadoEvaluacion : byte
    {
        // Subida pero todavía no calificada por la IA.
        Pendiente = 0,
        // Calificada: tiene nota y detalle por pregunta.
        Calificada = 1,
        // La calificación con IA falló; MensajeError explica por qué (se puede reintentar).
        Error = 2
    }
}
