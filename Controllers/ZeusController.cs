using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Zeus;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/zeus")]
    public sealed class ZeusController(ZeusService service) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<ZeusSummaryDto>> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.GetAsync(userId.Value, ct));
        }

        [HttpGet("expenses")]
        public async Task<ActionResult<IReadOnlyList<ZeusExpenseDto>>> Expenses(
            [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.ExpensesAsync(userId.Value, year, month, ct));
        }

        [HttpPost("passes")]
        public async Task<ActionResult<ZeusSummaryDto>> Buy(BuyZeusPassDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.BuyAsync(userId.Value, dto, ct));
        }

        [HttpDelete("passes/{id:guid}")]
        public async Task<ActionResult<ZeusSummaryDto>> RemovePass(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.RemovePassAsync(userId.Value, id, ct));
        }

        [HttpPost("usage")]
        public async Task<ActionResult<ZeusSummaryDto>> AddUsage(SaveZeusUsageDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.SaveUsageAsync(userId.Value, null, dto, ct));
        }

        [HttpPut("usage/{id:guid}")]
        public async Task<ActionResult<ZeusSummaryDto>> UpdateUsage(Guid id, SaveZeusUsageDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.SaveUsageAsync(userId.Value, id, dto, ct));
        }

        [HttpDelete("usage/{id:guid}")]
        public async Task<ActionResult<ZeusSummaryDto>> RemoveUsage(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.RemoveUsageAsync(userId.Value, id, ct));
        }
    }
}
