using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IBudgetService
    {
        Task<List<Budget>> GetForMonthAsync(
            int year,
            int month,
            CancellationToken ct = default);

        Task<Budget> CreateAsync(
            Budget budget,
            CancellationToken ct = default);

        Task UpdateAsync(
            Guid id,
            Budget budget,
            CancellationToken ct = default);

        Task DisableAsync(
            Guid id,
            CancellationToken ct = default);
    }
}
