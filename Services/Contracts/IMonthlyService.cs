using PersonalFinance.Api.Models.Dtos.Monthly;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IMonthlyService
    {
        Task<MonthlyDataResponseDto> GetMonthDataAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    }
}
