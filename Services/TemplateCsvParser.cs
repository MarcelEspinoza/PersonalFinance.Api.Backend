using System.Text;

namespace PersonalFinance.Api.Services
{
    public sealed record TemplateImportRow(
        int RowNumber,
        string Description,
        string Amount,
        string Date,
        string Category,
        string Notes,
        string Type,
        string MovementType,
        string BankOrigin,
        string IsTransfer,
        string BankDestination,
        string TransferReference,
        string Loan);

    public static class TemplateCsvParser
    {
        private static readonly char[] Delimiters = { ';', ',' };

        private static readonly IReadOnlyDictionary<string, string[]> Headers =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["description"] = new[] { "description", "descripcion" },
                ["amount"] = new[] { "amount", "importe", "monto" },
                ["date"] = new[] { "date", "fecha" },
                ["category"] = new[] { "category", "categoryname", "categoria" },
                ["notes"] = new[] { "notes", "notas" },
                ["type"] = new[] { "type", "tipo" },
                ["movementtype"] = new[] { "movementtype", "tipomovimiento" },
                ["bankorigin"] = new[] { "bankorigin", "bank", "bancoorigen", "cuentaorigen" },
                ["istransfer"] = new[] { "istransfer", "transferencia" },
                ["bankdestination"] = new[] { "bankdestination", "counterpartybank", "bancodestino", "cuentadestino" },
                ["transferreference"] = new[] { "transferreference", "referenciatransferencia" },
                ["loan"] = new[] { "loan", "prestamo" }
            };

        private static readonly string[] RequiredHeaders =
        {
            "description", "amount", "date", "category", "type", "movementtype", "bankorigin"
        };

        public static IReadOnlyList<TemplateImportRow> Parse(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            var headerLine = reader.ReadLine();
            if (headerLine is null)
                throw new InvalidDataException("El CSV está vacío.");

            headerLine = StripBom(headerLine);
            var delimiter = DetectDelimiter(headerLine);
            var header = SplitLine(headerLine, delimiter);
            var indexes = BuildIndexes(header);

            var missing = RequiredHeaders.Where(key => !indexes.ContainsKey(key)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException(
                    $"El CSV no tiene las columnas obligatorias: {string.Join(", ", missing)}.");

            var rows = new List<TemplateImportRow>();
            var rowNumber = 1;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                rowNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = SplitLine(line, delimiter);
                string Field(string key) =>
                    indexes.TryGetValue(key, out var index) && index < fields.Count
                        ? fields[index].Trim()
                        : string.Empty;

                rows.Add(new TemplateImportRow(
                    rowNumber,
                    Field("description"),
                    Field("amount"),
                    Field("date"),
                    Field("category"),
                    Field("notes"),
                    Field("type"),
                    Field("movementtype"),
                    Field("bankorigin"),
                    Field("istransfer"),
                    Field("bankdestination"),
                    Field("transferreference"),
                    Field("loan")));
            }

            return rows;
        }

        private static Dictionary<string, int> BuildIndexes(IReadOnlyList<string> header)
        {
            var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < header.Count; index++)
            {
                var normalized = NormalizeHeader(header[index]);
                foreach (var definition in Headers)
                {
                    if (definition.Value.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        indexes.TryAdd(definition.Key, index);
                        break;
                    }
                }
            }

            return indexes;
        }

        private static char DetectDelimiter(string headerLine)
        {
            return Delimiters
                .Select(delimiter => new
                {
                    Delimiter = delimiter,
                    Score = SplitLine(headerLine, delimiter)
                        .Select(NormalizeHeader)
                        .Count(header => Headers.Values.Any(
                            aliases => aliases.Contains(header, StringComparer.OrdinalIgnoreCase)))
                })
                .OrderByDescending(candidate => candidate.Score)
                .First()
                .Delimiter;
        }

        private static string NormalizeHeader(string value)
        {
            var normalized = value.Trim().ToLowerInvariant()
                .Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) ==
                    System.Globalization.UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(character))
                    builder.Append(character);
            }

            return builder.ToString();
        }

        private static string StripBom(string line) =>
            line.Length > 0 && line[0] == '\uFEFF' ? line[1..] : line;

        private static List<string> SplitLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];

                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            current.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(character);
                    }
                }
                else if (character == '"')
                {
                    inQuotes = true;
                }
                else if (character == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
