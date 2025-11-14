using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class TransfersController : ControllerBase
    {
        private readonly ITransferService _transferService;

        public TransfersController(ITransferService transferService)
        {
            _transferService = transferService;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTransferRequest dto, CancellationToken ct)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var uId)) return Unauthorized();

            try
            {
                var transferId = await _transferService.CreateTransferAsync(uId, dto, ct);
                return CreatedAtAction(nameof(GetByTransferId), new { id = transferId }, new { transferId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // log ex
                return StatusCode(500, "Error creating transfer");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetByTransferId(string id, CancellationToken ct)
        {
            var result = await _transferService.GetByTransferIdAsync(id, ct);
            return Ok(result);
        }
    }
}