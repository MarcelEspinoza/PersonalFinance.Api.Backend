using System.Globalization;
using System.Text;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Domain.Settlements
{
    /// <summary>
    /// Compone el texto que se manda por WhatsApp a partir de las líneas de la
    /// liquidación, con el mismo formato que se venía escribiendo a mano:
    /// gastos adelantados, lo que hay que restar, lo ya pagado y el pendiente.
    /// </summary>
    public static class SettlementMessageBuilder
    {
        private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

        public static SettlementTotals Totals(Settlement settlement)
        {
            var lines = settlement.Lines ?? new List<SettlementLine>();
            var charges = lines.Where(l => l.Kind == SettlementLineKind.Charge).Sum(l => l.Amount);
            var deductions = lines.Where(l => l.Kind == SettlementLineKind.Deduction).Sum(l => l.Amount);
            var payments = lines.Where(l => l.Kind == SettlementLineKind.Payment).Sum(l => l.Amount);

            return new SettlementTotals(
                charges,
                deductions,
                payments,
                settlement.CarriedOverAmount,
                charges + settlement.CarriedOverAmount - deductions - payments);
        }

        public static string Build(Settlement settlement, string? counterpartyName = null)
        {
            var totals = Totals(settlement);
            var lines = (settlement.Lines ?? new List<SettlementLine>())
                .OrderBy(line => line.SortOrder)
                .ThenBy(line => line.CreatedAt)
                .ToList();

            var text = new StringBuilder();

            var title = string.IsNullOrWhiteSpace(settlement.Title)
                ? "CUENTAS"
                : settlement.Title.Trim();
            text.Append(title.ToUpperInvariant());
            text.AppendLine();

            AppendSection(text, lines, SettlementLineKind.Charge, "GASTOS", "TOTAL GASTOS");

            if (settlement.CarriedOverAmount != 0m)
            {
                text.AppendLine();
                text.Append("Pendiente anterior: ");
                text.Append(Money(settlement.CarriedOverAmount));
            }

            AppendSection(text, lines, SettlementLineKind.Deduction, "A RESTAR", "TOTAL A RESTAR");
            AppendSection(text, lines, SettlementLineKind.Payment, "YA PAGADO", "TOTAL PAGADO");

            text.AppendLine();
            text.AppendLine();
            text.Append("TOTAL PENDIENTE ");
            text.Append(totals.Pending >= 0m ? "➡️ " : "(a tu favor) ");
            text.Append(Money(Math.Abs(totals.Pending)));

            if (!string.IsNullOrWhiteSpace(settlement.ClosingNote))
            {
                text.AppendLine();
                text.AppendLine();
                text.Append(settlement.ClosingNote.Trim());
            }

            _ = counterpartyName;
            return text.ToString();
        }

        private static void AppendSection(
            StringBuilder text,
            IReadOnlyCollection<SettlementLine> lines,
            SettlementLineKind kind,
            string heading,
            string totalLabel)
        {
            var section = lines.Where(line => line.Kind == kind).ToList();
            if (section.Count == 0) return;

            text.AppendLine();
            text.AppendLine();
            text.AppendLine(heading);
            text.AppendLine();

            foreach (var line in section)
            {
                text.Append("• ");
                text.Append(string.IsNullOrWhiteSpace(line.Description) ? "Sin concepto" : line.Description.Trim());
                text.Append(": ");
                text.Append(Money(line.Amount));
                if (line.IsShared)
                {
                    text.Append(" (");
                    text.Append(Money(line.FullAmount!.Value));
                    text.Append(" total)");
                }
                text.AppendLine();
            }

            text.Append(totalLabel);
            text.Append(": ");
            text.Append(Money(section.Sum(line => line.Amount)));
        }

        private static string Money(decimal amount) =>
            amount.ToString("N2", Spanish) + " €";
    }

    public sealed record SettlementTotals(
        decimal Charges,
        decimal Deductions,
        decimal Payments,
        decimal CarriedOver,
        decimal Pending);
}
