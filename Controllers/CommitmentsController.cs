using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

[ApiController]
[Route("api/commitments")]
[Authorize]
public class CommitmentsController : ControllerBase
{
    private readonly ICommitmentService _service;
    private readonly ICommitmentMatchingService _matcher;

    public CommitmentsController(ICommitmentService service, ICommitmentMatchingService matcher)
    {
        _service = service;
        _matcher = matcher;
    }

    [HttpGet]
    public async Task<IActionResult> GetForMonth(
        [FromQuery] int year,
        [FromQuery] int month)
        => Ok(await _service.GetForMonthAsync(year, month));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FinancialCommitment dto)
        => Ok(await _service.CreateAsync(dto));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FinancialCommitment dto)
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
        var data = await _matcher.GetMonthlyStatusAsync(year, month, ct);
        return Ok(data);
    }
}
