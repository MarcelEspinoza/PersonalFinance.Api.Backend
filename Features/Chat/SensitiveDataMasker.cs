using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalFinance.Api.Features.Chat
{
    /// <summary>
    /// Tapa los datos personales y bancarios más sensibles antes de mandar
    /// texto a la IA: correos, IBAN, tarjetas, DNI/NIE, teléfonos y números
    /// de cuenta largos. Los nombres de comercios y personas se mantienen
    /// porque hacen falta para clasificar los movimientos.
    /// </summary>
    public static class SensitiveDataMasker
    {
        private const int HardLimit = 20_000;
        private const string DniLetters = "TRWAGMYFPDXBNJZSQVHLCKE";
        private const RegexOptions Options =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
        private static readonly char[] CardSeparators = { ' ', '-' };

        private static readonly Regex Email = new(
            @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}",
            Options, MatchTimeout);

        // El patrón puede llevarse una palabra que va detrás del IBAN; MaskIban
        // se queda con el prefijo más largo que supera el control mod 97.
        private static readonly Regex IbanCandidate = new(
            @"(?<![A-Z0-9])[A-Z]{2}[0-9]{2}(?: ?[A-Z0-9]{4}){2,7}(?: ?[A-Z0-9]{1,4})?(?![A-Z0-9])",
            Options, MatchTimeout);

        private static readonly Regex CardCandidate = new(
            @"(?<![0-9])[0-9](?:[ \-]?[0-9]){12,18}(?![0-9])",
            Options, MatchTimeout);

        private static readonly Regex PersonalId = new(
            @"(?<![A-Z0-9])(?:[0-9]{8}|[XYZ][0-9]{7})[ \-]?[A-Z](?![A-Z0-9])",
            Options, MatchTimeout);

        private static readonly Regex Phone = new(
            @"(?<![0-9+])(?:(?:\+|00)34[ \-]?)?[6789](?:[ \-]?[0-9]){8}(?![0-9])",
            Options, MatchTimeout);

        private static readonly Regex LongNumber = new(
            @"(?<![0-9])[0-9](?:[ \-]?[0-9]){11,}(?![0-9])",
            Options, MatchTimeout);

        public static string Mask(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            try
            {
                var masked = Email.Replace(text, "[email]");
                masked = IbanCandidate.Replace(masked, MaskIban);
                masked = CardCandidate.Replace(masked, MaskCard);
                masked = PersonalId.Replace(masked, MaskPersonalId);
                masked = Phone.Replace(masked, "[teléfono]");
                return LongNumber.Replace(masked, match => $"[número …{LastDigits(match.Value)}]");
            }
            catch (RegexMatchTimeoutException)
            {
                return MaskAllDigits(text);
            }
        }

        /// <summary>
        /// Prepara un texto para el prompt: quita caracteres de control e
        /// invisibles (los saltos de línea sólo se conservan si se pide), tapa
        /// los datos sensibles y lo recorta a <paramref name="maxLength"/>.
        /// </summary>
        public static string ForPrompt(string? text, int maxLength, bool keepNewLines = false)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0) return string.Empty;

            var source = text.Length > HardLimit ? Cut(text, HardLimit) : text;
            var builder = new StringBuilder(source.Length);
            foreach (var c in source)
            {
                if (c == '\n' && keepNewLines) builder.Append('\n');
                else if (char.IsControl(c)) builder.Append(' ');
                else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format) builder.Append(c);
            }

            var masked = Mask(builder.ToString().Trim());
            return masked.Length <= maxLength
                ? masked
                : Cut(masked, maxLength - 1).TrimEnd() + "…";
        }

        private static string MaskIban(Match match)
        {
            var candidate = match.Value;
            for (var end = candidate.Length; end > 0; end = candidate.LastIndexOf(' ', end - 1))
            {
                var compact = candidate[..end].Replace(" ", string.Empty);
                if (IsValidIban(compact))
                    return $"[IBAN …{compact[^4..]}]" + candidate[end..];
            }

            return candidate;
        }

        private static string MaskCard(Match match)
        {
            var candidate = match.Value;
            for (var end = candidate.Length; end > 0; end = candidate.LastIndexOfAny(CardSeparators, end - 1))
            {
                var digits = OnlyDigits(candidate[..end]);
                if (digits.Length is >= 13 and <= 19 && PassesLuhn(digits))
                    return $"[tarjeta …{digits[^4..]}]" + candidate[end..];
            }

            return candidate;
        }

        private static string MaskPersonalId(Match match)
        {
            var compact = new string(match.Value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var number = compact[..^1];
            number = number[0] switch
            {
                'X' => "0" + number[1..],
                'Y' => "1" + number[1..],
                'Z' => "2" + number[1..],
                _ => number
            };

            return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
                   DniLetters[value % 23] == compact[^1]
                ? "[documento]"
                : match.Value;
        }

        private static bool IsValidIban(string iban)
        {
            if (iban.Length is < 15 or > 34) return false;

            var remainder = 0;
            foreach (var c in iban[4..] + iban[..4])
            {
                var upper = char.ToUpperInvariant(c);
                if (upper is >= '0' and <= '9')
                    remainder = (remainder * 10 + (upper - '0')) % 97;
                else if (upper is >= 'A' and <= 'Z')
                    remainder = (remainder * 100 + (upper - 'A' + 10)) % 97;
                else
                    return false;
            }

            return remainder == 1;
        }

        private static bool PassesLuhn(string digits)
        {
            var sum = 0;
            var doubleIt = false;
            for (var i = digits.Length - 1; i >= 0; i--)
            {
                var digit = digits[i] - '0';
                if (doubleIt)
                {
                    digit *= 2;
                    if (digit > 9) digit -= 9;
                }

                sum += digit;
                doubleIt = !doubleIt;
            }

            return sum % 10 == 0;
        }

        private static string OnlyDigits(string text) => new(text.Where(c => c is >= '0' and <= '9').ToArray());

        private static string LastDigits(string text)
        {
            var digits = OnlyDigits(text);
            return digits.Length <= 4 ? digits : digits[^4..];
        }

        private static string MaskAllDigits(string text) =>
            new(text.Select(c => c is >= '0' and <= '9' ? '#' : c).ToArray());

        private static string Cut(string text, int length)
        {
            if (length <= 0) return string.Empty;
            if (length >= text.Length) return text;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text[..length];
        }
    }
}
