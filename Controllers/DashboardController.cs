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
    public async Task<IActionResult> GetProjection(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        return Ok(await _dashboardService.GetFutureProjectionAsync(year, month, ct));
    }

}
