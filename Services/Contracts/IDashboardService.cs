using PersonalFinance.Api.Models.Dtos.Dashboard;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IDashboardService
    {
        Task<(
            List<MonthlyProjectionDto> monthlyData,
            SummaryDto summary,
            DashboardAlertsDto alerts,
            List<DashboardAccountBalanceDto> accounts
        )> GetFutureProjectionAsync(CancellationToken ct = default);
    }
}
