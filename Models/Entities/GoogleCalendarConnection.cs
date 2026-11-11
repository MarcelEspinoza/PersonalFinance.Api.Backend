namespace PersonalFinance.Api.Models.Entities
{
    public sealed class GoogleCalendarConnection
    {
        public Guid UserId { get; set; }
        public Guid Generation { get; set; } = Guid.NewGuid();
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? TaskListId { get; set; }
        public DateTime? AccessTokenExpiresAt { get; set; }
        public DateTime? ConnectedAt { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public string? LastError { get; set; }
        public bool NeedsReconnect { get; set; }
        public int Attempts { get; set; }
        public DateTime? RetryAt { get; set; }
        public Guid? LeaseId { get; set; }
        public DateTime? LeaseUntil { get; set; }
    }

    public sealed class GoogleCalendarAuthorization
    {
        public string StateHash { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public Guid ConnectionGeneration { get; set; }
        public string CodeVerifier { get; set; } = string.Empty;
        public string ReturnPath { get; set; } = "/zeus";
        public DateTime ExpiresAt { get; set; }
    }

    public sealed class GoogleSyncItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string SourceKey { get; set; } = string.Empty;
        public string ResourceKind { get; set; } = "event";
        public string? RemoteId { get; set; }
        public string? Payload { get; set; }
        public string DesiredHash { get; set; } = string.Empty;
        public bool NeedsSync { get; set; }
        public int Attempts { get; set; }
        public DateTime RetryAt { get; set; }
        public string? LastError { get; set; }
    }
}
