using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public enum MovementDisposition
    {
        /// <summary>Se convierte en asiento del libro.</summary>
        Import = 0,

        /// <summary>No entra, y el motivo se le enseña al usuario.</summary>
        Excluded = 1
    }

    public sealed record ClassifiedMovement(
        RevolutMovement Source,
        MovementDisposition Disposition,
        string Reason,
        EntryDirection Direction,
        EntryStatus Status,
        decimal Amount,
        decimal Fee,
        string NormalizedDescription);

    /// <summary>
    /// Decide qué hacer con cada línea del extracto, pero <b>sólo con reglas
    /// estructurales</b>: producto, estado y signo del importe.
    ///
    /// Aquí no se decide si algo es comida, alquiler o un traspaso a la hucha.
    /// Eso sale de <c>ConceptMapping</c>, que es dato editable por el usuario,
    /// no código: así una clasificación equivocada se arregla desde la pantalla
    /// de revisión en lugar de con un despliegue.
    /// </summary>
    public static class RevolutMovementClassifier
    {
        /// <summary>
        /// Cuenta corriente. El resto de productos ("Ahorros") son la otra cara
        /// de traspasos que ya vienen apuntados en la cuenta corriente:
        /// importarlos contaría el mismo dinero dos veces.
        /// </summary>
        private const string CurrentAccountProduct = "Actual";

        private const string StateCompleted = "COMPLETADO";
        private const string StateReturned = "DEVUELTO";
        private const string StatePending = "PENDIENTE";

        public static ClassifiedMovement Classify(RevolutMovement movement)
        {
            ArgumentNullException.ThrowIfNull(movement);

            var normalized = Normalize(movement.Description);
            var direction = movement.Amount >= 0 ? EntryDirection.In : EntryDirection.Out;
            var amount = Math.Abs(movement.Amount);
            var state = movement.State.Trim();

            ClassifiedMovement Excluded(string reason) => new(
                movement, MovementDisposition.Excluded, reason,
                direction, EntryStatus.Skipped, amount, movement.Fee, normalized);

            if (!string.Equals(movement.Product, CurrentAccountProduct, StringComparison.OrdinalIgnoreCase))
                return Excluded($"Producto '{movement.Product}': es el reflejo de un traspaso ya apuntado en la cuenta corriente.");

            if (string.Equals(state, StateReturned, StringComparison.OrdinalIgnoreCase))
                return Excluded("Movimiento devuelto: nunca llegó a liquidarse.");

            // Un movimiento aún sin liquidar entra como previsión, no como
            // hecho consumado: su importe todavía puede cambiar.
            var status = string.Equals(state, StatePending, StringComparison.OrdinalIgnoreCase)
                ? EntryStatus.Pending
                : EntryStatus.Paid;

            var reason = status == EntryStatus.Pending
                ? "Pendiente de liquidar: entra como previsión."
                : "Liquidado.";

            if (!string.Equals(state, StateCompleted, StringComparison.OrdinalIgnoreCase) &&
                status == EntryStatus.Paid)
            {
                reason = $"Estado '{state}' desconocido: se trata como liquidado.";
            }

            return new ClassifiedMovement(
                movement, MovementDisposition.Import, reason,
                direction, status, amount, movement.Fee, normalized);
        }

        private static readonly Regex CardMask = new(@"\*+\w*", RegexOptions.Compiled);
        private static readonly Regex LongDigits = new(@"\b\d{4,}\b", RegexOptions.Compiled);
        private static readonly Regex Dates = new(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Deja la descripción en una forma estable para poder casarla con
        /// <c>ConceptMapping</c>. Quita tildes, fechas y números largos, y muy
        /// en particular la máscara de la tarjeta: "recarga de Apple Pay con
        /// *8693" y "... con *6442" son el mismo movimiento hecho con dos
        /// tarjetas, y deben compartir regla.
        /// </summary>
        public static string Normalize(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;

            var text = description.Trim().ToUpperInvariant();

            text = Dates.Replace(text, " ");
            text = CardMask.Replace(text, " ");
            text = LongDigits.Replace(text, " ");
            text = RemoveDiacritics(text);
            text = Whitespace.Replace(text, " ");

            return text.Trim();
        }

        private static string RemoveDiacritics(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
