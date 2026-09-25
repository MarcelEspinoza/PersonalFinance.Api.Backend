using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ImportAiCandidate(Guid Id, string Name, string Kind);
    public sealed record ImportAiSuggestion(Guid ConceptId, decimal Confidence);

    public interface IImportAiSuggestionService
    {
        Task<IReadOnlyDictionary<string, ImportAiSuggestion>> SuggestAsync(
            IReadOnlyList<string> descriptions,
            IReadOnlyList<ImportAiCandidate> concepts,
            CancellationToken ct);
    }

    public sealed class ImportAiSuggestionService : IImportAiSuggestionService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ImportAiSuggestionService> _logger;

        public ImportAiSuggestionService(
            HttpClient http,
            IConfiguration configuration,
            ILogger<ImportAiSuggestionService> logger)
        {
            _http = http;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, ImportAiSuggestion>> SuggestAsync(
            IReadOnlyList<string> descriptions,
            IReadOnlyList<ImportAiCandidate> concepts,
            CancellationToken ct)
        {
            var result = new Dictionary<string, ImportAiSuggestion>(StringComparer.OrdinalIgnoreCase);
            if (descriptions.Count == 0 || concepts.Count == 0) return result;

            var apiKey = _configuration["Anthropic:ApiKey"]
                ?? _configuration["ANTHROPIC_API_KEY"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Anthropic no está configurado; las filas sin mapping quedan para revisión manual.");
                return result;
            }

            var model = _configuration["Anthropic:Model"] ?? "claude-3-5-haiku-latest";
            var prompt = $$"""
                Clasifica cada descripción bancaria en uno de los conceptos permitidos.
                Devuelve exclusivamente JSON válido, sin markdown, con esta forma:
                [{"description":"texto exacto","conceptId":"guid","confidence":0.0}]
                Usa confidence entre 0 y 1. Si no hay una clasificación razonable, omite la fila.

                CONCEPTOS:
                {{JsonSerializer.Serialize(concepts)}}

                DESCRIPCIONES:
                {{JsonSerializer.Serialize(descriptions)}}
                """;

            using var request = new HttpRequestMessage(
                HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = 4096,
                    temperature = 0,
                    messages = new[] { new { role = "user", content = prompt } }
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Anthropic devolvió HTTP {StatusCode}; las filas sin mapping quedan para revisión manual.",
                    (int)response.StatusCode);
                return result;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("content", out var content) ||
                content.GetArrayLength() == 0 ||
                !content[0].TryGetProperty("text", out var text))
                return result;

            var json = text.GetString()?.Trim() ?? string.Empty;
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = json.IndexOf('\n');
                json = firstNewLine >= 0 ? json[(firstNewLine + 1)..] : json;
                json = json.TrimEnd('`', '\r', '\n').Trim();
            }

            try
            {
                foreach (var item in JsonSerializer.Deserialize<List<AiResponseItem>>(json) ?? new())
                {
                    if (item.ConceptId == Guid.Empty ||
                        string.IsNullOrWhiteSpace(item.Description) ||
                        item.Confidence is < 0 or > 1 ||
                        !concepts.Any(c => c.Id == item.ConceptId))
                        continue;

                    var normalized = RevolutMovementClassifier.Normalize(item.Description);
                    result[normalized] = new ImportAiSuggestion(item.ConceptId, item.Confidence);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "La respuesta de Anthropic no tenía el JSON esperado.");
            }

            return result;
        }

        private sealed class AiResponseItem
        {
            public string Description { get; set; } = string.Empty;
            public Guid ConceptId { get; set; }
            public decimal Confidence { get; set; }
        }
    }
}
