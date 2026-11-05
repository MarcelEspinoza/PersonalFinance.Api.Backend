using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Models.Entities
{
    public sealed class ZeusHourPass
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public int Minutes { get; set; }
        public DateOnly PurchasedOn { get; set; }
        public Guid LedgerEntryId { get; set; }
        public LedgerEntry? LedgerEntry { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class ZeusHourUsage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public DateOnly Date { get; set; }
        public int Minutes { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
