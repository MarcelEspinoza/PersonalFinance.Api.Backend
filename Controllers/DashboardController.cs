using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Services.Contracts;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("projection")]
    [Authorize]
    public async Task<IActionResult> GetProjection(CancellationToken ct)
    {
        var (monthlyData, summary, alerts) =
            await _dashboardService.GetFutureProjectionAsync(ct);

        return Ok(new
        {
            monthlyData,
            summary,
            alerts
        });
    }

}
