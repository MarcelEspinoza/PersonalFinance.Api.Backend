namespace PersonalFinance.Api.Services.Contracts
{
    using PersonalFinance.Api.Models.Dtos.Dashboard;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IDashboardService
    {
        Task<(List<MonthlyProjectionDto> monthlyData, SummaryDto summary)> GetFutureProjectionAsync(Guid userId);
    }

}
