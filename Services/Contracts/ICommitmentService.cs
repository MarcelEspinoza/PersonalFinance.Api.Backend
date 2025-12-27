using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface ICommitmentService
    {
        Task<List<FinancialCommitment>> GetForMonthAsync(
            int year,
            int month,
            CancellationToken ct = default);

        Task<FinancialCommitment> CreateAsync(
            FinancialCommitment commitment,
            CancellationToken ct = default);

        Task UpdateAsync(
            Guid id,
            FinancialCommitment commitment,
            CancellationToken ct = default);

        Task DisableAsync(
            Guid id,
            CancellationToken ct = default);
    }
}
