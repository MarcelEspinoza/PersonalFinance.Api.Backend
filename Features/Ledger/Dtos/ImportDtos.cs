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
        /// <summary>Día en que el banco liquidó el movimiento; mueve el saldo.</summary>
        public DateOnly? SettledDate { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string RawDescription { get; set; } = string.Empty;
        public string? NormalizedDescription { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? SuggestedConceptId { get; set; }
        public Guid? ConfirmedConceptId { get; set; }
        public string? SuggestionSource { get; set; }
        public IReadOnlyList<ImportRowAllocationDto> Allocations { get; set; } = Array.Empty<ImportRowAllocationDto>();
    }

    public sealed class ImportRowAllocationDto
    {
        public Guid ConceptId { get; set; }
        public decimal Amount { get; set; }
    }

    public sealed class SplitImportRowDto
    {
        public IReadOnlyList<ImportRowAllocationDto> Allocations { get; set; } = Array.Empty<ImportRowAllocationDto>();
    }

    public sealed class ImportReviewDto
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string? FileName { get; set; }
        public string Status { get; set; } = string.Empty;
        public IReadOnlyList<ImportRowDto> Rows { get; set; } = Array.Empty<ImportRowDto>();
        public IReadOnlyList<ImportConceptOptionDto> Concepts { get; set; } = Array.Empty<ImportConceptOptionDto>();
        public ImportAnalysisDto? Analysis { get; set; }
    }

    /// <summary>Revisión automática del extracto: lo que miraría una persona con el banco delante.</summary>
    public sealed class ImportAnalysisDto
    {
        public decimal? BankBalance { get; set; }
        public DateOnly? BankBalanceDate { get; set; }
        /// <summary>Saldo de la app en esa fecha contando este extracto (aplicado o por aplicar).</summary>
        public decimal? AppBalance { get; set; }
        /// <summary>Banco menos app. Cero si cuadra.</summary>
        public decimal? Difference { get; set; }
        public IReadOnlyList<ImportFindingDto> Findings { get; set; } = Array.Empty<ImportFindingDto>();
    }

    public sealed class ImportFindingDto
    {
        /// <summary>"ok", "info" o "warning".</summary>
        public string Severity { get; set; } = "info";
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public IReadOnlyList<int> RowNumbers { get; set; } = Array.Empty<int>();
    }

    public sealed class SelectImportConceptDto
    {
        public Guid? ConceptId { get; set; }
        public bool SaveForFuture { get; set; }
    }

    public sealed class CreateImportConceptDto
    {
        public Guid GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public PersonalFinance.Domain.Ledger.Enums.ConceptNature Nature { get; set; }
        public bool SaveForFuture { get; set; }
    }

    public sealed class SelectImportGroupConceptDto
    {
        public string NormalizedDescription { get; set; } = string.Empty;
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
        /// <summary>Movimientos que estaban pendientes y el extracto ya muestra completados.</summary>
        public int ConfirmedPendingRows { get; set; }
        public IReadOnlyList<string> Problems { get; set; } = Array.Empty<string>();
    }

    public sealed class ImportChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public sealed class ImportChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<ImportChatMessageDto> History { get; set; } = Array.Empty<ImportChatMessageDto>();
    }

    public sealed class ImportChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public int AppliedChanges { get; set; }
        public IReadOnlyList<string> Unrecognized { get; set; } = Array.Empty<string>();
    }
}
