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
        public const string DefaultModel = "claude-opus-5-5";

        /// <summary>
        /// Modelo configurado en "Anthropic:Model" (variable Anthropic__Model)
        /// o, si no hay ninguno, el modelo por defecto.
        /// </summary>
        public static string ResolveModel(IConfiguration configuration)
        {
            var configured = configuration["Anthropic:Model"];
            return string.IsNullOrWhiteSpace(configured) ? DefaultModel : configured.Trim();
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
