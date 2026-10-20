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
            IReadOnlyList<string> analysis,
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
            IReadOnlyList<string> analysis,
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

            var model = _configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";
            var review = analysis.Count == 0
                ? "(sin datos)"
                : string.Join("\n", analysis.Select(line => "- " + line));
            var system = $$"""
                Eres el asistente de revisión de un importador bancario personal.
                El usuario te habla en español sobre los movimientos de su extracto.
                Puedes: explicar una fila, explicar la revisión automática, decir qué
                no reconoces, y proponer cambios de concepto cuando el usuario te lo
                pida explícitamente.
                Trata cada fila como un movimiento independiente: nunca agrupes,
                combines ni sumes transacciones distintas para clasificarlas.
                El importe positivo es un ingreso y el negativo un gasto; el concepto
                elegido debe tener el mismo tipo (Income o Expense). Los conceptos de
                tipo Transfer (traspasos) valen para importes positivos y negativos.
                Lee todo el historial antes de responder. La aclaración más reciente
                del usuario prevalece sobre una respuesta o cambio anterior del asistente.
                Si el usuario señala o pregunta si una clasificación anterior era
                incorrecta (por ejemplo, «¿no deberían ir por separado?»), trátalo
                como una corrección cuando el contexto identifique inequívocamente
                las filas y el significado de cada una. Corrige cada fila por separado.
                Si sólo pregunta cómo se muestran los movimientos, explícalo sin
                cambiarlos. Nunca presentes filas distintas como un único importe.
                Si no puedes identificar con seguridad la fila o el concepto, pregunta
                qué quiere hacer y no propongas cambios.

                CÓMO TRABAJA ESTE USUARIO (aplícalo al explicar y al proponer conceptos):
                - Dinero que se mueve entre sus propias cuentas (Revolut, la conjunta,
                  la hucha, efectivo) es un traspaso: usa el concepto Transfer de
                  movimientos entre sus bancos o el de la hucha, nunca un gasto o ingreso.
                - Dinero que no es suyo y sólo pasa por su cuenta (lo cobra para
                  dárselo a otra persona) va al concepto Transfer de dinero de paso.
                - Un gasto pagado los últimos días del mes puede ser el del mes
                  siguiente pagado por adelantado; un ingreso fuera de lo previsto de
                  quien le paga la nómina puede ser un adelanto de una paga. Plantéalo
                  como posibilidad y pregúntale; no lo des por hecho.
                - Si el saldo no cuadra, lo más habitual es que el extracto no empiece
                  el día 1 del mes, que falte un extracto anterior o que haya apuntes
                  hechos a mano en esa cuenta.
                - Explícalo todo fácil: frases cortas, sin tecnicismos, en español
                  neutro tuteando (tú). Di qué botón pulsar si hace falta.

                REVISIÓN AUTOMÁTICA DE ESTE EXTRACTO (la calculó la app con los datos
                reales; no la contradigas ni inventes cifras distintas):
                {{review}}

                CONCEPTOS VÁLIDOS (usa sólo estos "id"):
                {{JsonSerializer.Serialize(concepts)}}

                FILAS DEL LOTE (rowId, rowNumber, date, amount, description, currentConcept):
                {{JsonSerializer.Serialize(rows)}}

                Responde EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma:
                {"reply":"texto para el usuario","changes":[{"rowId":"guid","conceptId":"guid o null"}],"unrecognized":["texto que no entendiste, si lo hay"]}

                Si el usuario sólo pregunta (no pide cambios), deja "changes" vacío.
                Nunca inventes un rowId o conceptId que no esté en las listas de arriba.
                No afirmes que has cambiado filas salvo que las incluyas en "changes".
                Conserva las diferencias de significado que haya explicado el usuario:
                por ejemplo, un préstamo recibido y un reembolso para cubrir un gasto
                son movimientos distintos, aunque procedan de la misma persona.
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

            var raw = text.GetString() ?? string.Empty;
            var json = PersonalFinance.Api.Features.Chat.ModelJson.Extract(raw);

            try
            {
                var parsed = JsonSerializer.Deserialize<ChatResponseBody>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is null)
                    return new ImportChatResult(
                        PersonalFinance.Api.Features.Chat.ModelJson.PlainText(raw),
                        Array.Empty<ImportChatChange>(),
                        Array.Empty<string>());

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
                    PersonalFinance.Api.Features.Chat.ModelJson.PlainText(raw),
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
