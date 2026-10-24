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
        /// <summary>Planned entry this row will be deducted from when the batch is applied (always serialized, null if none).</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public Guid? PlannedEntryId { get; set; }
        public IReadOnlyList<ImportRowAllocationDto> Allocations { get; set; } = Array.Empty<ImportRowAllocationDto>();
    }

    /// <summary>
    /// Open planned entry («previsto») that rows of an import can be deducted from.
    /// Nullable fields are always serialized so the client sees an explicit null.
    /// </summary>
    public sealed class ImportPlannedCandidateDto
    {
        public Guid Id { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public Guid? ConceptId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public string? ConceptName { get; set; }
        /// <summary>"in" or "out".</summary>
        public string Direction { get; set; } = string.Empty;
        public DateOnly DueDate { get; set; }
        /// <summary>Always positive; the sign is given by <see cref="Direction"/>.</summary>
        public decimal ForecastAmount { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public Guid? AccountId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public string? AccountName { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public string? Description { get; set; }
        /// <summary>Sum of the absolute amounts of this batch's rows linked to the entry.</summary>
        public decimal LinkedAmount { get; set; }
        /// <summary>ForecastAmount minus LinkedAmount; negative when the rows exceed the forecast.</summary>
        public decimal RemainingAfterLinks { get; set; }
        public IReadOnlyList<Guid> LinkedRowIds { get; set; } = Array.Empty<Guid>();
    }

    public sealed class SetImportRowPlannedEntryDto
    {
        /// <summary>Planned entry to deduct the row from; null removes the link.</summary>
        public Guid? PlannedEntryId { get; set; }
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
        /// <summary>Siempre 0: el asistente sólo propone y el usuario confirma cada cambio.</summary>
        public int AppliedChanges { get; set; }
        public IReadOnlyList<string> Unrecognized { get; set; } = Array.Empty<string>();
        public IReadOnlyList<ImportChatProposedActionDto> ProposedActions { get; set; } = Array.Empty<ImportChatProposedActionDto>();
    }

    /// <summary>Cambio propuesto por el asistente; no se aplica hasta que el usuario lo confirma.</summary>
    public sealed class ImportChatProposedActionDto
    {
        /// <summary>
        /// "set_row_concept", "create_concept", "link_planned", "unlink_planned",
        /// "skip_planned", "update_planned" o "create_reminder".
        /// </summary>
        public string Type { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public IReadOnlyList<Guid> RowIds { get; set; } = Array.Empty<Guid>();
        public Guid? ConceptId { get; set; }
        public string? ConceptName { get; set; }
        public string? Name { get; set; }
        public Guid? GroupId { get; set; }
        public string? GroupName { get; set; }
        public PersonalFinance.Domain.Ledger.Enums.ConceptNature? Nature { get; set; }
        public bool SaveForFuture { get; set; }
        /// <summary>Planned entry the action works on (link_planned, skip_planned, update_planned).</summary>
        public Guid? PlannedEntryId { get; set; }
        /// <summary>Concept name of that planned entry, for display.</summary>
        public string? PlannedEntryName { get; set; }
        /// <summary>New planned amount (update_planned) or reminder amount (create_reminder).</summary>
        public decimal? Amount { get; set; }
        /// <summary>New planned date (update_planned) or reminder date (create_reminder).</summary>
        public DateOnly? DueDate { get; set; }
        public string? Title { get; set; }
        /// <summary>"none", "weekly", "monthly" or "yearly" (create_reminder).</summary>
        public string? Repeat { get; set; }
        public int? NotifyDaysBefore { get; set; }
        public string? Notes { get; set; }
        // create_reminder: true = a cobro (money comes in), so the forecast adds it.
        public bool? IsIncome { get; set; }
    }
}
