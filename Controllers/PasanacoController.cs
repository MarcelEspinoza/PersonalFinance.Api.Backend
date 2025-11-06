using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/pasanacos")]
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
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
        public async Task<IActionResult> AdvanceRound(string id)
        {
            var success = await _service.AdvanceRoundAsync(id);
            if (!success)
                return BadRequest("No se puede avanzar: hay pagos pendientes");

            return Ok("Ronda avanzada correctamente");
        }

        [HttpPost("payments/{paymentId}/mark-paid")]
        public async Task<IActionResult> MarkPaymentAsPaid(Guid paymentId)
        {
            var userId = GetCurrentUserId();
            var success = await _service.MarkPaymentAsPaidAsync(paymentId, userId!.Value);
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
