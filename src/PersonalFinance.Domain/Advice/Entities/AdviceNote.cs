namespace PersonalFinance.Domain.Advice.Entities
{
    /// <summary>
    /// Respuesta del usuario a un consejo concreto del asesor mensual, junto con
    /// lo que el asesor le contestó. Se guarda para devolvérsela al modelo en los
    /// análisis siguientes: así deja de repetir consejos que el usuario ya ha
    /// descartado o corregido.
    /// </summary>
    public class AdviceNote
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public int Year { get; set; }

        public int Month { get; set; }

        /// <summary>Título del consejo al que responde. Vacío si responde al resumen.</summary>
        public string InsightTitle { get; set; } = string.Empty;

        public string UserMessage { get; set; } = string.Empty;

        public string AssistantReply { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
