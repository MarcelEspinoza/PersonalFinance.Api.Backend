using PersonalFinance.Api.Models.Dtos.Monthly;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IAnalyticsService
    {
        Task<MonthlyInsightsDto> GetMonthlyAsync(int year, int month, Guid? bankId = null, CancellationToken ct = default);
    }
}