using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MonthlyController : ControllerBase
    {
        private readonly IMonthlyService _monthlyService;

        public MonthlyController(IMonthlyService monthlyService)
        {
            _monthlyService = monthlyService;
        }

        // El userId de la ruta se mantiene por compatibilidad, pero se ignora: siempre se usa el del token.
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetMonthData(Guid userId, [FromQuery] DateTime startDate, [FromQuery] DateTime endDate, CancellationToken ct)
        {
            var currentUserId = User.GetUserId();
            if (currentUserId == null) return Unauthorized();

            var result = await _monthlyService.GetMonthDataAsync(currentUserId.Value, startDate, endDate, ct);
            return Ok(result);
        }
    }
}
