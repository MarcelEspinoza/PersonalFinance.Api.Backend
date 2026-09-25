using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

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
        private readonly ILogger<GlobalChatService> _logger;

        public GlobalChatService(HttpClient http, IConfiguration configuration, AppDbContext db, ILogger<GlobalChatService> logger)
        {
            _http = http;
            _configuration = configuration;
            _db = db;
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

            var snapshot = await BuildSnapshotAsync(userId, ct);
            var model = _configuration["Anthropic:Model"] ?? "claude-3-5-haiku-latest";

            var system = $$"""
                Eres el asistente financiero personal dentro de la app. El usuario te habla
                en español y puede pedirte: explicaciones de sus datos, consejos de ahorro,
                búsquedas dentro de la información que ves abajo, o crear un gasto/ingreso.

                DATOS DEL USUARIO (resumen, no exhaustivo):
                {{JsonSerializer.Serialize(snapshot)}}

                CATEGORÍAS VÁLIDAS (usa sólo estos "id" si propones un gasto/ingreso):
                {{JsonSerializer.Serialize(categories)}}

                Si el resumen no contiene el detalle exacto que te piden, dilo con honestidad
                en vez de inventar cifras.

                Si el usuario te pide explícitamente crear/registrar un gasto o un ingreso,
                añade una entrada en "proposedActions" (nunca lo apliques tú mismo, sólo
                lo propones). Si falta información imprescindible (importe o concepto),
                pregúntasela en vez de adivinarla.

                Responde EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma exacta:
                {"reply":"texto para el usuario","proposedActions":[{"type":"create_expense|create_income","summary":"resumen corto para el botón de confirmar","amount":0,"description":"","date":"YYYY-MM-DD","categoryId":0,"categoryName":"","expenseType":"Fixed|Temporary"}]}

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

                var validCategoryIds = categories.Select(c => c.Id).ToHashSet();
                var actions = (parsed.ProposedActions ?? new())
                    .Where(a => validCategoryIds.Contains(a.CategoryId) && a.Amount > 0)
                    .Select(a => new ProposedActionDto
                    {
                        Type = a.Type ?? "create_expense",
                        Summary = a.Summary ?? string.Empty,
                        Amount = a.Amount,
                        Description = a.Description ?? string.Empty,
                        Date = a.Date ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        CategoryId = a.CategoryId,
                        CategoryName = a.CategoryName ?? string.Empty,
                        ExpenseType = a.ExpenseType == "Fixed" ? "Fixed" : "Temporary"
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
        private async Task<object> BuildSnapshotAsync(Guid userId, CancellationToken ct)
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
                .Select(b => new { b.CategoryId, b.MonthlyLimit })
                .ToListAsync(ct);
            var budgetsSummary = budgets.Select(b => new { category = categoryNames.GetValueOrDefault(b.CategoryId, "Otros"), monthlyLimit = b.MonthlyLimit });

            var commitments = await _db.FinancialCommitments
                .Where(c => c.UserId == userId && c.IsActive)
                .Select(c => new { c.Name, c.Type, c.ExpectedAmount, c.Tolerance })
                .ToListAsync(ct);

            var loans = await _db.Loans
                .Where(l => l.UserId == userId && l.Status != "paid")
                .Select(l => new { l.Name, l.Type, l.OutstandingAmount, l.NextPaymentAmount, l.NextPaymentDate })
                .ToListAsync(ct);

            var savings = await _db.SavingAccounts
                .Where(s => s.UserId == userId)
                .Select(s => s.Balance)
                .ToListAsync(ct);

            var pasanacos = await _db.Pasanacos
                .Select(p => new { p.Name, p.MonthlyAmount, p.TotalParticipants, p.CurrentRound })
                .ToListAsync(ct);

            var accounts = await _db.Accounts
                .Where(a => a.UserId == userId && a.IsActive)
                .Select(a => new { a.Name, a.Type, a.Currency, a.Entity })
                .ToListAsync(ct);

            return new
            {
                today = now.ToString("yyyy-MM-dd"),
                monthlyBreakdown,
                budgets = budgetsSummary,
                commitments,
                loans,
                savingsBalance = savings.Sum(),
                pasanacos,
                accounts
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
        }
    }
}
