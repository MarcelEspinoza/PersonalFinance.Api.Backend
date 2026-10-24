namespace PersonalFinance.Api.Features.Settlements
{
    /// <summary>Persona con la que se comparten gastos.</summary>
    public sealed record SettlementPersonDto(
        Guid Id,
        string Name,
        string? PhoneNumber,
        int OpenSettlements,
        string? BankName = null);

    /// <param name="BankName">Cómo sale su nombre en el banco, para cazar sus transferencias al importar.</param>
    public sealed record SavePersonDto(string Name, string? PhoneNumber, string? BankName = null);

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
        decimal Pending,
        decimal PaymentsSent = 0m);

    /// <summary>
    /// Cómo cuenta la liquidación en la previsión: cuánto, en qué sentido y qué día;
    /// o por qué no cuenta (<see cref="Reason"/>).
    /// </summary>
    public sealed record SettlementForecastInfoDto(
        bool Counts,
        string? Direction,
        decimal Amount,
        DateOnly? Date,
        string? Reason);

    public sealed record SettlementLoanDto(
        Guid Id,
        string Name,
        decimal OutstandingAmount,
        string Status,
        decimal AmountInSettlement);

    public sealed record SettlementLoanOptionDto(
        Guid Id,
        string Name,
        decimal OutstandingAmount,
        string Status);

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
        Guid? LinkedLoanId,
        SettlementLoanDto? LinkedLoan,
        SettlementTotalsDto Totals,
        IReadOnlyList<SettlementLineDto> Lines,
        string Message,
        string? WhatsAppUrl,
        DateOnly? ExpectedDate = null,
        SettlementForecastInfoDto? Forecast = null);

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
        string? ClosingNote,
        DateOnly? ExpectedDate = null);

    /// <param name="ExpectedDate">Día en que se espera cobrar o pagar. Si no viene, dentro de 7 días.</param>
    public sealed record MarkSentDto(DateOnly? ExpectedDate);

    public sealed record CloseSettlementLoanDto(string Resolution);
    public sealed record LinkSettlementLoanDto(Guid? LoanId);

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
