using PersonalFinance.Api.Models.Dtos.Dashboard;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IDashboardService
    {
        Task<(List<MonthlyProjectionDto> monthlyData, SummaryDto summary)>
            GetFutureProjectionAsync(CancellationToken ct = default);
    }
}
