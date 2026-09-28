using PersonalFinance.Api.Models.Dtos.Dashboard;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IDashboardService
    {
        Task<DashboardProjectionResult> GetFutureProjectionAsync(
            int? year = null,
            int? month = null,
            CancellationToken ct = default);
    }
}
