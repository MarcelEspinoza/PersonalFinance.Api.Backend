namespace PersonalFinance.Api.Features.Settlements
{
    /// <summary>Persona con la que se comparten gastos.</summary>
    public sealed record SettlementPersonDto(
        Guid Id,
        string Name,
        string? PhoneNumber,
        int OpenSettlements);

    public sealed record SavePersonDto(string Name, string? PhoneNumber);

    public sealed record SettlementLineDto(
        Guid Id,
        string Kind,
        string Description,
        decimal Amount,
        decimal? FullAmount,
        Guid? LedgerEntryId,
        int SortOrder);

    public sealed record SettlementTotalsDto(
        decimal Charges,
        decimal Deductions,
        decimal Payments,
        decimal CarriedOver,
        decimal Pending);

    public sealed record SettlementSummaryDto(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string Title,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string Status,
        decimal Pending,
        int LineCount,
        DateTime? SentAt);

    public sealed record SettlementDetailDto(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string? PhoneNumber,
        string Title,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string Status,
        string? ClosingNote,
        decimal CarriedOverAmount,
        DateTime? SentAt,
        SettlementTotalsDto Totals,
        IReadOnlyList<SettlementLineDto> Lines,
        string Message,
        string? WhatsAppUrl);

    public sealed record CreateSettlementDto(
        Guid CounterpartyId,
        string? Title,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        decimal? CarriedOverAmount,
        string? ClosingNote);

    public sealed record UpdateSettlementDto(
        string? Title,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        decimal? CarriedOverAmount,
        string? ClosingNote);

    public sealed record SaveLineDto(
        string Kind,
        string Description,
        decimal Amount,
        decimal? FullAmount,
        Guid? LedgerEntryId);

    /// <summary>Movimiento del libro que se puede imputar a la liquidación.</summary>
    public sealed record SettlementCandidateDto(
        Guid LedgerEntryId,
        DateOnly Date,
        string Description,
        string ConceptName,
        decimal Amount,
        string Direction,
        bool AlreadyAdded);

    /// <summary>
    /// Imputa un movimiento del libro. Si se manda <see cref="SharePercent"/>
    /// o <see cref="Amount"/> se reparte; si no, va entero.
    /// </summary>
    public sealed record AddFromEntryDto(
        Guid LedgerEntryId,
        string? Kind,
        decimal? SharePercent,
        decimal? Amount);

    /// <summary>
    /// Atajo desde el listado de movimientos: manda el gasto a la liquidación
    /// abierta de esa persona, creándola si aún no existe.
    /// </summary>
    public sealed record AssignEntryDto(
        Guid CounterpartyId,
        Guid LedgerEntryId,
        string? Kind,
        decimal? SharePercent,
        decimal? Amount);
}
