using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ImportChatRowContext(Guid RowId, int RowNumber, string Date, decimal Amount, string Description, string? CurrentConcept);
    public sealed record ImportChatMessage(string Role, string Content);
    public sealed record ImportChatChange(Guid RowId, Guid? ConceptId);
    public sealed record ImportChatResult(string Reply, IReadOnlyList<ImportChatChange> Changes, IReadOnlyList<string> Unrecognized);

    public interface IImportChatService
    {
        Task<ImportChatResult> AskAsync(
            string userMessage,
            IReadOnlyList<ImportChatMessage> history,
            IReadOnlyList<ImportChatRowContext> rows,
            IReadOnlyList<ImportAiCandidate> concepts,
            CancellationToken ct);
    }

    /// <summary>
    /// Chat de revisión: el usuario puede corregir conceptos y preguntar por
    /// filas hablando en lugar de fila a fila con el desplegable. El modelo no
    /// toca nada directamente; sólo propone cambios que el controlador valida
    /// y aplica, igual que una sugerencia manual.
    /// </summary>
    public sealed class ImportChatService : IImportChatService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ImportChatService> _logger;

        public ImportChatService(HttpClient http, IConfiguration configuration, ILogger<ImportChatService> logger)
        {
            _http = http;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ImportChatResult> AskAsync(
            string userMessage,
            IReadOnlyList<ImportChatMessage> history,
            IReadOnlyList<ImportChatRowContext> rows,
            IReadOnlyList<ImportAiCandidate> concepts,
            CancellationToken ct)
        {
            var apiKey = _configuration["Anthropic:ApiKey"]
                ?? _configuration["ANTHROPIC_API_KEY"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ImportChatResult(
                    "No puedo hablar todavía: falta configurar la clave de Anthropic en el servidor.",
                    Array.Empty<ImportChatChange>(),
                    Array.Empty<string>());
            }

            var model = _configuration["Anthropic:Model"] ?? "claude-3-5-haiku-latest";
            var system = $$"""
                Eres el asistente de revisión de un importador bancario personal.
                El usuario te habla en español sobre los movimientos de su extracto.
                Puedes: explicar una fila, decir qué no reconoces, y proponer cambios
                de concepto cuando el usuario te lo pida explícitamente.

                CONCEPTOS VÁLIDOS (usa sólo estos "id"):
                {{JsonSerializer.Serialize(concepts)}}

                FILAS DEL LOTE (rowId, rowNumber, date, amount, description, currentConcept):
                {{JsonSerializer.Serialize(rows)}}

                Responde EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma:
                {"reply":"texto para el usuario","changes":[{"rowId":"guid","conceptId":"guid o null"}],"unrecognized":["texto que no entendiste, si lo hay"]}

                Si el usuario sólo pregunta (no pide cambios), deja "changes" vacío.
                Nunca inventes un rowId o conceptId que no esté en las listas de arriba.
                """;

            var messages = history
                .Select(h => new { role = h.Role, content = h.Content })
                .Append(new { role = "user", content = userMessage })
                .ToList();

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
                    messages
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic devolvió HTTP {StatusCode} en el chat de importación.", (int)response.StatusCode);
                return new ImportChatResult(
                    "El servicio de IA no ha respondido; inténtalo de nuevo en un momento.",
                    Array.Empty<ImportChatChange>(),
                    Array.Empty<string>());
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("content", out var content) ||
                content.GetArrayLength() == 0 ||
                !content[0].TryGetProperty("text", out var text))
                return new ImportChatResult("No he podido leer la respuesta del modelo.", Array.Empty<ImportChatChange>(), Array.Empty<string>());

            var json = text.GetString()?.Trim() ?? string.Empty;
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = json.IndexOf('\n');
                json = firstNewLine >= 0 ? json[(firstNewLine + 1)..] : json;
                json = json.TrimEnd('`', '\r', '\n').Trim();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<ChatResponseBody>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is null) return new ImportChatResult(json, Array.Empty<ImportChatChange>(), Array.Empty<string>());

                var validRowIds = rows.Select(r => r.RowId).ToHashSet();
                var validConceptIds = concepts.Select(c => c.Id).ToHashSet();

                var changes = (parsed.Changes ?? new())
                    .Where(c => validRowIds.Contains(c.RowId) && (c.ConceptId is null || validConceptIds.Contains(c.ConceptId.Value)))
                    .Select(c => new ImportChatChange(c.RowId, c.ConceptId))
                    .ToList();

                return new ImportChatResult(
                    parsed.Reply ?? string.Empty,
                    changes,
                    parsed.Unrecognized ?? new List<string>());
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "La respuesta del chat de importación no tenía el JSON esperado.");
                return new ImportChatResult(
                    "He tenido un problema entendiendo mi propia respuesta; prueba a reformular.",
                    Array.Empty<ImportChatChange>(),
                    Array.Empty<string>());
            }
        }

        private sealed class ChatResponseBody
        {
            public string? Reply { get; set; }
            public List<ChatChangeItem>? Changes { get; set; }
            public List<string>? Unrecognized { get; set; }
        }

        private sealed class ChatChangeItem
        {
            public Guid RowId { get; set; }
            public Guid? ConceptId { get; set; }
        }
    }
}
