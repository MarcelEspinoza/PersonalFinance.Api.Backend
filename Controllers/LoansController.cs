using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/loans")]
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
        [Authorize]
        public async Task<IActionResult> GetLoans([FromQuery] Guid userId)
        {
            var loans = await _loanService.GetLoansAsync(userId);
            return Ok(loans);
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetLoan(Guid id)
        {
            var loan = await _loanService.GetLoanAsync(id);
            if (loan == null) return NotFound();
            return Ok(loan);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateLoan([FromBody] Loan loan)
        {
            var created = await _loanService.CreateLoanAsync(loan);
            return Ok(created);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateLoan(Guid id, [FromBody] Loan loan)
        {
            if (id != loan.Id) return BadRequest();
            await _loanService.UpdateLoanAsync(loan);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteLoan(Guid id)
        {
            var userId = GetCurrentUserId();
            // buscar si existe payment vinculado que use este loan id
            var payment = await pasanacoService.GetPaymentByLoanIdAsync(id); // implementa este método en IPasanacoService
            if (payment != null)
            {
                var pasanaco = await pasanacoService.GetByIdAsync(payment.PasanacoId);
                var current = pasanacoService.GetCurrentMonthYearForPasanaco(pasanaco);
                if (payment.Month != current.month || payment.Year != current.year)
                {
                    return BadRequest("No se puede borrar: el préstamo está vinculado a un pasanaco de ronda anterior.");
                }

                // Borrar loan + limpiar payment
                await _loanService.DeleteLoanAsync(id);

                return NoContent();
            }

            // no está asociado a pasanaco -> borrar normal
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
