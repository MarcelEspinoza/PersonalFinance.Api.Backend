using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Services.Contracts;


namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReconciliationsController : ControllerBase
    {
        private readonly IReconciliationService _service;

        public ReconciliationsController(IReconciliationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetForMonth([FromQuery] int year, [FromQuery] int month)
        {
            var list = await _service.GetForMonthAsync(year, month);
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateReconciliationDto dto)
        {
            var rec = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetForMonth), new { year = rec!.Year, month = rec!.Month }, rec);
        }

        [HttpGet("suggest")]
        public async Task<IActionResult> Suggest([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? bankId)
        {
            var suggestion = await _service.SuggestAsync(year, month, bankId);
            return Ok(suggestion);
        }

        [HttpPost("{id:guid}/mark")]
        public async Task<IActionResult> MarkReconciled(Guid id)
        {
            var ok = await _service.MarkReconciledAsync(id);
            if (!ok)
            {
                var recon = await _service.GetForMonthAsync(DateTime.UtcNow.Year, DateTime.UtcNow.Month); // not ideal: but we return a simple message
                return BadRequest(new { success = false, message = "No se puede marcar como conciliado: la diferencia entre el sistema y el saldo de cierre no es 0. Revisa las sugerencias y asegura que las partidas cuadren a 0." });
            }
            return Ok(new { success = true, message = "Conciliación marcada como reconciliada" });
        }
    }
}