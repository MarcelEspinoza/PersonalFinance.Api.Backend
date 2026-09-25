using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/chat")]
    public sealed class ChatController : ControllerBase
    {
        private readonly IGlobalChatService _chat;
        private readonly IExpenseService _expenses;
        private readonly IIncomeService _incomes;

        public ChatController(IGlobalChatService chat, IExpenseService expenses, IIncomeService incomes)
        {
            _chat = chat;
            _expenses = expenses;
            _incomes = incomes;
        }

        [HttpPost]
        public async Task<ActionResult<ChatResponseDto>> Ask([FromBody] ChatRequestDto request, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Message)) return BadRequest("El mensaje no puede estar vacío.");

            var result = await _chat.AskAsync(userId.Value, request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Aplica una acción "crear gasto" que el asistente propuso y el
        /// usuario confirmó explícitamente desde el chat.
        /// </summary>
        [HttpPost("actions/expense")]
        public async Task<IActionResult> ConfirmExpense([FromBody] ConfirmActionDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (!DateTime.TryParse(dto.Date, out var date)) return BadRequest("Fecha inválida.");

            var expense = await _expenses.CreateAsync(userId.Value, new CreateExpenseDto
            {
                Amount = dto.Amount,
                Description = dto.Description,
                Date = date,
                Type = dto.ExpenseType == "Fixed" ? "Fixed" : "Temporary",
                CategoryId = dto.CategoryId
            }, ct);

            return Ok(new { expense.Id });
        }

        /// <summary>
        /// Aplica una acción "crear ingreso" que el asistente propuso y el
        /// usuario confirmó explícitamente desde el chat.
        /// </summary>
        [HttpPost("actions/income")]
        public async Task<IActionResult> ConfirmIncome([FromBody] ConfirmActionDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (!DateTime.TryParse(dto.Date, out var date)) return BadRequest("Fecha inválida.");

            var income = await _incomes.CreateAsync(userId.Value, new CreateIncomeDto
            {
                Amount = dto.Amount,
                Description = dto.Description,
                Date = date,
                Type = dto.ExpenseType == "Fixed" ? "Fixed" : "Temporary",
                CategoryId = dto.CategoryId
            }, ct);

            return Ok(new { income.Id });
        }
    }
}
