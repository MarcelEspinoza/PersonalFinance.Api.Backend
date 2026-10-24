namespace PersonalFinance.Api.Features.ServiceSavings
{
    /// <summary>
    /// "Ahorra en tus servicios": asks the AI, with web search, for cheaper or
    /// better alternatives to the user's recurring payments.
    /// </summary>
    public interface IServiceSavingsService
    {
        Task<ServiceSavingsStateDto> GetStateAsync(Guid userId, CancellationToken ct);

        /// <summary>Creates a running review and queues it, unless one is running or the daily limit is reached.</summary>
        Task<ServiceSavingsStartResult> StartAnalysisAsync(Guid userId, CancellationToken ct);

        /// <summary>Upserts the user's comment / hidden flag for a service and returns all notes.</summary>
        Task<List<ServiceNoteDto>> SaveNoteAsync(Guid userId, ServiceNoteRequestDto request, CancellationToken ct);

        /// <summary>Runs a queued review. Called by <see cref="ServiceSavingsWorker"/>; AI failures are stored, not thrown.</summary>
        Task RunReviewAsync(Guid reviewId, CancellationToken ct);

        /// <summary>Marks a review as failed with a user-facing message if it is still running.</summary>
        Task MarkFailedAsync(Guid reviewId, string message, CancellationToken ct);
    }
}
