using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Chat
{
    public interface IMonthAdvisorService
    {
        Task<MonthAdviceDto> AdviseAsync(int? year, int? month, CancellationToken ct);
    }

    public sealed class MonthAdviceDto
    {
        public bool Available { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<MonthAdviceInsightDto> Insights { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class MonthAdviceInsightDto
    {
        /// <summary>"danger", "warning", "info" o "good".</summary>
        public string Severity { get; set; } = "info";
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pide a la IA que lea la previsiÃ³n del mes (y de los dos siguientes) y
    /// devuelva comentarios y consejos en lenguaje sencillo. Los cÃ¡lculos los
    /// hace la app; la IA solo interpreta la cronologÃ­a ya calculada para no
    /// inventar cifras.
    /// </summary>
    public sealed class MonthAdvisorService : IMonthAdvisorService
    {
        private const int MonthsAhead = 2;

        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly IDashboardService _dashboard;
        private readonly ILogger<MonthAdvisorService> _logger;

        public MonthAdvisorService(
            HttpClient http,
            IConfiguration configuration,
            IDashboardService dashboard,
            ILogger<MonthAdvisorService> logger)
        {
            _http = http;
            _configuration = configuration;
            _dashboard = dashboard;
            _logger = logger;
        }

        public async Task<MonthAdviceDto> AdviseAsync(int? year, int? month, CancellationToken ct)
        {
            var anchor = await _dashboard.GetFutureProjectionAsync(year, month, ct);
            var result = new MonthAdviceDto
            {
                Year = anchor.Outlook.Year,
                Month = anchor.Outlook.Month
            };

            var apiKey = _configuration["Anthropic:ApiKey"]
                ?? _configuration["ANTHROPIC_API_KEY"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                result.Summary = "Falta configurar la clave de Anthropic en el servidor.";
                return result;
            }

            var months = new List<object> { BuildMonthContext(anchor.Outlook) };
            var cursor = new DateOnly(anchor.Outlook.Year, anchor.Outlook.Month, 1);
            for (var i = 1; i <= MonthsAhead; i++)
            {
                var next = cursor.AddMonths(i);
                var projection = await _dashboard.GetFutureProjectionAsync(next.Year, next.Month, ct);
                months.Add(BuildMonthContext(projection.Outlook));
            }

            var context = JsonSerializer.Serialize(new
            {
                today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                monthAnalysed = $"{result.Year}-{result.Month:00}",
                months
            });

            var system = """
                Eres el asesor financiero personal del usuario dentro de su app. Lees la
                previsiÃ³n de su mes y de los dos siguientes y le explicas, en espaÃ±ol
                sencillo y cercano (tuteando), quÃ© va a pasar con su dinero y quÃ© deberÃ­a
                hacer. Es una persona analÃ­tica: da cifras concretas y fechas.

                Los datos ya vienen calculados por la app. Para cada cuenta tienes el saldo
                de partida y la cronologÃ­a de movimientos con el saldo acumulado tras cada
                uno ("balanceAfter"). NO recalcules ni inventes cifras: usa las que vienen.

                Explica sobre todo:
                - Si una cuenta termina el mes en positivo pero pasa por negativo, di que el
                  problema es de FECHAS (los pagos salen antes de que lleguen los cobros),
                  indica el dÃ­a y el importe del peor momento y quÃ© cobro lo arregla.
                - QuÃ© pagos concretos se podrÃ­an mover a despuÃ©s del cobro principal, o quÃ©
                  cobros convendrÃ­a pedir antes, y cuÃ¡nto mejora cada opciÃ³n.
                - Las transferencias entre cuentas que la app recomienda.
                - Cuentas sin ningÃºn movimiento previsto (probablemente sus gastos estÃ¡n
                  asignados a otra cuenta en ConfiguraciÃ³n).
                - La tendencia de los meses siguientes (si mejora o empeora y por quÃ©).
                - Gastos variables que se estÃ¡n desviando del lÃ­mite.

                Responde EXCLUSIVAMENTE JSON vÃ¡lido, sin markdown, con esta forma:
                {"summary":"2-3 frases con la conclusiÃ³n principal",
                 "insights":[{"severity":"danger|warning|info|good","title":"titular corto","detail":"explicaciÃ³n y consejo concreto"}]}
                Entre 3 y 7 insights, ordenados de mÃ¡s a menos urgente.
                """;

            var model = _configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = 2048,
                    temperature = 0,
                    system,
                    messages = new[] { new { role = "user", content = context } }
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic devolviÃ³ HTTP {StatusCode} al aconsejar el mes.", (int)response.StatusCode);
                result.Summary = "El servicio de IA no ha respondido; intÃ©ntalo de nuevo en un momento.";
                return result;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("content", out var content) ||
                content.GetArrayLength() == 0 ||
                !content[0].TryGetProperty("text", out var text))
            {
                result.Summary = "No he podido leer la respuesta del modelo.";
                return result;
            }

            var raw = text.GetString() ?? string.Empty;
            var json = ModelJson.Extract(raw);
            try
            {
                var parsed = JsonSerializer.Deserialize<RawAdvice>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                result.Available = true;
                result.Summary = parsed?.Summary ?? string.Empty;
                result.Insights = (parsed?.Insights ?? new())
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                    .Select(item => new MonthAdviceInsightDto
                    {
                        Severity = NormalizeSeverity(item.Severity),
                        Title = item.Title!.Trim(),
                        Detail = item.Detail?.Trim() ?? string.Empty
                    })
                    .ToList();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "La respuesta del asesor mensual no tenÃ­a el JSON esperado.");
                result.Available = true;
                result.Summary = ModelJson.PlainText(raw);
            }

            return result;
        }

        /// <summary>
        /// Resumen compacto del mes con la cronologÃ­a por cuenta y el saldo
        /// acumulado tras cada movimiento, para que la IA no tenga que sumar.
        /// </summary>
        public static object BuildMonthContext(MonthOutlookDto outlook)
        {
            var itemsByAccount = outlook.PendingItems
                .Where(item => item.AccountId.HasValue)
                .ToLookup(item => item.AccountId!.Value);

            var accounts = outlook.Accounts.Select(account =>
            {
                var balance = account.BaseBalance;
                var timeline = itemsByAccount[account.AccountId]
                    .OrderBy(item => item.DueDate)
                    .ThenBy(item => item.Direction == EntryDirection.In ? 0 : 1)
                    .Select(item =>
                    {
                        balance += item.Direction == EntryDirection.In ? item.Amount : -item.Amount;
                        return new
                        {
                            date = item.DueDate.ToString("yyyy-MM-dd"),
                            concept = item.Description,
                            amount = item.Direction == EntryDirection.In ? item.Amount : -item.Amount,
                            balanceAfter = balance
                        };
                    })
                    .ToList();

                return new
                {
                    name = account.Name,
                    startBalance = account.BaseBalance,
                    pendingIncome = account.PendingIncome,
                    pendingExpense = account.PendingExpense,
                    endBalance = account.ProjectedEndBalance,
                    lowestBalance = account.LowestBalance,
                    lowestBalanceDate = account.LowestBalanceDate?.ToString("yyyy-MM-dd"),
                    shortfall = account.Shortfall,
                    hasNoMovements = timeline.Count == 0,
                    timeline
                };
            }).ToList();

            return new
            {
                month = $"{outlook.Year}-{outlook.Month:00}",
                outlook.IsPast,
                outlook.IsCurrent,
                accounts,
                suggestedTransfers = outlook.SuggestedTransfers.Select(transfer => new
                {
                    from = transfer.FromAccountName,
                    to = transfer.ToAccountName,
                    transfer.Amount,
                    before = transfer.Before?.ToString("yyyy-MM-dd")
                }),
                uncoveredShortfall = outlook.UncoveredShortfall,
                freeMoneyAtMonthEnd = outlook.FreeMoney,
                variableExpenseReserve = outlook.VariableExpenseReserve,
                overdue = outlook.OverdueItems.Select(item => new
                {
                    date = item.DueDate.ToString("yyyy-MM-dd"),
                    concept = item.Description,
                    item.Amount,
                    account = item.AccountName
                }),
                deviations = outlook.Deviations
                    .Where(item => Math.Abs(item.Deviation) >= 1m && (item.Actual > 0m || item.Pending > 0m))
                    .Select(item => new { item.Name, item.Planned, item.Actual, item.Pending, item.Deviation })
            };
        }

        private static string NormalizeSeverity(string? severity) =>
            severity?.Trim().ToLowerInvariant() switch
            {
                "danger" => "danger",
                "warning" => "warning",
                "good" => "good",
                _ => "info"
            };

        private sealed class RawAdvice
        {
            public string? Summary { get; set; }
            public List<RawInsight>? Insights { get; set; }
        }

        private sealed class RawInsight
        {
            public string? Severity { get; set; }
            public string? Title { get; set; }
            public string? Detail { get; set; }
        }
    }
}
