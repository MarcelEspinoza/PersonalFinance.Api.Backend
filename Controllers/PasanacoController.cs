using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/pasanacos")]
    [Authorize]
    public class PasanacoController : ControllerBase
    {
        private readonly IPasanacoService _service;

        public PasanacoController(IPasanacoService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var result = await _service.GetAllAsync(userId.Value);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var result = await _service.GetByIdAsync(id, userId.Value);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePasanacoDto dto)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var created = await _service.CreateAsync(userId.Value, dto);
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdatePasanacoDto dto)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            await _service.UpdateAsync(id, userId.Value, dto);
            return NoContent();
        }

        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(string id)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            await _service.CompleteAsync(id, userId.Value);
            return NoContent();
        }

        [HttpPost("{id}/reopen")]
        public async Task<IActionResult> Reopen(string id)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            await _service.ReopenAsync(id, userId.Value);
            return NoContent();
        }

        // Delete with related-summary check and optional force flag
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] bool force = false)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            // Check related summary (also verifies the pasanaco belongs to the caller)
            var summary = await _service.GetRelatedSummaryAsync(id, userId.Value);

            if (!force && summary.HasAnyRelated)
            {
                return BadRequest(new
                {
                    message = "No se puede eliminar el pasanaco porque existen registros relacionados.",
                    related = summary
                });
            }

            if (force && !User.IsInRole("Admin"))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tienes permisos para forzar el borrado." });
            }

            if (force)
            {
                await _service.DeletePasanacoCascadeAsync(id, userId.Value);
                return NoContent();
            }

            // normal delete
            await _service.DeleteAsync(id, userId.Value);
            return NoContent();
        }

        [HttpGet("{id}/participants")]
        public async Task<IActionResult> GetParticipants(string id)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var result = await _service.GetParticipantsAsync(id, userId.Value);
            return Ok(result);
        }

        [HttpPost("{id}/participants")]
        public async Task<IActionResult> AddParticipant(string id, [FromBody] CreateParticipantDto dto)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            await _service.AddParticipantAsync(id, userId.Value, dto);
            return Ok();
        }

        [HttpDelete("{id}/participants/{participantId}")]
        public async Task<IActionResult> DeleteParticipant(string id, string participantId)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            await _service.DeleteParticipantAsync(id, participantId, userId.Value);
            return NoContent();
        }

        [HttpGet("{id}/payments")]
        public async Task<IActionResult> GetPayments(string id, [FromQuery] int month, [FromQuery] int year)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var result = await _service.GetPaymentsAsync(id, userId.Value, month, year);
            return Ok(result);
        }

        [HttpPost("{id}/generate-payments")]
        public async Task<IActionResult> GeneratePayments(string id, [FromBody] GeneratePaymentsDto dto)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            await _service.GeneratePaymentsAsync(id, userId.Value, dto.Month, dto.Year);
            return Ok();
        }

        [HttpPost("{id}/advance")]
        public async Task<IActionResult> AdvanceRound(string id, [FromQuery] bool createLoans = false)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var success = await _service.AdvanceRoundAsync(id, userId.Value, createLoans);
            if (!success)
                return BadRequest("No se puede avanzar: hay pagos pendientes o ya estás en la última ronda.");

            return Ok("Ronda avanzada correctamente");
        }

        [HttpPost("{id}/retreat")]
        public async Task<IActionResult> RetreatRound(string id)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var success = await _service.RetreatRoundAsync(id, userId.Value);
            if (!success) return BadRequest("No se puede retroceder desde la primera ronda.");
            return Ok("Ronda retrocedida correctamente");
        }

        [HttpPost("{id}/participants/{participantId}/loan")]
        public async Task<IActionResult> CreateLoanForParticipant(
            string id,
            string participantId,
            [FromBody] CreateLoanForParticipantDto dto)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var loan = await _service.CreateLoanForParticipantAsync(
                id,
                participantId,
                dto.Amount,
                userId.Value,
                dto.Note);
            return Ok(loan);
        }

        [HttpPost("payments/{paymentId}/mark-paid")]
        public async Task<IActionResult> MarkPaymentAsPaid(Guid paymentId)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();

            var success = await _service.MarkPaymentAsPaidAsync(paymentId, userId.Value);
            if (!success) return BadRequest("No se pudo marcar como pagado");
            return Ok("Pago registrado y ingreso creado");
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return null;
            if (!Guid.TryParse(userIdClaim, out var userId)) return null;
            return userId;
        }

    }

}