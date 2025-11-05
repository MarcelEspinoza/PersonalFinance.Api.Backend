using PersonalFinance.Api.Models.Dtos.Saving;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface ISavingService
    {
        Task PlanSavingsAsync(Guid userId, PlanSavingsDto dto, CancellationToken ct = default);
        Task<decimal> GetSavingsForMonthAsync(Guid userId, DateTime month, CancellationToken ct = default);
    }

}
