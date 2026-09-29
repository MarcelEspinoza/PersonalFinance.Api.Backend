﻿using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;
using PersonalFinance.Domain.Advice.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Chat
{
    public interface IMonthAdvisorService
    {
        Task<MonthAdviceDto> AdviseAsync(Guid userId, int? year, int? month, CancellationToken ct);

        Task<AdviceReplyResultDto> ReplyAsync(Guid userId, AdviceReplyRequestDto request, CancellationToken ct);

        Task<List<AdviceNoteDto>> GetNotesAsync(Guid userId, int year, int month, CancellationToken ct);
    }

    /// <summary>Respuesta del usuario a un consejo, con lo que le contestó el asesor.</summary>
    public sealed class AdviceNoteDto
    {
        public string InsightTitle { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Reply { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public sealed class AdviceReplyRequestDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string InsightTitle { get; set; } = string.Empty;
        public string? InsightDetail { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class AdviceReplyResultDto
    {
        public bool Available { get; set; }
        public string Reply { get; set; } = string.Empty;
        /// <summary>Hilo completo de ese consejo, en orden cronológico.</summary>
        public List<AdviceNoteDto> Thread { get; set; } = new();
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
    /// Pide a la IA que lea la previsión del mes (y de los dos siguientes) y
    /// devuelva comentarios y consejos en lenguaje sencillo. Los cálculos los
    /// hace la app; la IA solo interpreta la cronología ya calculada para no
    /// inventar cifras.
    /// </summary>
    public sealed class MonthAdvisorService : IMonthAdvisorService
    {
        private const int MonthsAhead = 2;

        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly IDashboardService _dashboard;
        private readonly IAppDbContext _db;
        private readonly ILogger<MonthAdvisorService> _logger;

        public MonthAdvisorService(
            HttpClient http,
            IConfiguration configuration,
            IDashboardService dashboard,
            IAppDbContext db,
            ILogger<MonthAdvisorService> logger)
        {
            _http = http;
            _configuration = configuration;
            _dashboard = dashboard;
            _db = db;
            _logger = logger;
        }

        public async Task<MonthAdviceDto> AdviseAsync(Guid userId, int? year, int? month, CancellationToken ct)
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

            var notes = await LoadNotesAsync(userId, result.Year, result.Month, ct);

            var context = JsonSerializer.Serialize(new
            {
                today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                monthAnalysed = $"{result.Year}-{result.Month:00}",
                months,
                userFeedback = notes.Select(note => new
                {
                    sobreElConsejo = note.InsightTitle,
                    dijoElUsuario = note.UserMessage,
                    respondiste = note.AssistantReply
                })
            });

            var system = """
                Eres el asesor financiero personal del usuario dentro de su app. Lees la
                previsión de su mes y de los dos siguientes y le explicas, en español
                sencillo y cercano (tuteando), qué va a pasar con su dinero y qué debería
                hacer. Es una persona analítica: da cifras concretas y fechas.

                Los datos ya vienen calculados por la app. Para cada cuenta tienes el saldo
                de partida y la cronología de movimientos con el saldo acumulado tras cada
                uno ("balanceAfter"). NO recalcules ni inventes cifras: usa las que vienen.

                Explica sobre todo:
                - Si una cuenta termina el mes en positivo pero pasa por negativo, di que el
                  problema es de FECHAS (los pagos salen antes de que lleguen los cobros),
                  indica el día y el importe del peor momento y qué cobro lo arregla.
                - Qué pagos concretos se podrían mover a después del cobro principal, o qué
                  cobros convendría pedir antes, y cuánto mejora cada opción.
                - Las transferencias entre cuentas que la app recomienda.
                - Cuentas sin ningún movimiento previsto (probablemente sus gastos están
                  asignados a otra cuenta en Configuración).
                - La tendencia de los meses siguientes (si mejora o empeora y por qué).
                - Gastos variables que se están desviando del límite.

                "userFeedback" son las respuestas que el usuario te ha dado a consejos
                anteriores de este mes. Respétalas: si ya te dijo que algo no aplica, que ya
                lo tiene resuelto o que el dato es otro, no repitas ese consejo y ten en
                cuenta lo que te contó al redactar los nuevos.

                Responde EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma:
                {"summary":"2-3 frases con la conclusión principal",
                 "insights":[{"severity":"danger|warning|info|good","title":"titular corto","detail":"explicación y consejo concreto"}]}
                Entre 3 y 7 insights, ordenados de más a menos urgente.
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
                _logger.LogWarning("Anthropic devolvió HTTP {StatusCode} al aconsejar el mes.", (int)response.StatusCode);
                result.Summary = "El servicio de IA no ha respondido; inténtalo de nuevo en un momento.";
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
                _logger.LogWarning(ex, "La respuesta del asesor mensual no tenía el JSON esperado.");
                result.Available = true;
                result.Summary = ModelJson.PlainText(raw);
            }

            return result;
        }

        /// <summary>
        /// Contesta a un consejo concreto. Ve la previsión del mes, el consejo al
        /// que responde y el hilo anterior, y guarda la conversación para que el
        /// próximo análisis la tenga en cuenta.
        /// </summary>
        public async Task<AdviceReplyResultDto> ReplyAsync(
            Guid userId,
            AdviceReplyRequestDto request,
            CancellationToken ct)
        {
            var message = request.Message?.Trim() ?? string.Empty;
            if (message.Length == 0)
                return new AdviceReplyResultDto { Reply = "Escribe una respuesta para poder contestarte." };

            var title = (request.InsightTitle ?? string.Empty).Trim();
            if (title.Length > 200) title = title[..200];

            var thread = await LoadNotesAsync(userId, request.Year, request.Month, ct);

            var apiKey = _configuration["Anthropic:ApiKey"]
                ?? _configuration["ANTHROPIC_API_KEY"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new AdviceReplyResultDto
                {
                    Reply = "Falta configurar la clave de Anthropic en el servidor.",
                    Thread = ToDtos(thread.Where(note => note.InsightTitle == title))
                };
            }

            var projection = await _dashboard.GetFutureProjectionAsync(request.Year, request.Month, ct);
            var context = JsonSerializer.Serialize(new
            {
                today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                month = BuildMonthContext(projection.Outlook),
                consejo = new { titulo = title, detalle = request.InsightDetail },
                conversacionPrevia = thread
                    .Where(note => note.InsightTitle == title)
                    .Select(note => new { dijoElUsuario = note.UserMessage, respondiste = note.AssistantReply }),
                otrosComentariosDelUsuario = thread
                    .Where(note => note.InsightTitle != title)
                    .Select(note => new { sobreElConsejo = note.InsightTitle, dijoElUsuario = note.UserMessage }),
                respuestaDelUsuario = message
            });

            var system = """
                Eres el asesor financiero personal del usuario dentro de su app. El usuario
                está respondiendo a UN consejo concreto que le diste sobre la previsión de su
                mes. Contéstale en español, tuteándole, de forma breve y directa.

                Tienes la previsión completa del mes ya calculada por la app: usa esas cifras
                y fechas, no las recalcules ni te las inventes.

                - Si te corrige o te da un dato que tú no tenías, acéptalo sin discutir y
                  recalcula tu consejo con esa información.
                - Si te dice que el consejo no le sirve, propón una alternativa concreta en
                  lugar de repetir lo mismo.
                - Si te pregunta algo que los datos no responden, dilo con honestidad y di
                  qué necesitarías.
                - No repitas el consejo entero: ve al grano, máximo 4 o 5 frases.

                Responde EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma exacta:
                {"reply":"tu respuesta para el usuario"}
                """;

            var model = _configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = 1024,
                    temperature = 0,
                    system,
                    messages = new[] { new { role = "user", content = context } }
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic devolvió HTTP {StatusCode} al responder a un consejo.", (int)response.StatusCode);
                return new AdviceReplyResultDto
                {
                    Reply = "El servicio de IA no ha respondido; inténtalo de nuevo en un momento.",
                    Thread = ToDtos(thread.Where(note => note.InsightTitle == title))
                };
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("content", out var content) ||
                content.GetArrayLength() == 0 ||
                !content[0].TryGetProperty("text", out var text))
            {
                return new AdviceReplyResultDto
                {
                    Reply = "No he podido leer la respuesta del modelo.",
                    Thread = ToDtos(thread.Where(note => note.InsightTitle == title))
                };
            }

            var raw = text.GetString() ?? string.Empty;
            string reply;
            try
            {
                var parsed = JsonSerializer.Deserialize<RawReply>(ModelJson.Extract(raw),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                reply = string.IsNullOrWhiteSpace(parsed?.Reply) ? ModelJson.PlainText(raw) : parsed!.Reply!.Trim();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "La respuesta a un consejo no tenía el JSON esperado.");
                reply = ModelJson.PlainText(raw);
            }

            var note = new AdviceNote
            {
                UserId = userId,
                Year = request.Year,
                Month = request.Month,
                InsightTitle = title,
                UserMessage = message,
                AssistantReply = reply
            };
            _db.AdviceNotes.Add(note);
            await _db.SaveChangesAsync(ct);

            thread.Add(note);
            return new AdviceReplyResultDto
            {
                Available = true,
                Reply = reply,
                Thread = ToDtos(thread.Where(item => item.InsightTitle == title))
            };
        }

        public async Task<List<AdviceNoteDto>> GetNotesAsync(Guid userId, int year, int month, CancellationToken ct)
        {
            var notes = await LoadNotesAsync(userId, year, month, ct);
            return ToDtos(notes);
        }

        private async Task<List<AdviceNote>> LoadNotesAsync(Guid userId, int year, int month, CancellationToken ct)
        {
            return await _db.AdviceNotes
                .AsNoTracking()
                .Where(note => note.UserId == userId && note.Year == year && note.Month == month)
                .OrderBy(note => note.CreatedAt)
                .ToListAsync(ct);
        }

        private static List<AdviceNoteDto> ToDtos(IEnumerable<AdviceNote> notes) =>
            notes.Select(note => new AdviceNoteDto
            {
                InsightTitle = note.InsightTitle,
                Message = note.UserMessage,
                Reply = note.AssistantReply,
                CreatedAt = note.CreatedAt
            }).ToList();

        /// <summary>
        /// Resumen compacto del mes con la cronología por cuenta y el saldo
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

        private sealed class RawReply
        {
            public string? Reply { get; set; }
        }

        private sealed class RawInsight
        {
            public string? Severity { get; set; }
            public string? Title { get; set; }
            public string? Detail { get; set; }
        }
    }
}
