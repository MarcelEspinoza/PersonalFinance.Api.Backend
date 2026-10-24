using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PersonalFinance.Api.Features.Chat;

namespace PersonalFinance.Api.Features.ServiceSavings
{
    /// <summary>
    /// Pages that really came back from web search. Sources given by the model are
    /// only kept when they point to one of them (or to another page of the same site).
    /// </summary>
    internal sealed class SeenSources
    {
        private const int MaxTitleLength = 150;

        private readonly Dictionary<string, ServiceSavingSourceDto> _byPage = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ServiceSavingSourceDto> _bySite = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byPage.Count;

        public void Add(string? url, string? title)
        {
            if (!TryParseWebUrl(url, out var uri)) return;

            var source = new ServiceSavingSourceDto
            {
                Url = uri.AbsoluteUri,
                Title = ServiceSavingsResponse.Clean(title, MaxTitleLength) ?? SiteOf(uri)
            };
            _byPage.TryAdd(PageOf(uri), source);
            _bySite.TryAdd(SiteOf(uri), source);
        }

        public ServiceSavingSourceDto? Match(string? url, string? title)
        {
            if (!TryParseWebUrl(url, out var uri)) return null;

            if (_byPage.TryGetValue(PageOf(uri), out var page))
            {
                return new ServiceSavingSourceDto
                {
                    Url = page.Url,
                    Title = ServiceSavingsResponse.Clean(title, MaxTitleLength) ?? page.Title
                };
            }

            return _bySite.TryGetValue(SiteOf(uri), out var site)
                ? new ServiceSavingSourceDto { Url = site.Url, Title = site.Title }
                : null;
        }

        private static bool TryParseWebUrl(string? url, [NotNullWhen(true)] out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return false;
            if (string.IsNullOrEmpty(parsed.Host)) return false;

            uri = parsed;
            return true;
        }

        private static string SiteOf(Uri uri)
        {
            var host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        }

        private static string PageOf(Uri uri) => SiteOf(uri) + uri.AbsolutePath.TrimEnd('/') + uri.Query;
    }

    internal sealed record ParsedServiceReview(string? Summary, List<ServiceSavingItemDto> Items);

    /// <summary>Reads the Anthropic content blocks and validates the suggestions.</summary>
    internal static class ServiceSavingsResponse
    {
        public const int MaxItems = 10;
        private const int MaxSourcesPerItem = 3;

        private static readonly string[] Actions = { "cheaper", "cancel", "switch" };

        /// <summary>The model's final answer: the text written after the last search result.</summary>
        public static string FinalText(IReadOnlyList<JsonElement> blocks)
        {
            var lastResult = -1;
            for (var i = 0; i < blocks.Count; i++)
            {
                if (TypeOf(blocks[i]) == "web_search_tool_result") lastResult = i;
            }

            var text = JoinText(blocks, lastResult + 1);
            return text.Contains('{') || lastResult < 0 ? text : JoinText(blocks, 0);
        }

        public static int CountSearchCalls(IReadOnlyList<JsonElement> blocks) =>
            blocks.Count(block =>
                TypeOf(block) == "server_tool_use" &&
                string.Equals(ReadString(block, "name"), "web_search", StringComparison.Ordinal));

        /// <summary>URLs returned by web search or cited by the model. Search errors are ignored.</summary>
        public static SeenSources CollectSources(IReadOnlyList<JsonElement> blocks)
        {
            var seen = new SeenSources();
            foreach (var block in blocks)
            {
                switch (TypeOf(block))
                {
                    case "web_search_tool_result":
                        if (block.TryGetProperty("content", out var results) && results.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var result in results.EnumerateArray())
                            {
                                if (TypeOf(result) == "web_search_result")
                                    seen.Add(ReadString(result, "url"), ReadString(result, "title"));
                            }
                        }
                        break;

                    case "text":
                        if (block.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var citation in citations.EnumerateArray())
                            {
                                if (TypeOf(citation) == "web_search_result_location")
                                    seen.Add(ReadString(citation, "url"), ReadString(citation, "title"));
                            }
                        }
                        break;
                }
            }

