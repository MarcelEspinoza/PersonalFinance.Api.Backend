namespace PersonalFinance.Api.Controllers
{

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http.HttpResults;
    using Microsoft.AspNetCore.Mvc;
    using PersonalFinance.Api.Models.Dtos.Income;
    using PersonalFinance.Api.Models.Dtos.Pasanaco;
    using PersonalFinance.Api.Models.Entities;
    using PersonalFinance.Api.Services;
    using PersonalFinance.Api.Services.Contracts;
    using System;
    using System.Linq;
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;

    [ApiController]
    [Route("api/[controller]")]
    public class IncomeController : ControllerBase
    {
        private readonly IIncomeService _incomeService;
        private readonly IPasanacoService pasanacoService;

        public IncomeController(IIncomeService incomeService, IPasanacoService pasanacoService)
        {
            _incomeService = incomeService;
            this.pasanacoService = pasanacoService;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return null;
            if (!Guid.TryParse(userIdClaim, out var userId)) return null;
            return userId;
        }

        // GET: api/income
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var incomes = await _incomeService.GetAllAsync(userId.Value, cancellationToken);
            return Ok(incomes); // 👈 devuelve el DTO completo
        }

        // GET: api/income/5
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var income = await _incomeService.GetByIdAsync(id, userId.Value, cancellationToken);
            if (income == null) return NotFound();

            return Ok(income);
        }

        // POST: api/income
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateIncomeDto dto, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var created = await _incomeService.CreateAsync(userId.Value, dto, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new
                {
                    id = created.Id,
                    amount = created.Amount,
                    description = created.Description,
                    date = created.Date,
                    type = created.Type,
                    start_date = created.Start_Date,
                    end_Date = created.End_Date,
                    notes = created.Notes,
                    categoryId = created.CategoryId,
                    loanId = created.LoanId,
                    bankId = created.BankId
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        // PUT: api/income/5
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateIncomeDto dto, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var updated = await _incomeService.UpdateAsync(id, userId.Value, dto, cancellationToken);
                if (!updated) return NotFound();
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteIncome(int id)
        {
            var userId = GetCurrentUserId();

            var payment = await pasanacoService.GetPaymentByTransactionIdAsync(id);
            if (payment != null)
            {
                // recuperar pasanaco para calcular ronda actual
                var pasanaco = await pasanacoService.GetByIdAsync(payment.PasanacoId);
                if (pasanaco == null) return BadRequest("Pasanaco no encontrado");

                var current = pasanacoService.GetCurrentMonthYearForPasanaco(pasanaco);
                if (payment.Month != current.month || payment.Year != current.year)
                {
                    return BadRequest("No se puede borrar: el ingreso está vinculado a un pasanaco de ronda anterior.");
                }

                await pasanacoService.UndoPaymentAsync(payment.Id, userId!.Value);

                return NoContent();
            }

            await _incomeService.DeleteAsync(id, userId!.Value);
            return NoContent();
        }

        
    }
    
}
