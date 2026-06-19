namespace pjtSPEF.Models.Entities
{
    public enum EstadoEvaluacion : byte
    {
        // Subida pero todavía no calificada por la IA.
        Pendiente = 0,
        // Calificada por la IA: tiene nota y detalle, pero el docente todavía no la revisó.
        Calificada = 1,
        // La calificación con IA falló; MensajeError explica por qué (se puede reintentar).
        Error = 2,
        // Revisada y confirmada por el docente (pulsó Guardar calificación). Una re-calificación
        // con IA la vuelve a dejar en Calificada (hay que volver a revisarla).
        Revisada = 3
    }
}
