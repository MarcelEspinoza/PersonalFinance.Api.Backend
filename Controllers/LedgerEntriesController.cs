using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Commands.ConfirmLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.DeleteLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.SkipLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.UnconfirmLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.UpdateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Dtos;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/ledger-entries")]
    public class LedgerEntriesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public LedgerEntriesController(IMediator mediator) => _mediator = mediator;

        [HttpPost]
        public async Task<ActionResult<MonthlyEntryDto>> Create(
            [FromBody] CreateLedgerEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new CreateLedgerEntryCommand(userId.Value, dto), ct));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<MonthlyEntryDto>> Update(
            Guid id, [FromBody] UpdateLedgerEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new UpdateLedgerEntryCommand(userId.Value, id, dto), ct));
        }

        [HttpPost("{id:guid}/confirm")]
        public async Task<ActionResult<MonthlyEntryDto>> Confirm(
            Guid id, [FromBody] ConfirmLedgerEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new ConfirmLedgerEntryCommand(userId.Value, id, dto), ct));
        }

        [HttpPost("{id:guid}/unconfirm")]
        public async Task<ActionResult<MonthlyEntryDto>> Unconfirm(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new UnconfirmLedgerEntryCommand(userId.Value, id), ct));
        }

        [HttpPost("{id:guid}/skip")]
        public async Task<ActionResult<MonthlyEntryDto>> Skip(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new SkipLedgerEntryCommand(userId.Value, id, true), ct));
        }

        /// <summary>Devuelve al ciclo normal un asiento que se había descartado.</summary>
        [HttpPost("{id:guid}/unskip")]
        public async Task<ActionResult<MonthlyEntryDto>> Unskip(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new SkipLedgerEntryCommand(userId.Value, id, false), ct));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            await _mediator.Send(new DeleteLedgerEntryCommand(userId.Value, id), ct);

            return NoContent();
        }
    }
}
