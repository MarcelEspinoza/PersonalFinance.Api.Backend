using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Features.Ledger.Commands.ConfirmLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.SetMonthlyBudget;
using PersonalFinance.Api.Features.Ledger.Commands.UpdateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Reminders;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Services.Contracts;
using PersonalFinance.Domain.Ledger.Enums;

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
        private readonly IPaymentService _payments;
        private readonly IPasanacoService _pasanacos;
        private readonly IImportBatchApplicationService _importApplication;
        private readonly IReminderService _reminders;
        private readonly AppDbContext _db;

        public ChatController(
            IGlobalChatService chat,
            IExpenseService expenses,
            IIncomeService incomes,
            IPaymentService payments,
            IPasanacoService pasanacos,
            IImportBatchApplicationService importApplication,
            IReminderService reminders,
            AppDbContext db)
        {
            _chat = chat;
            _expenses = expenses;
            _incomes = incomes;
            _payments = payments;
            _pasanacos = pasanacos;
            _importApplication = importApplication;
            _reminders = reminders;
            _db = db;
        }

        // Las órdenes de «Mes» (las mismas que usa api/ledger-entries).
        private IMediator Mediator => HttpContext.RequestServices.GetRequiredService<IMediator>();

        [HttpPost]
        public async Task<ActionResult<ChatResponseDto>> Ask([FromBody] ChatRequestDto request, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Message)) return BadRequest("El mensaje no puede estar vacío.");

            // Admin mode (api_call proposals) depends only on the role in the token.
            var result = await _chat.AskAsync(userId.Value, request, User.IsInRole("Admin"), ct);
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

        [HttpPost("actions/confirm")]
        public async Task<IActionResult> ConfirmAction(
            [FromBody] ProposedActionDto action,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            switch (action.Type)
            {
                case "create_expense":
                    return await ConfirmExpense(action, userId.Value, ct);
                case "create_income":
                    return await ConfirmIncome(action, userId.Value, ct);
                case "update_expense":
                    return await UpdateExpense(action, userId.Value, ct);
                case "update_income":
                    return await UpdateIncome(action, userId.Value, ct);
                case "update_ledger_entry":
                    return await UpdateLedgerEntry(action, userId.Value, ct);
                case "create_category":
                    return await CreateCategory(action, userId.Value, ct);
                case "create_budget":
                    return await CreateOrUpdateBudget(action, userId.Value, ct);
                case "record_loan_payment":
                    return await RecordLoanPayment(action, userId.Value);
                case "complete_pasanaco":
                    if (string.IsNullOrWhiteSpace(action.TargetId)) return BadRequest("Falta el pasanaco.");
                    await _pasanacos.CompleteAsync(action.TargetId, userId.Value);
                    return Ok(new { action.Type, action.TargetId });
                case "reopen_pasanaco":
                    if (string.IsNullOrWhiteSpace(action.TargetId)) return BadRequest("Falta el pasanaco.");
                    await _pasanacos.ReopenAsync(action.TargetId, userId.Value);
                    return Ok(new { action.Type, action.TargetId });
                case "classify_import_group":
                    return await ClassifyImportGroup(action, userId.Value, ct);
                case "apply_import":
                    return await ApplyImport(action, userId.Value, ct);
                case "create_reminder":
                    return await CreateReminder(action, userId.Value, ct);
                case "api_call":
                    // The frontend calls the endpoint itself with the user's token.
                    return BadRequest("Esta acción la ejecuta la app directamente con tus permisos; no se confirma por aquí.");
                default:
                    return BadRequest("La acción propuesta no está soportada.");
            }
        }

        private Task<IActionResult> ConfirmExpense(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct) =>
            CreateLedgerMovement(action, userId, ConceptKind.Expense, ct);

        private Task<IActionResult> ConfirmIncome(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct) =>
            CreateLedgerMovement(action, userId, ConceptKind.Income, ct);

        /// <summary>
        /// Anota el gasto o ingreso en «Mes» igual que la pantalla: crea el
        /// asiento y, si su fecha ya ha llegado (hoy en Madrid), lo confirma
        /// como pagado con ese importe y esa fecha.
        /// </summary>
        private async Task<IActionResult> CreateLedgerMovement(
            ProposedActionDto action,
            Guid userId,
            ConceptKind kind,
            CancellationToken ct)
        {
            var label = kind == ConceptKind.Expense ? "gasto" : "ingreso";
            if (action.Amount <= 0)
                return BadRequest(new { message = $"Falta el importe del {label}." });
            if (!TryGetActionDate(action, out var date))
                return BadRequest(new { message = $"Falta la fecha del {label}." });
            if (action.ConceptId is null)
                return BadRequest(new { message = $"Elige un concepto de {label} para anotarlo en «Mes»." });

            var conceptValid = await _db.Concepts.AnyAsync(
                item =>
                    item.Id == action.ConceptId.Value &&
                    item.UserId == userId &&
                    item.IsActive &&
                    item.Kind == kind,
                ct);
            if (!conceptValid)
                return BadRequest(new { message = $"El concepto elegido no es un concepto de {label} válido." });

            if (action.AccountId is not null)
            {
                var accountValid = await _db.Accounts.AnyAsync(
                    item =>
                        item.Id == action.AccountId.Value &&
                        item.UserId == userId &&
                        item.IsActive,
                    ct);
                if (!accountValid)
                    return BadRequest(new { message = "La cuenta elegida no existe o está desactivada." });
            }

            var day = DateOnly.FromDateTime(date);
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var created = await Mediator.Send(new CreateLedgerEntryCommand(userId, new CreateLedgerEntryDto
            {
                ConceptId = action.ConceptId.Value,
                AccountId = action.AccountId,
                Direction = kind == ConceptKind.Expense ? EntryDirection.Out : EntryDirection.In,
                DueDate = day,
                ForecastAmount = action.Amount,
                Description = Clip(NullIfBlank(action.Description), 300),
                Notes = Clip(NullIfBlank(action.Notes), 500)
            }), ct);
            if (day <= ReminderRules.LocalToday(DateTime.UtcNow))
            {
                await Mediator.Send(new ConfirmLedgerEntryCommand(userId, created.Id, new ConfirmLedgerEntryDto
                {
                    ActualAmount = action.Amount,
                    ValueDate = day
                }), ct);
            }
            await tx.CommitAsync(ct);

            return Ok(new { action.Type, created.Id });
        }

        private async Task<IActionResult> UpdateExpense(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            if (!int.TryParse(action.TargetId, out var id)) return BadRequest("Falta el gasto.");
            var updated = await _expenses.UpdateAsync(id, userId, new UpdateExpenseDto
            {
                Amount = action.Amount > 0 ? action.Amount : null,
                Description = NullIfBlank(action.Description),
                Date = TryGetActionDate(action, out var date) ? date : null,
                CategoryId = action.CategoryId > 0 ? action.CategoryId : null,
                Type = NullIfBlank(action.ExpenseType),
                Notes = action.Notes
            }, ct);
            return updated ? Ok(new { action.Type, id }) : NotFound();
        }

        private async Task<IActionResult> UpdateIncome(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            if (!int.TryParse(action.TargetId, out var id)) return BadRequest("Falta el ingreso.");
            var updated = await _incomes.UpdateAsync(id, userId, new UpdateIncomeDto
            {
                Amount = action.Amount > 0 ? action.Amount : null,
                Description = NullIfBlank(action.Description),
                Date = TryGetActionDate(action, out var date) ? date : null,
                CategoryId = action.CategoryId > 0 ? action.CategoryId : null,
                Type = NullIfBlank(action.ExpenseType),
                Notes = action.Notes
            }, ct);
            return updated ? Ok(new { action.Type, id }) : NotFound();
        }

        private async Task<IActionResult> CreateCategory(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            var name = action.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest("Falta el nombre de la categoría.");

            var exists = await _db.Categories.AnyAsync(
                category => category.UserId == userId && category.Name == name,
                ct);
            if (exists) return Conflict("Ya existe una categoría con ese nombre.");

            var category = new Category
            {
                UserId = userId,
                Name = name,
                Description = NullIfBlank(action.Description),
                IsActive = true
            };
            _db.Categories.Add(category);
            await _db.SaveChangesAsync(ct);
            return Ok(new { action.Type, category.Id });
        }

        private async Task<IActionResult> UpdateLedgerEntry(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            if (!Guid.TryParse(action.TargetId, out var entryId))
                return BadRequest(new { message = "Falta el movimiento." });

            var hasDate = TryGetActionDate(action, out var date);
            var entryExists = await _db.LedgerEntries.AnyAsync(
                item => item.Id == entryId && item.UserId == userId,
                ct);
            if (!entryExists)
            {
                // Una reserva de gasto variable no es un asiento: llega con el id
                // del concepto y el importe es el nuevo límite de ese mes.
                var isConcept = await _db.Concepts.AnyAsync(
                    item => item.Id == entryId && item.UserId == userId,
                    ct);
                if (!isConcept) return NotFound(new { message = "El movimiento ya no existe." });
                if (!hasDate) return BadRequest(new { message = "Falta el mes de la reserva." });

                var month = DateOnly.FromDateTime(date);
                var budgetId = await Mediator.Send(
                    new SetMonthlyBudgetCommand(userId, entryId, month.Year, month.Month, action.Amount),
                    ct);
                return Ok(new { action.Type, Id = budgetId });
            }

            if (action.ConceptId is not null)
            {
                var conceptExists = await _db.Concepts.AnyAsync(
                    item =>
                        item.Id == action.ConceptId.Value &&
                        item.UserId == userId &&
                        item.IsActive,
                    ct);
                if (!conceptExists) return BadRequest(new { message = "El concepto no existe." });
            }

            if (action.AccountId is not null)
            {
                var accountExists = await _db.Accounts.AnyAsync(
                    item =>
                        item.Id == action.AccountId.Value &&
                        item.UserId == userId &&
                        item.IsActive,
                    ct);
                if (!accountExists) return BadRequest(new { message = "La cuenta no existe." });
            }

            // Misma orden que «Mes»: cambiar la fecha a otro mes lleva el asiento
            // a ese periodo, y el importe de una previsión sólo cambia lo previsto.
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await Mediator.Send(new UpdateLedgerEntryCommand(userId, entryId, new UpdateLedgerEntryDto
            {
                ConceptId = action.ConceptId,
                AccountId = action.AccountId,
                DueDate = hasDate ? DateOnly.FromDateTime(date) : null,
                ForecastAmount = action.Amount > 0 ? action.Amount : null,
                Description = Clip(NullIfBlank(action.Description), 300)
            }), ct);

            var entry = await _db.LedgerEntries.FirstAsync(item => item.Id == entryId, ct);
            if (entry.Status == EntryStatus.Paid && (action.Amount > 0 || hasDate))
            {
                if (action.Amount > 0) entry.ActualAmount = action.Amount;
                if (hasDate) entry.ValueDate = DateOnly.FromDateTime(date);
                await _db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);

            return Ok(new { action.Type, entry.Id });
        }

        private async Task<IActionResult> CreateOrUpdateBudget(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            if (action.Amount <= 0 || action.CategoryId <= 0 ||
                action.Year is null || action.Month is not (>= 1 and <= 12))
                return BadRequest("Faltan datos para crear el presupuesto.");

            var categoryExists = await _db.Categories.AnyAsync(
                category =>
                    category.Id == action.CategoryId &&
                    (category.UserId == userId || category.IsSystem),
                ct);
            if (!categoryExists) return BadRequest("La categoría no existe.");

            var start = new DateTime(action.Year.Value, action.Month.Value, 1);
            var budget = await _db.Budgets.FirstOrDefaultAsync(
                item =>
                    item.UserId == userId &&
                    item.CategoryId == action.CategoryId &&
                    item.StartMonth <= start &&
                    (item.EndMonth == null || item.EndMonth >= start),
                ct);
            if (budget is null)
            {
                budget = new Budget
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    CategoryId = action.CategoryId,
                    MonthlyLimit = action.Amount,
                    StartMonth = start,
                    IsActive = true
                };
                _db.Budgets.Add(budget);
            }
            else
            {
                budget.MonthlyLimit = action.Amount;
                budget.IsActive = true;
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { action.Type, budget.Id });
        }

        private async Task<IActionResult> RecordLoanPayment(
            ProposedActionDto action,
            Guid userId)
        {
            if (!Guid.TryParse(action.TargetId, out var loanId) ||
                action.Amount <= 0 ||
                !TryGetActionDate(action, out var date))
                return BadRequest("Faltan datos para registrar el pago.");

            var loan = await _db.Loans
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == loanId && item.UserId == userId);
            if (loan is null) return NotFound();
            if (action.Amount > loan.OutstandingAmount)
                return BadRequest("El pago supera el saldo pendiente del préstamo.");

            var payment = await _payments.CreatePaymentAsync(loanId, new LoanPayment
            {
                Id = Guid.NewGuid(),
                Amount = action.Amount,
                PaymentDate = date,
                Notes = action.Notes
            });
            return Ok(new { action.Type, payment.Id });
        }

        private async Task<IActionResult> ClassifyImportGroup(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            if (!Guid.TryParse(action.TargetId, out var batchId) ||
                action.ConceptId is null ||
                string.IsNullOrWhiteSpace(action.NormalizedDescription))
                return BadRequest("Faltan datos para clasificar la importación.");

            var batch = await _db.ImportBatches.FirstOrDefaultAsync(
                batch =>
                    batch.Id == batchId &&
                    batch.UserId == userId &&
                    batch.Status != ImportBatchStatus.Applied,
                ct);
            var conceptExists = await _db.Concepts.AnyAsync(
                concept =>
                    concept.Id == action.ConceptId.Value &&
                    concept.UserId == userId &&
                    concept.IsActive,
                ct);
            if (batch is null || batch.AccountId is null || !conceptExists)
                return BadRequest("El lote o el concepto no son válidos.");

            var rows = await _db.ImportRows
                .Where(row =>
                    row.BatchId == batchId &&
                    row.UserId == userId &&
                    row.Status == ImportRowStatus.Pending &&
                    row.NormalizedDescription == action.NormalizedDescription)
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                row.ConfirmedConceptId = action.ConceptId;
                row.SuggestionSource = "assistant";
                row.SuggestionConfidence = 1m;
            }

            var mapping = await _db.ConceptMappings.FirstOrDefaultAsync(
                item =>
                    item.UserId == userId &&
                    item.AccountId == batch.AccountId &&
                    item.Pattern == action.NormalizedDescription,
                ct);
            if (mapping is null)
            {
                _db.ConceptMappings.Add(new PersonalFinance.Domain.Ledger.Entities.ConceptMapping
                {
                    UserId = userId,
                    AccountId = batch.AccountId,
                    Pattern = action.NormalizedDescription,
                    ConceptId = action.ConceptId.Value,
                    Priority = 120,
                    IsActive = true
                });
            }
            else
            {
                mapping.ConceptId = action.ConceptId.Value;
                mapping.Priority = Math.Max(mapping.Priority, 120);
                mapping.IsActive = true;
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { action.Type, updated = rows.Count });
        }

        private async Task<IActionResult> ApplyImport(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            if (!Guid.TryParse(action.TargetId, out var batchId))
                return BadRequest("Falta el lote de importación.");

            var result = await _importApplication.ApplyAsync(userId, batchId, ct);
            if (result.NotFound) return NotFound();
            if (!result.Success && result.Conflict) return Conflict(result.Error);
            if (!result.Success)
                return BadRequest(new { message = result.Error, unassignedRows = result.UnassignedRows });

            return Ok(new { action.Type, result.Applied, result.BatchId });
        }

        /// <summary>
        /// Re-validates the reminder with the same rules as api/reminders (date today
        /// or later) and creates it with source "ai". Errors come back as 400 { message }.
        /// </summary>
        private async Task<IActionResult> CreateReminder(
            ProposedActionDto action,
            Guid userId,
            CancellationToken ct)
        {
            var reminder = await _reminders.CreateFromAssistantAsync(userId, new SaveReminderDto
            {
                Title = action.Name,
                DueDate = action.Date,
                Amount = action.Amount,
                Notes = action.Notes,
                Repeat = action.Repeat,
                NotifyDaysBefore = action.NotifyDaysBefore
            }, ct);
            return Ok(new { action.Type, reminder.Id });
        }

        private static bool TryGetActionDate(ProposedActionDto action, out DateTime date) =>
            DateTime.TryParse(action.Date, out date);

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Clip(string? value, int maxLength) =>
            value is not null && value.Length > maxLength ? value[..maxLength] : value;
    }
}
