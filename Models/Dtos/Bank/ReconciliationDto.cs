namespace PersonalFinance.Api.Models.Dtos.Bank
{
    public record ReconciliationDto(Guid Id, Guid BankId, int Year, int Month, decimal ClosingBalance, bool Reconciled, string? Notes, DateTime CreatedAt);
    public record CreateReconciliationDto(Guid BankId, int Year, int Month, decimal ClosingBalance, string? Notes);
    public record ReconciliationSuggestionDto(decimal SystemTotal, decimal ClosingBalance, decimal Difference, object? Details = null);
}