            return seen;
        }

        /// <summary>
        /// Parses the model's JSON and keeps only valid suggestions about known,
        /// not hidden services. Returns null when the text has no JSON object.
        /// </summary>
        public static ParsedServiceReview? Parse(string text, ServiceSavingsContext context, SeenSources seen)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(ModelJson.Extract(text));
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                var summary = Clean(ReadString(root, "summary"), 1000);
                var hasItems = TryGetProperty(root, "items", out var array) && array.ValueKind == JsonValueKind.Array;
                if (!hasItems && summary is null) return null;

                var items = new List<ServiceSavingItemDto>();
                if (hasItems)
                {
                    foreach (var raw in array.EnumerateArray())
                    {
                        var item = ToItem(raw, context, seen);
                        if (item is not null) items.Add(item);
                    }
                }

                var ordered = items
                    .OrderByDescending(item => item.MonthlySaving ?? 0)
                    .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(MaxItems)
                    .ToList();

                return new ParsedServiceReview(summary, ordered);
            }
        }

        private static ServiceSavingItemDto? ToItem(JsonElement raw, ServiceSavingsContext context, SeenSources seen)
        {
            if (raw.ValueKind != JsonValueKind.Object) return null;

            var candidate = context.Find(ReadString(raw, "key"));
            if (candidate is null || context.DismissedKeys.Contains(candidate.Key)) return null;

            var action = ReadString(raw, "action")?.Trim().ToLowerInvariant();
            if (action is null || !Actions.Contains(action)) return null;

            var current = PlausibleCurrent(ReadDecimal(raw, "currentMonthly"), candidate.MonthlyAmount);
            decimal? proposalMonthly = null;
            decimal? saving;
            if (action == "cancel")
            {
                saving = current;
            }
            else
            {
                proposalMonthly = NonNegative(ReadDecimal(raw, "proposalMonthly"));
                var claimed = ReadDecimal(raw, "monthlySaving");
                if (current is null) saving = null;
                else if (proposalMonthly is not null) saving = Clamp(current.Value - proposalMonthly.Value, current.Value);
                else if (claimed is not null) saving = Clamp(claimed.Value, current.Value);
                else saving = null;
            }

            if (action == "cheaper" && !(saving > 0)) return null;

            var sources = ReadSources(raw, seen);
            var caution = Clean(ReadString(raw, "caution"), 400);
            if (sources.Count == 0 && action != "cancel")
            {
                caution = caution is null
                    ? ServiceSavingsMessages.UnverifiedOffer
                    : $"{caution} {ServiceSavingsMessages.UnverifiedOffer}";
            }

            return new ServiceSavingItemDto
            {
                Key = candidate.Key,
                Service = Clean(ReadString(raw, "service"), 120) ?? candidate.Name,
                Provider = Clean(ReadString(raw, "provider"), 120),
                Category = Clean(ReadString(raw, "category"), 80),
                Action = action,
                CurrentMonthly = Round(current),
                Proposal = Clean(ReadString(raw, "proposal"), 300),
                ProposalMonthly = Round(proposalMonthly),
                MonthlySaving = Round(saving),
                Detail = Clean(ReadString(raw, "detail"), 800),
                Caution = caution,
                Sources = sources
            };
        }

        /// <summary>
        /// The model's current monthly cost, unless it is missing or far from what
        /// the ledger says (then the ledger amount wins).
        /// </summary>
        private static decimal? PlausibleCurrent(decimal? fromModel, decimal? fromContext)
        {
            if (fromModel is not > 0) return fromContext is > 0 ? fromContext : null;
            if (fromContext is > 0 && (fromModel > fromContext * 3 || fromModel < fromContext / 3)) return fromContext;
            return fromModel;
        }

        private static List<ServiceSavingSourceDto> ReadSources(JsonElement raw, SeenSources seen)
        {
            var sources = new List<ServiceSavingSourceDto>();
            if (!TryGetProperty(raw, "sources", out var array) || array.ValueKind != JsonValueKind.Array) return sources;

            foreach (var source in array.EnumerateArray())
            {
                string? url = null;
                string? title = null;
                if (source.ValueKind == JsonValueKind.String)
                {
                    url = source.GetString();
                }
                else if (source.ValueKind == JsonValueKind.Object)
                {
                    url = ReadString(source, "url");
                    title = ReadString(source, "title");
                }

                var match = seen.Match(url, title);
                if (match is null || sources.Any(existing => existing.Url == match.Url)) continue;

                sources.Add(match);
                if (sources.Count == MaxSourcesPerItem) break;
            }

            return sources;
        }

        /// <summary>Single-line text without control characters, cut to <paramref name="maxLength"/>.</summary>
        internal static string? Clean(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var builder = new StringBuilder(text.Length);
            var pendingSpace = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;

                if (pendingSpace) builder.Append(' ');
                pendingSpace = false;
                builder.Append(c);
            }

            var cleaned = builder.ToString();
            if (cleaned.Length == 0 || cleaned.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            if (cleaned.Length <= maxLength) return cleaned;

            var cut = maxLength - 1;
            if (char.IsHighSurrogate(cleaned[cut - 1])) cut--;
            return cleaned[..cut].TrimEnd() + "…";
        }

        private static string JoinText(IReadOnlyList<JsonElement> blocks, int from)
        {
            var builder = new StringBuilder();
            for (var i = from; i < blocks.Count; i++)
            {
                if (TypeOf(blocks[i]) == "text" &&
                    blocks[i].TryGetProperty("text", out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    builder.Append(value.GetString());
                }
            }

            return builder.ToString();
        }

        private static string? TypeOf(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object) return false;
            if (element.TryGetProperty(name, out value)) return true;

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        private static string? ReadString(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        private static decimal? ReadDecimal(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number) return value.TryGetDecimal(out var number) ? number : null;
            if (value.ValueKind != JsonValueKind.String) return null;

            var text = value.GetString()?.Replace("€", string.Empty).Replace(',', '.').Trim();
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        private static decimal? NonNegative(decimal? value) => value is >= 0 ? value : null;

        private static decimal Clamp(decimal value, decimal max) => Math.Min(Math.Max(value, 0), max);

        private static decimal? Round(decimal? value) =>
            value is null ? null : Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }
}
