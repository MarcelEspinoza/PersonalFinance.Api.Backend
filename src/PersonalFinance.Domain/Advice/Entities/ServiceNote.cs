namespace PersonalFinance.Domain.Advice.Entities
{
    /// <summary>
    /// The user's comment on a recurring service and whether its suggestions are
    /// hidden. Sent back to the next service savings analysis.
    /// </summary>
    public class ServiceNote
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        /// <summary>Stable key of the service, e.g. "rule:{id}" or "grp:{conceptId}:{description}".</summary>
        public string ServiceKey { get; set; } = string.Empty;

        public string ServiceName { get; set; } = string.Empty;

        public string? Message { get; set; }

        public bool Dismissed { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
