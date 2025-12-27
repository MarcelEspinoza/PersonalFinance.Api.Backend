using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private readonly IBudgetService _service;
    private readonly IBudgetMatchingService _matching;

    public BudgetsController(IBudgetService service, IBudgetMatchingService matching)
    {
        _service = service;
        _matching = matching;
    }

    [HttpGet]
    public async Task<IActionResult> GetForMonth(
        [FromQuery] int year,
        [FromQuery] int month)
        => Ok(await _service.GetForMonthAsync(year, month));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Budget dto)
        => Ok(await _service.CreateAsync(dto));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Budget dto)
    {
        await _service.UpdateAsync(id, dto);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Disable(Guid id)
    {
        await _service.DisableAsync(id);
        return NoContent();
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetMonthlyStatus(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        var data = await _matching.GetMonthlyStatusAsync(year, month, ct);
        return Ok(data);
    }
}
