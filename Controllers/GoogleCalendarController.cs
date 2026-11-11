using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Calendar;

namespace PersonalFinance.Api.Controllers
{
    public sealed class ConnectGoogleCalendarDto
    {
        [Required]
        [StringLength(30)]
        public string ReturnPath { get; set; } = "/zeus";
    }

    [ApiController]
    [Authorize]
    [Route("api/calendar/google")]
    public sealed class GoogleCalendarController(GoogleCalendarService service) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<GoogleCalendarStateDto>> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(await service.GetStateAsync(userId.Value, ct));
        }

        [HttpPost("connect")]
        public async Task<IActionResult> Connect(ConnectGoogleCalendarDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            return Ok(new { authorizationUrl = await service.StartConnectionAsync(userId.Value, dto.ReturnPath, ct) });
        }

        [AllowAnonymous]
        [HttpGet("callback")]
        public async Task<IActionResult> Callback(
            [FromQuery, StringLength(2048)] string? state,
            [FromQuery, StringLength(8192)] string? code,
            [FromQuery, StringLength(300)] string? error, CancellationToken ct) =>
            Redirect(await service.CompleteConnectionAsync(state, code, error, ct));

        [HttpPost("retry")]
        public async Task<ActionResult<GoogleCalendarStateDto>> Retry(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            await service.RetryAsync(userId.Value, ct);
            return Ok(await service.GetStateAsync(userId.Value, ct));
        }

        [HttpDelete]
        public async Task<ActionResult<GoogleCalendarStateDto>> Disconnect(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            await service.DisconnectAsync(userId.Value, ct);
            return Ok(await service.GetStateAsync(userId.Value, ct));
        }
    }
}
