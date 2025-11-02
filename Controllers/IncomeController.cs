namespace PersonalFinance.Api.Controllers
{

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using PersonalFinance.Api.Models.Dtos.Income;
    using PersonalFinance.Api.Services;
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

        public IncomeController(IIncomeService incomeService)
        {
            _incomeService = incomeService;
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
            var result = incomes.Select(i => new
            {
                id = i.Id,
                amount = i.Amount,
                description = i.Description,
                date = i.Date,
                type = i.Type
                // Note: CategoryId not included here to avoid breaking if Income model isn't updated yet.
            });

            return Ok(result);
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

            return Ok(new
            {
                id = income.Id,
                amount = income.Amount,
                description = income.Description,
                date = income.Date,
                type = income.Type
            });
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
                    type = created.Type
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

        // DELETE: api/income/5
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var deleted = await _incomeService.DeleteAsync(id, userId.Value, cancellationToken);
            if (!deleted) return NotFound();

            return NoContent();
        }
    }
    
}
