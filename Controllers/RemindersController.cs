using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Reminders;

namespace PersonalFinance.Api.Controllers
{
    /// <summary>
    /// Recordatorios del usuario: la campana de la app los muestra cuando vencen
    /// y un proceso en segundo plano los envía por correo.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/reminders")]
    public class RemindersController : ControllerBase
    {
        private readonly IReminderService _service;

        public RemindersController(IReminderService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ReminderDto>>> List(
            [FromQuery] string? status, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.ListAsync(userId.Value, status, ct));
        }

        [HttpGet("due")]
        public async Task<ActionResult<DueRemindersDto>> Due(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.GetDueAsync(userId.Value, ct));
        }

        [HttpPost]
        public async Task<ActionResult<ReminderDto>> Create([FromBody] SaveReminderDto? dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return StatusCode(StatusCodes.Status201Created, await _service.CreateAsync(userId.Value, dto, ct));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<ReminderDto>> Update(
            Guid id, [FromBody] SaveReminderDto? dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.UpdateAsync(userId.Value, id, dto, ct));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            await _service.DeleteAsync(userId.Value, id, ct);
            return NoContent();
        }

        [HttpPost("{id:guid}/complete")]
        public async Task<ActionResult<ReminderDto>> Complete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.CompleteAsync(userId.Value, id, ct));
        }

        [HttpPost("{id:guid}/snooze")]
        public async Task<ActionResult<ReminderDto>> Snooze(
            Guid id, [FromBody] SnoozeReminderDto? dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            return Ok(await _service.SnoozeAsync(userId.Value, id, dto, ct));
        }
    }
}
