using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/loans")]
    [Authorize] // Require authenticated user for all loan endpoints
    public class LoansController : ControllerBase
    {
        private readonly ILoanService _loanService;
        private readonly IPasanacoService pasanacoService;

        public LoansController(ILoanService loanService, IPasanacoService pasanacoService)
        {
            _loanService = loanService;
            this.pasanacoService = pasanacoService;
        }


        // userId en la query se mantiene por compatibilidad, pero se ignora: siempre se usa el del token.
        [HttpGet]
        public async Task<IActionResult> GetLoans([FromQuery] Guid? userId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return Unauthorized();

            var loans = await _loanService.GetLoansAsync(currentUserId.Value);
            return Ok(loans);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLoan(Guid id)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var loan = await _loanService.GetLoanAsync(id, userId.Value);
            if (loan == null) return NotFound();
            return Ok(loan);
        }

        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview(
            [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();
            var today = DateTime.UtcNow;
            var selectedYear = year ?? today.Year;
            var selectedMonth = month ?? today.Month;
            if (selectedYear is < 2000 or > 2100 || selectedMonth is < 1 or > 12)
                return BadRequest(new { message = "Indica un mes y un año válidos." });
            return Ok(await _loanService.GetOverviewAsync(userId.Value, selectedYear, selectedMonth, ct));
        }

        [HttpPost("archive")]
        public async Task<IActionResult> SetArchived(
            [FromBody] SetLoansArchivedDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Unauthorized();
            if (dto.LoanIds is not { Length: > 0 and <= 1000 } || dto.LoanIds.Any(id => id == Guid.Empty))
                return BadRequest(new { message = "Selecciona los préstamos que quieres archivar o recuperar." });
            var changed = await _loanService.SetArchivedAsync(
                userId.Value, dto.LoanIds.Distinct().ToArray(), dto.Archived, ct);
            return changed ? NoContent() :
                NotFound(new { message = "Algún préstamo ya no está disponible. Actualiza la lista." });
        }

        [HttpPost]
        public async Task<IActionResult> CreateLoan([FromBody] LoanDto loan)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            loan.UserId = userId.Value;
            var created = await _loanService.CreateLoanAsync(loan);
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLoan(Guid id, [FromBody] LoanDto loan)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            await _loanService.UpdateLoanAsync(id, userId.Value, loan);
            return NoContent();
        }

        /// <summary>
        /// Delete loan endpoint (the loan must belong to the caller, otherwise 404 for everyone, admins included):
        /// - If loan is associated to a PasanacoPayment:
        ///     - If payment belongs to current round => allow deletion by undoing the payment (delegates to PasanacoService.UndoPaymentAsync)
        ///     - If payment belongs to previous round => forbid deletion (400)
        /// - If loan is NOT associated to a Pasanaco => require Admin role to delete (forbid otherwise)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLoan(Guid id)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var loan = await _loanService.GetLoanAsync(id, userId.Value);
            if (loan == null) return NotFound();

            // buscar si existe payment vinculado que use este loan id
            var payment = await pasanacoService.GetPaymentByLoanIdAsync(id, userId.Value);
            if (payment != null)
            {
                // obtener pasanaco y calcular mes/año actual
                var pasanaco = await pasanacoService.GetByIdAsync(payment.PasanacoId, userId.Value);
                if (pasanaco == null) return BadRequest("Pasanaco asociado no encontrado");

                var current = pasanacoService.GetCurrentMonthYearForPasanaco(pasanaco);
                if (payment.Month != current.month || payment.Year != current.year)
                {
                    return BadRequest("No se puede borrar: el préstamo está vinculado a un pasanaco de ronda anterior.");
                }

                // Es la ronda actual => delegar en PasanacoService para deshacer el pago
                // PasanacoService.UndoPaymentAsync debe encargarse de eliminar loan/transacción y limpiar el payment
                await pasanacoService.UndoPaymentAsync(payment.Id, userId.Value);
                return NoContent();
            }

            // no está asociado a pasanaco -> permitir borrado solo para Admins
            if (!User.IsInRole("Admin"))
            {
                return Forbid();
            }

            await _loanService.DeleteLoanAsync(id, userId.Value);
            return NoContent();
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