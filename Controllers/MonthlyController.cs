using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetMonthData(Guid userId, [FromQuery] DateTime startDate, [FromQuery] DateTime endDate, CancellationToken ct)
        {
            var result = await _monthlyService.GetMonthDataAsync(userId, startDate, endDate, ct);
            return Ok(result);
        }
    }
}
