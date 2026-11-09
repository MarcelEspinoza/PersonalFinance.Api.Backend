using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PersonalFinance.Api.Features.Chat
{
    /// <summary>
    /// Ajustes comunes de las llamadas a la API de mensajes de Anthropic.
    /// Los modelos Claude 4.7 o posteriores (incluido Opus 5.5) rechazan
    /// "temperature", "top_p" y "top_k" con un HTTP 400, así que ninguna
    /// petición debe enviarlos.
    /// </summary>
    public static class AnthropicMessages
    {
        public const string DefaultModel = "claude-sonnet-5-5";

        /// <summary>
        /// Resolves the task-specific model, provider model, or provider default.
        /// </summary>
        public static string ResolveModel(IConfiguration configuration, string? task = null)
        {
            var provider = ResolveProvider(configuration);
            var taskModel = task is null ? null : configuration[$"Ai:Models:{task}"];
            var configured = configuration[$"{provider}:Model"];
            var model = !string.IsNullOrWhiteSpace(taskModel)
                ? taskModel.Trim()
                : !string.IsNullOrWhiteSpace(configured)
                    ? configured.Trim()
                    : provider switch
                    {
                        "Poe" => "Claude-Sonnet-4.6",
                        "OpenRouter" when task == "Savings" => "anthropic/claude-sonnet-4.6",
                        "OpenRouter" => "openai/gpt-6.1-sol",
                        _ => DefaultModel
                    };
            if (provider == "Poe" && !model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The Poe Messages endpoint supports only official Claude models. Use OpenRouter for GPT models.");
            return model;
        }

        public static string? ResolveApiKey(IConfiguration configuration)
        {
            var provider = ResolveProvider(configuration);
            var environmentKey = provider.ToUpperInvariant() + "_API_KEY";
            return configuration[$"{provider}:ApiKey"]
                ?? configuration[environmentKey]
                ?? Environment.GetEnvironmentVariable(environmentKey);
        }

        public static HttpRequestMessage CreateRequest(IConfiguration configuration, string apiKey)
        {
            var provider = ResolveProvider(configuration);
            var url = provider switch
            {
                "Poe" => "https://api.poe.com/v1/messages",
                "OpenRouter" => "https://openrouter.ai/api/v1/messages",
                _ => "https://api.anthropic.com/v1/messages"
            };
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (provider == "OpenRouter")
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            else
                request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static string ResolveProvider(IConfiguration configuration)
        {
            var configured = configuration["Ai:Provider"];
            return configured?.Trim().ToLowerInvariant() switch
            {
                null or "" or "anthropic" => "Anthropic",
                "poe" => "Poe",
                "openrouter" => "OpenRouter",
                _ => throw new InvalidOperationException("Ai:Provider must be Anthropic, Poe, or OpenRouter.")
            };
        }

        /// <summary>
        /// Une el texto de todos los bloques "text" de la respuesta. Los modelos
        /// recientes pueden devolver otros bloques (por ejemplo, de razonamiento)
        /// antes del texto, así que no basta con leer el primero.
        /// </summary>
        public static bool TryGetText(JsonElement root, out string text)
        {
            text = string.Empty;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
                return false;

            var builder = new StringBuilder();
            var found = false;
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (block.TryGetProperty("type", out var type) &&
                    type.ValueKind == JsonValueKind.String &&
                    !string.Equals(type.GetString(), "text", StringComparison.Ordinal))
                    continue;
                if (!block.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                    continue;

                builder.Append(value.GetString());
                found = true;
            }

            text = builder.ToString();
            return found;
        }

        /// <summary>
        /// Convierte el historial que manda el cliente en la lista "messages".
        /// Sólo se aceptan los roles "user" y "assistant" (un "system" u otro
        /// rol serviría para inyectar instrucciones). Se toman los últimos
        /// <paramref name="maxHistoryMessages"/>, se tapan los datos sensibles,
        /// se quitan los turnos vacíos y, como la API no admite prefill del
        /// asistente, la lista empieza y termina con el usuario y une los
        /// turnos seguidos del mismo rol.
        /// </summary>
        internal static List<object> BuildMessages(
            IEnumerable<(string Role, string Content)> history,
            string? userMessage,
            int maxHistoryMessages,
            int maxMessageLength)
        {
            var turns = history
                .Where(h => h.Role is "user" or "assistant")
                .TakeLast(maxHistoryMessages)
                .Select(h => (Role: h.Role, Content: SensitiveDataMasker.ForPrompt(h.Content, maxMessageLength, keepNewLines: true)))
                .Append((Role: "user", Content: SensitiveDataMasker.ForPrompt(userMessage, maxMessageLength, keepNewLines: true)))
                .Where(turn => turn.Content.Length > 0)
                .SkipWhile(turn => turn.Role != "user");

            var merged = new List<(string Role, string Content)>();
            foreach (var turn in turns)
            {
                if (merged.Count > 0 && merged[^1].Role == turn.Role)
                    merged[^1] = (turn.Role, merged[^1].Content + "\n\n" + turn.Content);
                else
                    merged.Add(turn);
            }

            while (merged.Count > 0 && merged[^1].Role != "user") merged.RemoveAt(merged.Count - 1);

            return merged.Select(turn => (object)new { role = turn.Role, content = turn.Content }).ToList();
        }
    }
}
