using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Features.Chat
{
    public interface IGlobalChatService
    {
        Task<ChatResponseDto> AskAsync(Guid userId, ChatRequestDto request, CancellationToken ct);
    }

    /// <summary>
    /// Asistente transversal: puede consultar y explicar cualquier módulo
    /// (gastos, ingresos, presupuestos, compromisos, préstamos, ahorros,
    /// pasanaco, cuentas). No escribe nada directamente: cuando el usuario le
    /// pide crear un gasto/ingreso, propone una acción concreta que el
    /// controlador valida y que el frontend sólo aplica si el usuario confirma.
    /// </summary>
    public sealed class GlobalChatService : IGlobalChatService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _db;
        private readonly IDashboardService _dashboard;
        private readonly ILogger<GlobalChatService> _logger;

        public GlobalChatService(
            HttpClient http,
            IConfiguration configuration,
            AppDbContext db,
            IDashboardService dashboard,
            ILogger<GlobalChatService> logger)
        {
            _http = http;
            _configuration = configuration;
            _db = db;
            _dashboard = dashboard;
            _logger = logger;
        }

        public async Task<ChatResponseDto> AskAsync(Guid userId, ChatRequestDto request, CancellationToken ct)
        {
            var apiKey = _configuration["Anthropic:ApiKey"]
                ?? _configuration["ANTHROPIC_API_KEY"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ChatResponseDto { Reply = "No puedo hablar todavía: falta configurar la clave de Anthropic en el servidor." };
            }

            var categories = await _db.Categories
                .Where(c => c.UserId == userId && c.IsActive)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);
            var ledgerConcepts = await _db.Concepts
                .Where(c => c.UserId == userId && c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.Kind })
                .ToListAsync(ct);

            var snapshot = await BuildSnapshotAsync(userId, ledgerConcepts, ct);
            var model = _configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";

            var system = $$"""
                Eres el asistente financiero personal dentro de la app. El usuario te habla
                en español y puede pedirte: explicaciones de sus datos, consejos de ahorro,
                búsquedas dentro de la información que ves abajo, o crear un gasto/ingreso.

                DATOS DEL USUARIO (resumen, no exhaustivo):
                {{JsonSerializer.Serialize(snapshot)}}

                CATEGORÍAS VÁLIDAS (usa sólo estos "id" si propones un gasto/ingreso):
                {{JsonSerializer.Serialize(categories)}}

                CONCEPTOS CONTABLES VÁLIDOS (usa estos IDs para classify_import_group):
                {{JsonSerializer.Serialize(ledgerConcepts)}}

                Si el resumen no contiene el detalle exacto que te piden, dilo con honestidad
                en vez de inventar cifras.

                "currentMonthOutlook" es la previsión del mes calculada por la app: para cada
                cuenta trae la cronología de cobros y pagos pendientes con el saldo tras cada
                uno ("balanceAfter"). Úsala para explicar si le faltará dinero, en qué fecha y
                qué pagos o cobros conviene mover. No recalcules esas cifras.

                Puedes preparar estas acciones, pero nunca ejecutarlas directamente:
                - create_expense / create_income: amount, description, date, categoryId, expenseType.
                - update_expense / update_income: targetId y sólo los campos que cambian.
                - update_ledger_entry: targetId, y los campos que cambian entre amount,
                  description, date, conceptId y accountId.
                - create_category: name y description.
                - create_budget: amount como límite mensual, categoryId, year y month.
                - record_loan_payment: targetId del préstamo, amount, date y notes.
                - complete_pasanaco / reopen_pasanaco: targetId del pasanaco.
                - classify_import_group: targetId del lote, conceptId y normalizedDescription.
                - apply_import: targetId del lote, únicamente cuando ya no queden grupos pendientes.

                Si falta un dato imprescindible, pregúntalo. Usa exclusivamente IDs presentes
                en los datos. Nunca propongas borrar datos ni aplicar un lote completo.

                Responde EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma exacta:
                {"reply":"texto para el usuario","proposedActions":[{"type":"tipo_de_accion","summary":"resumen corto para confirmar","amount":0,"description":"","date":"YYYY-MM-DD","categoryId":0,"categoryName":"","expenseType":"Fixed|Temporary","targetId":null,"name":null,"notes":null,"year":null,"month":null,"conceptId":null,"accountId":null,"normalizedDescription":null}]}

                Deja "proposedActions" como lista vacía si no corresponde ninguna acción.
                """;

            var messages = request.History
                .Select(h => new { role = h.Role, content = h.Content })
                .Append(new { role = "user", content = request.Message })
                .ToList();

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = 2048,
                    temperature = 0,
                    system,
                    messages
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic devolvió HTTP {StatusCode} en el chat global.", (int)response.StatusCode);
                return new ChatResponseDto { Reply = "El servicio de IA no ha respondido; inténtalo de nuevo en un momento." };
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("content", out var content) ||
                content.GetArrayLength() == 0 ||
                !content[0].TryGetProperty("text", out var text))
                return new ChatResponseDto { Reply = "No he podido leer la respuesta del modelo." };

            var json = text.GetString()?.Trim() ?? string.Empty;
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = json.IndexOf('\n');
                json = firstNewLine >= 0 ? json[(firstNewLine + 1)..] : json;
                json = json.TrimEnd('`', '\r', '\n').Trim();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<RawChatResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is null) return new ChatResponseDto { Reply = json };

                var actions = (parsed.ProposedActions ?? new())
                    .Where(IsStructurallyValid)
                    .Select(a => new ProposedActionDto
                    {
                        Type = a.Type ?? "create_expense",
                        Summary = a.Summary ?? string.Empty,
                        Amount = a.Amount,
                        Description = a.Description ?? string.Empty,
                        Date = a.Date ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        CategoryId = a.CategoryId,
                        CategoryName = a.CategoryName ?? string.Empty,
                        ExpenseType = a.ExpenseType == "Fixed" ? "Fixed" : "Temporary",
                        TargetId = a.TargetId,
                        Name = a.Name,
                        Notes = a.Notes,
                        Year = a.Year,
                        Month = a.Month,
                        ConceptId = a.ConceptId,
                        AccountId = a.AccountId,
                        NormalizedDescription = a.NormalizedDescription
                    })
                    .ToList();

                return new ChatResponseDto { Reply = parsed.Reply ?? string.Empty, ProposedActions = actions };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "La respuesta del chat global no tenía el JSON esperado.");
                return new ChatResponseDto { Reply = "He tenido un problema entendiendo mi propia respuesta; prueba a reformular." };
            }
        }

        /// <summary>
        /// Resumen agregado (no filas completas) de cada módulo, pensado para
        /// caber en el prompt sin exponer todo el histórico. Cubre los últimos
        /// 3 meses de gastos/ingresos por categoría, presupuestos y compromisos
        /// activos, préstamos vivos, ahorro acumulado, pasanaco y cuentas.
        /// </summary>
        private async Task<object> BuildSnapshotAsync(
            Guid userId,
            object ledgerConcepts,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var since = new DateTime(now.Year, now.Month, 1).AddMonths(-2);

            var expenses = await _db.Expenses
                .Where(e => e.UserId == userId && e.Date >= since && !e.IsTransfer)
                .Select(e => new { e.Date, e.Amount, e.CategoryId })
                .ToListAsync(ct);

            var incomes = await _db.Incomes
                .Where(i => i.UserId == userId && i.Date >= since && !i.IsTransfer)
                .Select(i => new { i.Date, i.Amount, i.CategoryId })
                .ToListAsync(ct);

            var categoryNames = await _db.Categories
                .Where(c => c.UserId == userId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

            var monthlyBreakdown = expenses
                .GroupBy(e => new { e.Date.Year, e.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    month = $"{g.Key.Year}-{g.Key.Month:00}",
                    totalExpense = g.Sum(x => x.Amount),
                    totalIncome = incomes.Where(i => i.Date.Year == g.Key.Year && i.Date.Month == g.Key.Month).Sum(i => i.Amount),
                    byCategory = g.GroupBy(x => x.CategoryId)
                        .Select(cg => new { category = categoryNames.GetValueOrDefault(cg.Key, "Otros"), total = cg.Sum(x => x.Amount) })
                        .OrderByDescending(x => x.total)
                        .Take(8)
                        .ToList()
                })
                .ToList();

            var budgets = await _db.Budgets
                .Where(b => b.UserId == userId && b.IsActive)
                .Select(b => new { b.Id, b.CategoryId, b.MonthlyLimit, b.StartMonth, b.EndMonth })
                .ToListAsync(ct);
            var budgetsSummary = budgets.Select(b => new
            {
                b.Id,
                categoryId = b.CategoryId,
                category = categoryNames.GetValueOrDefault(b.CategoryId, "Otros"),
                monthlyLimit = b.MonthlyLimit,
                b.StartMonth,
                b.EndMonth
            });

            var commitments = await _db.FinancialCommitments
                .Where(c => c.UserId == userId && c.IsActive)
                .Select(c => new { c.Name, c.Type, c.ExpectedAmount, c.Tolerance })
                .ToListAsync(ct);

            var loans = await _db.Loans
                .Where(l => l.UserId == userId && l.Status != "paid")
                .Select(l => new { l.Id, l.Name, l.Type, l.OutstandingAmount, l.NextPaymentAmount, l.NextPaymentDate })
                .ToListAsync(ct);

            var savings = await _db.SavingAccounts
                .Where(s => s.UserId == userId)
                .Select(s => s.Balance)
                .ToListAsync(ct);

            var pasanacos = await _db.Pasanacos
                .Select(p => new { p.Id, p.Name, p.MonthlyAmount, p.TotalParticipants, p.CurrentRound, p.IsCompleted, p.CompletedAt })
                .ToListAsync(ct);

            var accounts = await _db.Accounts
                .Where(a => a.UserId == userId && a.IsActive)
                .Select(a => new { a.Id, a.Name, a.Type, a.Currency, a.Entity })
                .ToListAsync(ct);

            var ledgerEntries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId && entry.DueDate >= DateOnly.FromDateTime(since))
                .OrderByDescending(entry => entry.DueDate)
                .Take(150)
                .Select(entry => new
                {
                    entry.Id,
                    entry.DueDate,
                    entry.Direction,
                    entry.Status,
                    amount = entry.ActualAmount ?? entry.ForecastAmount,
                    entry.Description,
                    entry.ConceptId,
                    concept = entry.Concept != null ? entry.Concept.Name : null,
                    entry.AccountId,
                    account = entry.Account != null ? entry.Account.Name : null
                })
                .ToListAsync(ct);

            var pendingImportEntities = await _db.ImportBatches
                .AsNoTracking()
                .Include(batch => batch.Rows)
                .Where(batch => batch.UserId == userId && batch.Status != PersonalFinance.Domain.Ledger.Enums.ImportBatchStatus.Applied)
                .OrderByDescending(batch => batch.CreatedAt)
                .Take(5)
                .ToListAsync(ct);
            var pendingImports = pendingImportEntities.Select(batch => new
            {
                batch.Id,
                batch.FileName,
                batch.Status,
                pendingGroups = batch.Rows
                    .Where(row =>
                        row.Status == PersonalFinance.Domain.Ledger.Enums.ImportRowStatus.Pending &&
                        row.ConfirmedConceptId == null &&
                        row.SuggestedConceptId == null)
                    .GroupBy(row => row.NormalizedDescription)
                    .Select(group => new
                    {
                        normalizedDescription = group.Key,
                        count = group.Count(),
                        total = group.Sum(row => row.Amount)
                    })
                    .OrderByDescending(group => group.count)
                    .Take(30)
                    .ToList()
            })
                .ToList();

            var projection = await _dashboard.GetFutureProjectionAsync(null, null, ct);
            var monthOutlook = MonthAdvisorService.BuildMonthContext(projection.Outlook);

            return new
            {
                today = now.ToString("yyyy-MM-dd"),
                currentMonthOutlook = monthOutlook,
                monthlyBreakdown,
                budgets = budgetsSummary,
                commitments,
                loans,
                savingsBalance = savings.Sum(),
                pasanacos,
                accounts,
                ledgerConcepts,
                recentLedgerEntries = ledgerEntries,
                pendingImports
            };
        }

        private static bool IsStructurallyValid(RawProposedAction action)
        {
            return action.Type switch
            {
                "create_expense" or "create_income" =>
                    action.Amount > 0 && action.CategoryId > 0 && !string.IsNullOrWhiteSpace(action.Description),
                "update_expense" or "update_income" =>
                    int.TryParse(action.TargetId, out _),
                "update_ledger_entry" =>
                    Guid.TryParse(action.TargetId, out _),
                "create_category" =>
                    !string.IsNullOrWhiteSpace(action.Name),
                "create_budget" =>
                    action.Amount > 0 && action.CategoryId > 0 && action.Year.HasValue && action.Month is >= 1 and <= 12,
                "record_loan_payment" =>
                    action.Amount > 0 && Guid.TryParse(action.TargetId, out _),
                "complete_pasanaco" or "reopen_pasanaco" =>
                    !string.IsNullOrWhiteSpace(action.TargetId),
                "classify_import_group" =>
                    Guid.TryParse(action.TargetId, out _) &&
                    action.ConceptId.HasValue &&
                    !string.IsNullOrWhiteSpace(action.NormalizedDescription),
                "apply_import" =>
                    Guid.TryParse(action.TargetId, out _),
                _ => false
            };
        }

        private sealed class RawChatResponse
        {
            public string? Reply { get; set; }
            public List<RawProposedAction>? ProposedActions { get; set; }
        }

        private sealed class RawProposedAction
        {
            public string? Type { get; set; }
            public string? Summary { get; set; }
            public decimal Amount { get; set; }
            public string? Description { get; set; }
            public string? Date { get; set; }
            public int CategoryId { get; set; }
            public string? CategoryName { get; set; }
            public string? ExpenseType { get; set; }
            public string? TargetId { get; set; }
            public string? Name { get; set; }
            public string? Notes { get; set; }
            public int? Year { get; set; }
            public int? Month { get; set; }
            public Guid? ConceptId { get; set; }
            public Guid? AccountId { get; set; }
            public string? NormalizedDescription { get; set; }
        }
    }
}
