using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
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
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _advisor.AdviseAsync(userId.Value, year, month, ct));
    }

    /// <summary>Responde a un consejo concreto; la conversación alimenta los análisis siguientes.</summary>
    [HttpPost("advice/reply")]
    [Authorize]
    public async Task<ActionResult<AdviceReplyResultDto>> ReplyToAdvice(
        [FromBody] AdviceReplyRequestDto request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Escribe una respuesta.");
        if (request.Month is < 1 or > 12) return BadRequest("Mes no válido.");

        return Ok(await _advisor.ReplyAsync(userId.Value, request, ct));
    }

    [HttpGet("advice/notes")]
    [Authorize]
    public async Task<ActionResult<List<AdviceNoteDto>>> GetAdviceNotes(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _advisor.GetNotesAsync(userId.Value, year, month, ct));
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
