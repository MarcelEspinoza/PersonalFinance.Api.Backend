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


        [HttpGet]
        public async Task<IActionResult> GetLoans([FromQuery] Guid userId)
        {
            var loans = await _loanService.GetLoansAsync(userId);
            return Ok(loans);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLoan(Guid id)
        {
            var loan = await _loanService.GetLoanAsync(id);
            if (loan == null) return NotFound();
            return Ok(loan);
        }

        [HttpPost]
        public async Task<IActionResult> CreateLoan([FromBody] LoanDto loan)
        {
            var created = await _loanService.CreateLoanAsync(loan);
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLoan(Guid id, [FromBody] Loan loan)
        {
            if (id != loan.Id) return BadRequest();
            await _loanService.UpdateLoanAsync(loan);
            return NoContent();
        }

        /// <summary>
        /// Delete loan endpoint:
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

            // buscar si existe payment vinculado que use este loan id
            var payment = await pasanacoService.GetPaymentByLoanIdAsync(id);
            if (payment != null)
            {
                // obtener pasanaco y calcular mes/año actual
                var pasanaco = await pasanacoService.GetByIdAsync(payment.PasanacoId);
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

            await _loanService.DeleteLoanAsync(id);
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