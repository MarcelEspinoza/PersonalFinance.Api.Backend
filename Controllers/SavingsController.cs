using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Models.Dtos.Saving;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SavingsController : ControllerBase
    {
        private readonly ISavingService _savingService;

        public SavingsController(ISavingService savingService)
        {
            _savingService = savingService;
        }

        // El userId de la query se mantiene por compatibilidad, pero se ignora: siempre se usa el del token.
        [HttpPost("plan")]
        public async Task<IActionResult> PlanSavings(Guid? userId, [FromBody] PlanSavingsDto dto, CancellationToken ct)
        {
            var currentUserId = User.GetUserId();
            if (currentUserId == null) return Unauthorized();

            await _savingService.PlanSavingsAsync(currentUserId.Value, dto, ct);
            return Ok(new { message = "Plan de ahorro creado correctamente" });
        }
    }

}
