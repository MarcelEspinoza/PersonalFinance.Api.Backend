using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Ledger.Queries.GetChartOfAccounts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/chart-of-accounts")]
    public class ChartOfAccountsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ChartOfAccountsController(IMediator mediator) => _mediator = mediator;

        [HttpGet]
        public async Task<ActionResult<ChartOfAccountsDto>> Get(
            [FromQuery] bool includeInactive, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _mediator.Send(new GetChartOfAccountsQuery(userId.Value, includeInactive), ct));
        }

        /// <summary>
        /// Crea el plan de cuentas del Excel para el usuario autenticado.
        /// Repetir la llamada no duplica nada.
        /// </summary>
        [HttpPost("seed")]
        public async Task<ActionResult<SeedChartOfAccountsResultDto>> Seed(
            [FromQuery] int? budgetYear, [FromQuery] int? budgetMonth, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var command = new SeedChartOfAccountsCommand(userId.Value, budgetYear, budgetMonth);

            return Ok(await _mediator.Send(command, ct));
        }
    }
}
