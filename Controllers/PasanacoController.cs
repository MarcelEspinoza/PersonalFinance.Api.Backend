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
            var result = await _service.GetAllAsync();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var result = await _service.GetByIdAsync(id);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePasanacoDto dto)
        {
            var created = await _service.CreateAsync(dto);
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdatePasanacoDto dto)
        {
            await _service.UpdateAsync(id, dto);
            return NoContent();
        }

        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(string id)
        {
            await _service.CompleteAsync(id);
            return NoContent();
        }

        [HttpPost("{id}/reopen")]
        public async Task<IActionResult> Reopen(string id)
        {
            await _service.ReopenAsync(id);
            return NoContent();
        }

        // Delete with related-summary check and optional force flag
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] bool force = false)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            // Check related summary
            var summary = await _service.GetRelatedSummaryAsync(id);

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
                return Forbid("No tienes permisos para forzar el borrado.");
            }

            if (force)
            {
                await _service.DeletePasanacoCascadeAsync(id, userId.Value);
                return NoContent();
            }

            // normal delete
            await _service.DeleteAsync(id);
            return NoContent();
        }

        [HttpGet("{id}/participants")]
        public async Task<IActionResult> GetParticipants(string id)
        {
            var result = await _service.GetParticipantsAsync(id);
            return Ok(result);
        }

        [HttpPost("{id}/participants")]
        public async Task<IActionResult> AddParticipant(string id, [FromBody] CreateParticipantDto dto)
        {
            await _service.AddParticipantAsync(id, dto);
            return Ok();
        }

        [HttpDelete("{id}/participants/{participantId}")]
        public async Task<IActionResult> DeleteParticipant(string id, string participantId)
        {
            await _service.DeleteParticipantAsync(id, participantId);
            return NoContent();
        }

        [HttpGet("{id}/payments")]
        public async Task<IActionResult> GetPayments(string id, [FromQuery] int month, [FromQuery] int year)
        {
            var result = await _service.GetPaymentsAsync(id, month, year);
            return Ok(result);
        }

        [HttpPost("{id}/generate-payments")]
        public async Task<IActionResult> GeneratePayments(string id, [FromBody] GeneratePaymentsDto dto)
        {
            await _service.GeneratePaymentsAsync(id, dto.Month, dto.Year);
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
            var success = await _service.RetreatRoundAsync(id);
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