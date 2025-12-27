using PersonalFinance.Api.Models.Dtos.Budgets;

public interface IBudgetMatchingService
{
    Task<List<BudgetStatusDto>> GetMonthlyStatusAsync(
        int year,
        int month,
        CancellationToken ct = default);
}
