using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Services.Contracts;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IMonthAdvisorService _advisor;

    public DashboardController(IDashboardService dashboardService, IMonthAdvisorService advisor)
    {
        _dashboardService = dashboardService;
        _advisor = advisor;
    }

    [HttpPost("advice")]
    [Authorize]
    public async Task<ActionResult<MonthAdviceDto>> GetAdvice(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        return Ok(await _advisor.AdviseAsync(year, month, ct));
    }

    [HttpGet("projection")]
    [Authorize]
    public async Task<IActionResult> GetProjection(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] bool? includeVariableReserve,
        CancellationToken ct)
    {
        return Ok(await _dashboardService.GetFutureProjectionAsync(
            year, month, ct, includeVariableReserve ?? true));
    }

}
