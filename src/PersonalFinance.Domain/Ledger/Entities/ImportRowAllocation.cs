namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>A categorized part of a bank transaction split during import review.</summary>
    public sealed class ImportRowAllocation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid ImportRowId { get; set; }
        public ImportRow? ImportRow { get; set; }
        public Guid ConceptId { get; set; }
        public Concept? Concept { get; set; }
        public decimal Amount { get; set; }
        public int SortOrder { get; set; }
    }
}
