using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public static class ImportPendingRelevance
    {
        private static readonly HashSet<string> GenericIdentifiers = new(StringComparer.Ordinal)
        {
            "BIZUM", "ANADIDO", "TRAVES", "TARJETA", "PRESTAMO", "PRESTAMOS", "REEMBOLSO",
            "REEMBOLSOS", "DEVOLUCION", "DEVOLUCIONES", "PENDIENTE", "RECORDATORIO", "DEUDA",
            "CUOTA", "TRANSFERENCIAS", "RECIBIDO", "ENVIADO", "PAGAR", "COBRAR"
        };

        public static bool HasIdentifier(string bankText, string? pendingText)
        {
            var text = $" {SettlementPaymentLinker.Normalize(bankText)} ";
            return Identifiers(pendingText).Any(word => text.Contains($" {word} ", StringComparison.Ordinal));
        }

        public static bool HasMeaningfulText(string text) => Identifiers(text).Any();

        private static IEnumerable<string> Identifiers(string? text) =>
            MovementSearchTerms.From(new[] { text }).Words.Select(SettlementPaymentLinker.Normalize)
                .Where(word => !GenericIdentifiers.Contains(word));

        public static int Score(string? text, decimal amount, MovementSearchTerms? search, IEnumerable<ImportRow> rows)
        {
            var normalized = $" {SettlementPaymentLinker.Normalize(text)} ";
            var words = search?.Words.Count(word => normalized.Contains(
                $" {SettlementPaymentLinker.Normalize(word)} ", StringComparison.Ordinal)) ?? 0;
            return words * 1000
                + (search?.Amounts.Any(range => amount >= range.Min && amount <= range.Max) == true ? 100 : 0)
                + (rows.Any(row => HasIdentifier(row.RawDescription, text)) ? 50 : 0)
                + (rows.Any(row => Math.Abs(row.Amount) == amount) ? 20 : 0);
        }
    }
}
