using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Commands.CloseMonth;
using PersonalFinance.Api.Features.Ledger.Commands.ReopenMonth;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Ledger.Queries.GetMonthlySummary;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/periods")]
    public class PeriodsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public PeriodsController(IMediator mediator) => _mediator = mediator;

        /// <summary>Cuadro del mes. Lo abre si aún no existía.</summary>
        [HttpGet("{year:int}/{month:int}")]
        public async Task<ActionResult<MonthlySummaryDto>> Get(int year, int month, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new GetMonthlySummaryQuery(userId.Value, year, month), ct));
        }

        [HttpPost("{year:int}/{month:int}/close")]
        public async Task<ActionResult<MonthlySummaryDto>> Close(
            int year, int month, [FromBody] CloseMonthDto? dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var command = new CloseMonthCommand(userId.Value, year, month, dto ?? new CloseMonthDto());

            return Ok(await _mediator.Send(command, ct));
        }

        [HttpPost("{year:int}/{month:int}/reopen")]
        public async Task<ActionResult<MonthlySummaryDto>> Reopen(int year, int month, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new ReopenMonthCommand(userId.Value, year, month), ct));
        }
    }
}
