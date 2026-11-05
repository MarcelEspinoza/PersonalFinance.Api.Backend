using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Models.Entities
{
    public sealed class ZeusCareStay
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Provider { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateOnly Date { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public int Nights { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
        public Guid? LedgerEntryId { get; set; }
        public LedgerEntry? LedgerEntry { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
