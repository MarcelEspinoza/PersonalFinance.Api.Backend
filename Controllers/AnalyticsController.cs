using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly AnalyticsService _analytics;

        public AnalyticsController(AnalyticsService analytics)
        {
            _analytics = analytics;
        }

        [HttpGet("monthly")]
        public async Task<IActionResult> GetMonthly(
            [FromQuery] int year,
            [FromQuery] int month,
            [FromQuery] Guid? bankId,
            CancellationToken ct)
        {
            if (year < 2000 || month < 1 || month > 12)
                return BadRequest("Parámetros de fecha inválidos.");

            var result = await _analytics.GetMonthlyAsync(year, month, bankId, ct);
            return Ok(result);
        }
    }
}
