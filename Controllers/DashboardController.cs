using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

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
    public async Task<IActionResult> GetProjection()
    {
        var (monthlyData, summary, alerts) =
            await _dashboardService.GetFutureProjectionAsync();

        return Ok(new
        {
            monthlyData,
            summary,
            alerts
        });
    }



}
