using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Settlements;

namespace PersonalFinance.Api.Controllers
{
    /// <summary>
    /// Cuentas de gastos compartidos con mamá y Vane: qué le toca a cada una,
    /// cuánto ha pagado ya y el mensaje de WhatsApp con el resumen.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/settlements")]
    public class SettlementsController : ControllerBase
    {
        private readonly ISettlementService _service;

        public SettlementsController(ISettlementService service) => _service = service;

        [HttpGet("people")]
        public async Task<ActionResult<IReadOnlyList<SettlementPersonDto>>> People(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.GetPeopleAsync(userId.Value, ct));
        }

        [HttpPost("people")]
        public async Task<ActionResult<SettlementPersonDto>> CreatePerson(
            [FromBody] SavePersonDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.SavePersonAsync(userId.Value, null, dto, ct));
        }

        [HttpPut("people/{id:guid}")]
        public async Task<ActionResult<SettlementPersonDto>> UpdatePerson(
            Guid id, [FromBody] SavePersonDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.SavePersonAsync(userId.Value, id, dto, ct));
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<SettlementSummaryDto>>> List(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.ListAsync(userId.Value, ct));
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<SettlementDetailDto>> Get(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.GetAsync(userId.Value, id, ct));
        }

        [HttpGet("loans")]
        public async Task<ActionResult<IReadOnlyList<SettlementLoanOptionDto>>> LoanOptions(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.GetLoanOptionsAsync(userId.Value, ct));
        }

        [HttpPost]
        public async Task<ActionResult<SettlementDetailDto>> Create(
            [FromBody] CreateSettlementDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.CreateAsync(userId.Value, dto, ct));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<SettlementDetailDto>> Update(
            Guid id, [FromBody] UpdateSettlementDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.UpdateAsync(userId.Value, id, dto, ct));
        }

        [HttpPut("{id:guid}/loan")]
        public async Task<ActionResult<SettlementDetailDto>> LinkLoan(
            Guid id, [FromBody] LinkSettlementLoanDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.LinkLoanAsync(userId.Value, id, dto.LoanId, ct));
        }

        [HttpPost("{id:guid}/loan/close")]
        public async Task<ActionResult<SettlementDetailDto>> CloseLoan(
            Guid id, [FromBody] CloseSettlementLoanDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.CloseLinkedLoanAsync(userId.Value, id, dto.Resolution, ct));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            await _service.DeleteAsync(userId.Value, id, ct);
            return NoContent();
        }

        [HttpGet("{id:guid}/candidates")]
        public async Task<ActionResult<IReadOnlyList<SettlementCandidateDto>>> Candidates(
            Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.GetCandidatesAsync(userId.Value, id, ct));
        }

        [HttpPost("{id:guid}/lines")]
        public async Task<ActionResult<SettlementDetailDto>> AddLine(
            Guid id, [FromBody] SaveLineDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.AddLineAsync(userId.Value, id, dto, ct));
        }

        [HttpPut("{id:guid}/lines/{lineId:guid}")]
        public async Task<ActionResult<SettlementDetailDto>> UpdateLine(
            Guid id, Guid lineId, [FromBody] SaveLineDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.UpdateLineAsync(userId.Value, id, lineId, dto, ct));
        }

        [HttpDelete("{id:guid}/lines/{lineId:guid}")]
        public async Task<ActionResult<SettlementDetailDto>> DeleteLine(
            Guid id, Guid lineId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.DeleteLineAsync(userId.Value, id, lineId, ct));
        }

        [HttpPost("{id:guid}/lines/from-entry")]
        public async Task<ActionResult<SettlementDetailDto>> AddFromEntry(
            Guid id, [FromBody] AddFromEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.AddFromEntryAsync(userId.Value, id, dto, ct));
        }

        /// <summary>Atajo desde el listado de movimientos: "esto es de mamá".</summary>
        [HttpPost("assign-entry")]
        public async Task<ActionResult<SettlementDetailDto>> AssignEntry(
            [FromBody] AssignEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.AssignEntryAsync(userId.Value, dto, ct));
        }

        [HttpPost("{id:guid}/sent")]
        public async Task<ActionResult<SettlementDetailDto>> MarkSent(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.MarkSentAsync(userId.Value, id, ct));
        }

        [HttpPost("{id:guid}/reopen")]
        public async Task<ActionResult<SettlementDetailDto>> Reopen(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.ReopenAsync(userId.Value, id, ct));
        }
    }
}
