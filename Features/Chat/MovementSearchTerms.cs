using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalFinance.Api.Features.Chat
{
    /// <summary>
    /// Importes y palabras que el usuario menciona en el chat, para buscar
    /// movimientos de cualquier fecha que no entran en la ventana reciente.
    /// </summary>
    public sealed class MovementSearchTerms
    {
        private const int MaxAmounts = 5;
        private const int MaxWords = 6;
        private const int MinWordLength = 4;

        private static readonly Regex AmountPattern = new(
            @"(?<![\d.,])\d{1,7}(?:[.,]\d{1,2})?(?![\d]|[.,]\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(250));

        private static readonly Regex WordPattern = new(
            @"\p{L}+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(250));

        private static readonly HashSet<string> IgnoredWords = new(StringComparer.Ordinal)
        {
            "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto",
            "septiembre", "setiembre", "octubre", "noviembre", "diciembre",
            "movimiento", "movimientos", "gasto", "gastos", "ingreso", "ingresos", "pago", "pagos",
            "cobro", "cobros", "euros", "euro", "importe", "cuenta", "cuentas", "banco", "transferencia",
            "encuentra", "encuentras", "encontrar", "busca", "buscar", "buscame", "dime", "donde",
            "esta", "estan", "este", "esto", "esos", "esas", "eses", "aquel", "otra", "otro", "otros",
            "porque", "para", "pero", "como", "cual", "cuales", "cuando", "cuanto", "cuanta",
            "tengo", "tiene", "tienes", "hice", "hizo", "hay", "puedes", "quiero", "necesito",
            "desde", "hasta", "entre", "sobre", "solo", "todo", "todos", "todas", "nada", "algo",
            "mismo", "misma", "nuevo", "nueva", "mes", "meses", "pasado", "pasada", "anterior",
            "siguiente", "proximo", "ultimo", "ultima", "favor", "gracias", "hola", "mira", "revisa",
            "marca", "marcar", "pagado", "pagada", "pendiente", "anota", "anotar", "quita", "quitar",
            "cambia", "cambiar", "mueve", "mover", "vincula", "vincular", "liquidacion", "dinero"
        };

        public IReadOnlyList<(decimal Min, decimal Max)> Amounts { get; }
        public IReadOnlyList<string> Words { get; }

        private MovementSearchTerms(IReadOnlyList<(decimal, decimal)> amounts, IReadOnlyList<string> words)
        {
            Amounts = amounts;
            Words = words;
        }

        public bool IsEmpty => Amounts.Count == 0 && Words.Count == 0;

        public static MovementSearchTerms From(IEnumerable<string?> userTexts)
        {
            var amounts = new List<(decimal, decimal)>();
            var words = new List<string>();
            foreach (var text in userTexts.Reverse())
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                try
                {
                    AddAmounts(text, amounts);
                    AddWords(text, words);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }
            }

            return new MovementSearchTerms(amounts, words);
        }

        private static void AddAmounts(string text, List<(decimal, decimal)> amounts)
        {
            foreach (Match match in AmountPattern.Matches(text))
            {
                if (amounts.Count >= MaxAmounts) return;
                var value = match.Value.Replace(',', '.');
                if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
                    amount <= 0m)
                {
                    continue;
                }

                var range = value.Contains('.') ? (amount, amount) : (amount, amount + 0.99m);
                if (!amounts.Contains(range)) amounts.Add(range);
            }
        }

        private static void AddWords(string text, List<string> words)
        {
            foreach (Match match in WordPattern.Matches(text))
            {
                if (words.Count >= MaxWords) return;
                if (match.Value.Length < MinWordLength) continue;
                var word = match.Value.ToLowerInvariant();
                if (IgnoredWords.Contains(RemoveAccents(word)) || words.Contains(word)) continue;
                words.Add(word);
            }
        }

        private static string RemoveAccents(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
