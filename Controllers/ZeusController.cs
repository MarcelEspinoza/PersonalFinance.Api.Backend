using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Zeus;
using PersonalFinance.Api.Features.Ledger.Dtos;

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
            [FromQuery] int year, [FromQuery] int month, [FromQuery] string? provider, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.ExpensesAsync(userId.Value, year, month, provider, ct));
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

        [HttpPost("care")]
        public async Task<ActionResult<ZeusSummaryDto>> AddCare(SaveZeusCareStayDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.SaveCareAsync(userId.Value, null, dto, ct));
        }

        [HttpPut("care/{id:guid}")]
        public async Task<ActionResult<ZeusSummaryDto>> UpdateCare(Guid id, SaveZeusCareStayDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.SaveCareAsync(userId.Value, id, dto, ct));
        }

        [HttpDelete("care/{id:guid}")]
        public async Task<ActionResult<ZeusSummaryDto>> RemoveCare(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.RemoveCareAsync(userId.Value, id, ct));
        }

        [HttpPost("care/payments")]
        public async Task<ActionResult<ZeusSummaryDto>> PayCare(PayZeusCareDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.PayCareAsync(userId.Value, dto, ct));
        }

        [HttpDelete("care/{id:guid}/payment")]
        public async Task<ActionResult<ZeusSummaryDto>> UnlinkCarePayment(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.UnlinkCarePaymentAsync(userId.Value, id, ct));
        }

        [HttpPost("care/payments/{entryId:guid}/confirm")]
        public async Task<ActionResult<ZeusSummaryDto>> ConfirmCarePayment(
            Guid entryId, ConfirmLedgerEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.ConfirmCarePaymentAsync(userId.Value, entryId, dto, ct));
        }
    }
}
