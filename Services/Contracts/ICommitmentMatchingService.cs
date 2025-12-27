using PersonalFinance.Api.Models.Dtos.Commitments;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface ICommitmentMatchingService
    {
        Task<List<CommitmentStatusDto>> GetMonthlyStatusAsync(
            int year,
            int month,
            CancellationToken ct = default);
    }
}
