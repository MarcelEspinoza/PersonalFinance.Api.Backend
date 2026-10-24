namespace PersonalFinance.Api.Features.ServiceSavings
{
    public sealed class ServiceSavingsStateDto
    {
        /// <summary>Latest review, or null if the user never ran one.</summary>
        public ServiceReviewDto? Review { get; set; }
        public List<ServiceNoteDto> Notes { get; set; } = new();
        public int RemainingToday { get; set; }
        public int DailyLimit { get; set; }
    }

    public sealed class ServiceReviewDto
    {
        public Guid Id { get; set; }
        /// <summary>"running", "done" or "failed".</summary>
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Summary { get; set; }
        public List<ServiceSavingItemDto> Items { get; set; } = new();
        public int SearchCount { get; set; }
        public string? Error { get; set; }
    }

    public sealed class ServiceSavingItemDto
    {
        public string Key { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public string? Category { get; set; }
        /// <summary>"cheaper", "cancel" or "switch".</summary>
        public string Action { get; set; } = string.Empty;
        public decimal? CurrentMonthly { get; set; }
        public string? Proposal { get; set; }
        public decimal? ProposalMonthly { get; set; }
        public decimal? MonthlySaving { get; set; }
        public string? Detail { get; set; }
        public string? Caution { get; set; }
        public List<ServiceSavingSourceDto> Sources { get; set; } = new();
    }

    public sealed class ServiceSavingSourceDto
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public sealed class ServiceNoteDto
    {
        public string ServiceKey { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string? Message { get; set; }
        public bool Dismissed { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ServiceNoteRequestDto
    {
        public string? ServiceKey { get; set; }
        public string? ServiceName { get; set; }
        public string? Message { get; set; }
        public bool Dismissed { get; set; }
    }

    /// <summary>Outcome of asking for a new analysis.</summary>
    public sealed class ServiceSavingsStartResult
    {
        public ServiceSavingsStateDto State { get; init; } = new();

        /// <summary>Set when the daily limit is reached; the controller answers 429 with it.</summary>
        public string? LimitMessage { get; init; }
    }
}
