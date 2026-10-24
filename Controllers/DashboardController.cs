using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Features.ServiceSavings;
using PersonalFinance.Api.Services.Contracts;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IMonthAdvisorService _advisor;
    private readonly IServiceSavingsService _serviceSavings;

    public DashboardController(
        IDashboardService dashboardService,
        IMonthAdvisorService advisor,
        IServiceSavingsService serviceSavings)
    {
        _dashboardService = dashboardService;
        _advisor = advisor;
        _serviceSavings = serviceSavings;
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

    /// <summary>"Ahorra en tus servicios": latest review, the user's notes and the analyses left today.</summary>
    [HttpGet("services")]
    [Authorize]
    public async Task<ActionResult<ServiceSavingsStateDto>> GetServiceSavings(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _serviceSavings.GetStateAsync(userId.Value, ct));
    }

    /// <summary>
    /// Starts a new review in the background (the client polls GET services).
    /// Answers 429 when the daily limit is reached.
    /// </summary>
    [HttpPost("services/analyze")]
    [Authorize]
    public async Task<ActionResult<ServiceSavingsStateDto>> AnalyzeServiceSavings(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _serviceSavings.StartAnalysisAsync(userId.Value, ct);
        if (result.LimitMessage is not null)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = result.LimitMessage });

        return Ok(result.State);
    }

    /// <summary>Saves the user's comment or hidden flag for a service; both feed the next review.</summary>
    [HttpPut("services/notes")]
    [Authorize]
    public async Task<ActionResult<List<ServiceNoteDto>>> SaveServiceNote(
        [FromBody] ServiceNoteRequestDto request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (request is null) return BadRequest(new { message = "Faltan los datos de la nota." });

        return Ok(await _serviceSavings.SaveNoteAsync(userId.Value, request, ct));
    }

}
