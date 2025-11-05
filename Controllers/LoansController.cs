using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/loans")]
    public class LoansController : ControllerBase
    {
        private readonly ILoanService _loanService;
        public LoansController(ILoanService loanService) => _loanService = loanService;

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
        public async Task<IActionResult> CreateLoan([FromBody] Loan loan)
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLoan(Guid id)
        {
            await _loanService.DeleteLoanAsync(id);
            return NoContent();
        }
    }

}
