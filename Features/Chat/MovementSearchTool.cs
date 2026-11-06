using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using EntryDirection = PersonalFinance.Domain.Ledger.Enums.EntryDirection;
using EntryStatus = PersonalFinance.Domain.Ledger.Enums.EntryStatus;

namespace PersonalFinance.Api.Features.Chat
{
    /// <summary>
    /// Herramienta "search_movements" del chat: deja a la IA buscar en todos los
    /// movimientos del usuario, sin límite de fechas, antes de responder.
    /// </summary>
    public static class MovementSearchTool
    {
        public const string Name = "search_movements";
        private const int DefaultLimit = 50;
        private const int MaxLimit = 100;
        private const int MaxWords = 6;

        public static object Definition { get; } = new
        {
            name = Name,
            description =
                "Busca movimientos del usuario en TODO su historial («Mes»), de cualquier fecha, pasada o futura. " +
                "Todos los filtros son opcionales y se combinan. Devuelve los más recientes primero, con su id, " +
                "que sirve igual que el Id de recentLedgerEntries (targetId, sourceEntryId, ledgerEntryId).",
            input_schema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["text"] = new
                    {
                        type = "string",
                        description = "Palabras a buscar (persona, comercio, concepto, cuenta). Deben aparecer todas, en la descripción, las notas, el concepto o la cuenta."
                    },
                    ["amount"] = new { type = "number", description = "Importe exacto, en positivo (por ejemplo 176.01)." },
                    ["minAmount"] = new { type = "number", description = "Importe mínimo, en positivo." },
                    ["maxAmount"] = new { type = "number", description = "Importe máximo, en positivo." },
                    ["from"] = new { type = "string", description = "Fecha inicial incluida, YYYY-MM-DD." },
                    ["to"] = new { type = "string", description = "Fecha final incluida, YYYY-MM-DD." },
                    ["direction"] = new { type = "string", @enum = new[] { "in", "out" }, description = "in = cobros, out = pagos." },
                    ["limit"] = new { type = "integer", description = $"Máximo de resultados (1-{MaxLimit}, por defecto {DefaultLimit})." }
                }
            }
        };

        public sealed record FoundMovement(
            Guid Id,
            DateOnly DueDate,
            EntryDirection Direction,
            EntryStatus Status,
            bool IsTransfer,
            decimal Amount,
            decimal ForecastAmount,
            decimal? ActualAmount,
            string? Description,
            string? Notes,
            Guid ConceptId,
            string? Concept,
            Guid? AccountId,
            string? Account);

        public sealed record SearchResult(int Total, IReadOnlyList<FoundMovement> Movements, string? Error);

        public static async Task<SearchResult> ExecuteAsync(AppDbContext db, Guid userId, JsonElement input, CancellationToken ct)
        {
            if (input.ValueKind != JsonValueKind.Object)
                return new SearchResult(0, [], "La búsqueda necesita un objeto con filtros.");

            var query = db.LedgerEntries.AsNoTracking().Where(entry => entry.UserId == userId);

            if (ReadString(input, "text") is { } text)
            {
                foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(MaxWords))
                {
                    query = query.Where(entry =>
                        (entry.Description != null && entry.Description.Contains(word)) ||
                        (entry.Notes != null && entry.Notes.Contains(word)) ||
                        (entry.Concept != null && entry.Concept.Name.Contains(word)) ||
                        (entry.Account != null && entry.Account.Name.Contains(word)));
                }
            }

            if (ReadDecimal(input, "amount") is decimal amount)
            {
                var exact = Math.Abs(amount);
                query = query.Where(entry => (entry.ActualAmount ?? entry.ForecastAmount) == exact);
            }

            if (ReadDecimal(input, "minAmount") is decimal min)
            {
                var lower = Math.Abs(min);
                query = query.Where(entry => (entry.ActualAmount ?? entry.ForecastAmount) >= lower);
            }

            if (ReadDecimal(input, "maxAmount") is decimal max)
            {
                var upper = Math.Abs(max);
                query = query.Where(entry => (entry.ActualAmount ?? entry.ForecastAmount) <= upper);
            }

            if (ReadDate(input, "from") is DateOnly from) query = query.Where(entry => entry.DueDate >= from);
            if (ReadDate(input, "to") is DateOnly to) query = query.Where(entry => entry.DueDate <= to);

            switch (ReadString(input, "direction")?.ToLowerInvariant())
            {
                case "in":
                    query = query.Where(entry => entry.Direction == EntryDirection.In);
                    break;
                case "out":
                    query = query.Where(entry => entry.Direction == EntryDirection.Out);
                    break;
            }

            var limit = Math.Clamp(ReadInt(input, "limit") ?? DefaultLimit, 1, MaxLimit);
            var total = await query.CountAsync(ct);
            var movements = await query
                .OrderByDescending(entry => entry.DueDate)
                .ThenBy(entry => entry.Id)
                .Take(limit)
                .Select(entry => new FoundMovement(
                    entry.Id,
                    entry.DueDate,
                    entry.Direction,
                    entry.Status,
                    entry.IsTransfer,
                    entry.ActualAmount ?? entry.ForecastAmount,
                    entry.ForecastAmount,
                    entry.ActualAmount,
                    entry.Description,
                    entry.Notes,
                    entry.ConceptId,
                    entry.Concept != null ? entry.Concept.Name : null,
                    entry.AccountId,
                    entry.Account != null ? entry.Account.Name : null))
                .ToListAsync(ct);

            return new SearchResult(total, movements, null);
        }

        private static string? ReadString(JsonElement input, string name) =>
            input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;

        private static decimal? ReadDecimal(JsonElement input, string name)
        {
            if (!input.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
            return value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString()?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
        }

        private static int? ReadInt(JsonElement input, string name) =>
            input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : null;

        private static DateOnly? ReadDate(JsonElement input, string name) =>
            ReadString(input, name) is { } text &&
            DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }
}
