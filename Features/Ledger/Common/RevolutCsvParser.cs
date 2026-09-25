using System.Globalization;
using System.Text;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>Una línea del extracto, ya tipada pero sin interpretar.</summary>
    public sealed record RevolutMovement(
        int RowNumber,
        string Type,
        string Product,
        DateTime StartedAt,
        DateTime? CompletedAt,
        string Description,
        decimal Amount,
        decimal Fee,
        string Currency,
        string State,
        decimal? Balance);

    /// <summary>
    /// Resultado de leer el fichero. Las líneas defectuosas no abortan la
    /// importación: se apartan con su motivo para poder enseñárselas al usuario.
    /// </summary>
    public sealed record RevolutParseResult(
        IReadOnlyList<RevolutMovement> Movements,
        IReadOnlyList<string> Problems);

    /// <summary>
    /// Lee el CSV que exporta Revolut.
    ///
    /// Cuidado: Revolut no exporta siempre igual. Verificado contra dos
    /// extractos reales de la misma cuenta y el mismo idioma, uno con punto y
    /// coma y fecha <c>dd/MM/yyyy H:mm</c>, y otro con coma y fecha
    /// <c>yyyy-MM-dd HH:mm:ss</c>. Por eso el separador se deduce de la
    /// cabecera y se aceptan ambos formatos de fecha.
    ///
    /// Las columnas se localizan <b>por nombre de cabecera</b>, nunca por
    /// posición: si Revolut reordena o añade columnas en una exportación
    /// futura, seguimos leyendo bien en lugar de meter importes en el campo
    /// equivocado sin que nadie se entere.
    /// </summary>
    public static class RevolutCsvParser
    {
        /// <summary>
        /// Candidatos en orden de preferencia. Los importes llevan punto
        /// decimal en ambos formatos, así que la coma nunca es ambigua.
        /// </summary>
        private static readonly char[] Delimiters = { ';', ',' };

        private static readonly string[] DateFormats =
        {
            "dd/MM/yyyy H:mm",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy H:mm:ss",
            "dd/MM/yyyy HH:mm:ss",
            "yyyy-MM-dd H:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd H:mm",
            "yyyy-MM-dd HH:mm"
        };

        // Nombres tal y como vienen en la exportación española.
        private const string ColType = "Tipo";
        private const string ColProduct = "Producto";
        private const string ColStarted = "Fecha de inicio";
        private const string ColCompleted = "Fecha de finalización";
        private const string ColDescription = "Descripción";
        private const string ColAmount = "Importe";
        private const string ColFee = "Comisión";
        private const string ColCurrency = "Divisa";
        private const string ColState = "State";
        private const string ColBalance = "Saldo";

        private static readonly string[] RequiredColumns =
        {
            ColType, ColProduct, ColStarted, ColDescription, ColAmount, ColCurrency, ColState
        };

        public static RevolutParseResult Parse(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            var movements = new List<RevolutMovement>();
            var problems = new List<string>();

            var headerLine = reader.ReadLine();
            if (headerLine is null)
                return new RevolutParseResult(movements, new[] { "El fichero está vacío." });

            var delimiter = DetectDelimiter(StripBom(headerLine));
            var header = SplitLine(StripBom(headerLine), delimiter);
            var index = BuildColumnIndex(header, out var missing);

            if (missing.Count > 0)
            {
                problems.Add(
                    $"El fichero no tiene las columnas esperadas. Faltan: {string.Join(", ", missing)}. " +
                    $"Cabecera encontrada: {string.Join(" | ", header)}.");

                return new RevolutParseResult(movements, problems);
            }

            var rowNumber = 1;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                rowNumber++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = SplitLine(line, delimiter);

                if (TryReadMovement(rowNumber, fields, index, out var movement, out var reason))
                    movements.Add(movement!);
                else
                    problems.Add($"Línea {rowNumber}: {reason}");
            }

            return new RevolutParseResult(movements, problems);
        }

        private static bool TryReadMovement(
            int rowNumber,
            IReadOnlyList<string> fields,
            IReadOnlyDictionary<string, int> index,
            out RevolutMovement? movement,
            out string? reason)
        {
            movement = null;
            reason = null;

            string? Field(string column)
            {
                var position = index[column];
                return position < fields.Count ? fields[position] : null;
            }

            var startedRaw = Field(ColStarted);
            if (!TryParseDate(startedRaw, out var started))
            {
                reason = $"fecha de inicio ilegible ('{startedRaw}').";
                return false;
            }

            var amountRaw = Field(ColAmount);
            if (!TryParseDecimal(amountRaw, out var amount))
            {
                reason = $"importe ilegible ('{amountRaw}').";
                return false;
            }

            // Saldo y fecha de finalización vienen vacíos en los movimientos
            // devueltos o pendientes: nunca llegaron a liquidarse.
            TryParseDate(Field(ColCompleted), out var completed);

            decimal? balance = TryParseDecimal(Field(ColBalance), out var balanceValue)
                ? balanceValue
                : null;

            TryParseDecimal(Field(ColFee), out var fee);

            movement = new RevolutMovement(
                RowNumber: rowNumber,
                Type: Field(ColType)?.Trim() ?? string.Empty,
                Product: Field(ColProduct)?.Trim() ?? string.Empty,
                StartedAt: started,
                CompletedAt: completed == default ? null : completed,
                Description: Field(ColDescription)?.Trim() ?? string.Empty,
                Amount: amount,
                Fee: fee,
                Currency: Field(ColCurrency)?.Trim() ?? string.Empty,
                State: Field(ColState)?.Trim() ?? string.Empty,
                Balance: balance);

            return true;
        }

        private static Dictionary<string, int> BuildColumnIndex(
            IReadOnlyList<string> header, out List<string> missing)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < header.Count; i++)
            {
                var name = header[i].Trim();
                if (name.Length > 0 && !index.ContainsKey(name)) index[name] = i;
            }

            missing = RequiredColumns.Where(c => !index.ContainsKey(c)).ToList();

            // Las opcionales se indexan igualmente para no tener que comprobar
            // su presencia en cada fila.
            foreach (var optional in new[] { ColCompleted, ColFee, ColBalance })
                if (!index.ContainsKey(optional)) index[optional] = int.MaxValue;

            return index;
        }

        private static bool TryParseDate(string? raw, out DateTime value)
        {
            value = default;

            if (string.IsNullOrWhiteSpace(raw)) return false;

            return DateTime.TryParseExact(
                raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        private static bool TryParseDecimal(string? raw, out decimal value)
        {
            value = 0m;

            if (string.IsNullOrWhiteSpace(raw)) return false;

            return decimal.TryParse(
                raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string StripBom(string line) =>
            line.Length > 0 && line[0] == '\uFEFF' ? line[1..] : line;

        /// <summary>
        /// Elige el separador probando cada candidato y quedándose con el que
        /// deja más columnas obligatorias reconocibles. Contar caracteres no
        /// sirve: una descripción con comas desempataría mal.
        /// </summary>
        private static char DetectDelimiter(string headerLine)
        {
            var best = Delimiters[0];
            var bestScore = -1;

            foreach (var candidate in Delimiters)
            {
                var columns = SplitLine(headerLine, candidate)
                    .Select(c => c.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var score = RequiredColumns.Count(columns.Contains);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Trocea respetando comillas. El extracto que hemos visto no las usa,
        /// pero basta con que un comercio lleve el separador en el nombre para
        /// que un Split corriente desplace todas las columnas siguientes.
        /// </summary>
        private static List<string> SplitLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else inQuotes = false;
                    }
                    else current.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }

            fields.Add(current.ToString());

            return fields;
        }
    }
}
