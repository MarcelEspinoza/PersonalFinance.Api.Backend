using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.Saving;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SavingsController : ControllerBase
    {
        private readonly ISavingService _savingService;

        public SavingsController(ISavingService savingService)
        {
            _savingService = savingService;
        }

        [HttpPost("plan")]
        public async Task<IActionResult> PlanSavings(Guid userId, [FromBody] PlanSavingsDto dto, CancellationToken ct)
        {
            await _savingService.PlanSavingsAsync(userId, dto, ct);
            return Ok(new { message = "Plan de ahorro creado correctamente" });
        }
    }

}
