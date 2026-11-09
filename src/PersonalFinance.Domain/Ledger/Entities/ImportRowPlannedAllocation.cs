namespace PersonalFinance.Domain.Ledger.Entities
{
    public sealed class ImportRowPlannedAllocation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ImportRowId { get; set; }
        public ImportRow? ImportRow { get; set; }
        public Guid PlannedEntryId { get; set; }
        public decimal Amount { get; set; }
    }
}
