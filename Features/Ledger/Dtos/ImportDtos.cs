namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    public sealed class ImportConceptOptionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }

    public sealed class ImportRowDto
    {
        public Guid Id { get; set; }
        public int RowNumber { get; set; }
        public DateOnly ValueDate { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string RawDescription { get; set; } = string.Empty;
        public string? NormalizedDescription { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? SuggestedConceptId { get; set; }
        public Guid? ConfirmedConceptId { get; set; }
        public string? SuggestionSource { get; set; }
    }

    public sealed class ImportReviewDto
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string? FileName { get; set; }
        public string Status { get; set; } = string.Empty;
        public IReadOnlyList<ImportRowDto> Rows { get; set; } = Array.Empty<ImportRowDto>();
        public IReadOnlyList<ImportConceptOptionDto> Concepts { get; set; } = Array.Empty<ImportConceptOptionDto>();
    }

    public sealed class SelectImportConceptDto
    {
        public Guid? ConceptId { get; set; }
    }

    public sealed class ImportBatchDto
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string? FileName { get; set; }
        public int TotalRows { get; set; }
        public int AcceptedRows { get; set; }
        public int DuplicateRows { get; set; }
        public int ExcludedRows { get; set; }
        public IReadOnlyList<string> Problems { get; set; } = Array.Empty<string>();
    }
}
