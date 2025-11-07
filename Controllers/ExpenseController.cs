namespace PersonalFinance.Api.Controllers
{

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using PersonalFinance.Api.Models.Dtos.Expense;
    using PersonalFinance.Api.Services;
    using PersonalFinance.Api.Services.Contracts;
    using System;
    using System.Linq;
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;

    [ApiController]
    [Route("api/[controller]")]
    public class ExpenseController : ControllerBase
    {
        private readonly IExpenseService _expenseService;
        private readonly IPasanacoService pasanacoService;

        public ExpenseController(IExpenseService expenseService, IPasanacoService pasanacoService)
        {
            _expenseService = expenseService;
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

        // GET: api/expense
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var expense = await _expenseService.GetAllAsync(userId.Value, cancellationToken);            

            return Ok(expense);
        }

        // GET: api/expense/5
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var expense = await _expenseService.GetByIdAsync(id, userId.Value, cancellationToken);
            if (expense == null) return NotFound();

            return Ok(expense);
        }

        // POST: api/expense
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateExpenseDto dto, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var created = await _expenseService.CreateAsync(userId.Value, dto, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new
                {
                    id = created.Id,
                    amount = created.Amount,
                    description = created.Description,
                    date = created.Date,
                    type = created.Type,
                    start_date = created.Start_Date,
                    end_Date = created.End_Date,
                    notes = created.Notes
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

        // PUT: api/expense/5
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateExpenseDto dto, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var updated = await _expenseService.UpdateAsync(id, userId.Value, dto, cancellationToken);
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
        public async Task<IActionResult> DeleteExpense(int id)
        {
            var userId = GetCurrentUserId();
            // Si Expense tiene relation directa a PasanacoPayment via TransactionId, reutilizar la misma comprobación:
            var payment = await pasanacoService.GetPaymentByTransactionIdAsync(id);
            if (payment != null)
            {
                var pasanaco = await pasanacoService.GetByIdAsync(payment.PasanacoId);
                var current = pasanacoService.GetCurrentMonthYearForPasanaco(pasanaco);
                if (payment.Month != current.month || payment.Year != current.year)
                {
                    return BadRequest("No se puede borrar: el gasto está vinculado a un pasanaco de ronda anterior.");
                }
                await pasanacoService.UndoPaymentAsync(payment.Id, userId!.Value);
                return NoContent();
            }

            await _expenseService.DeleteAsync(id, userId!.Value);
            return NoContent();
        }
    }
    
}
