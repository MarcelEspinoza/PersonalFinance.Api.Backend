// Controllers/AnalyticsController.cs
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Services.Contracts;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analytics;

    public AnalyticsController(IAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthly(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] Guid? bankId,
        CancellationToken ct)
    {
        try
        {
            // Defaults si faltan: mes/año actuales (o devuelve 400 si prefieres estricta)
            var now = DateTime.UtcNow;
            int y = year ?? now.Year;
            int m = month ?? now.Month;

            if (m < 1 || m > 12)
                return BadRequest("Query param 'month' debe estar entre 1 y 12.");
            if (y < 1900 || y > 2100)
                return BadRequest("Query param 'year' no es válido.");

            var dto = await _analytics.GetMonthlyAsync(y, m, bankId, ct);
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            // Parámetros inválidos → 400
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            // Loguea el error y devuelve 500 con un requestId
            var rid = HttpContext.TraceIdentifier;
            Console.Error.WriteLine($"[MonthlyAnalytics][{rid}] {ex}");
            return StatusCode(500, new { message = "Internal error", requestId = rid });
        }
    }


}
