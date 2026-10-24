namespace PersonalFinance.Domain.Advice.Entities
{
    /// <summary>
    /// One run of the "save on your services" analysis. It is created as running
    /// and completed later by a background worker.
    /// </summary>
    public class ServiceReview
    {
        public const string StatusRunning = "running";
        public const string StatusDone = "done";
        public const string StatusFailed = "failed";

        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public string Status { get; set; } = StatusRunning;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        public string? Summary { get; set; }

        /// <summary>Validated suggestions serialized as a camelCase JSON array.</summary>
        public string? ItemsJson { get; set; }

        /// <summary>User-facing error message when the run failed.</summary>
        public string? Error { get; set; }

        public int SearchCount { get; set; }

        public string? Model { get; set; }
    }
}
