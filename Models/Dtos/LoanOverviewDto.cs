namespace PersonalFinance.Api.Models.Dtos
{
    public sealed record SetLoansArchivedDto(Guid[] LoanIds, bool Archived);

    public sealed record LoanOverviewItemDto(
        Guid Id,
        string Source,
        string Title,
        string Direction,
        decimal RemainingAmount,
        DateOnly? NextDate,
        decimal? NextAmount,
        DateOnly? CompletionDate,
        string? CounterpartyName,
        string? AccountName);

    public sealed record LoanOverviewDto(
        decimal Payable,
        decimal Receivable,
        decimal DueThisMonth,
        decimal ExpectedThisMonth,
        DateOnly? LastPaymentDate,
        int IncompleteSchedules,
        int ArchivedLoans,
        decimal ArchivedRecordedBalance,
        IReadOnlyList<LoanOverviewItemDto> Items);
}
