using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Calculations
{
    /// <summary>Totales de un mes, con previsto y real separados.</summary>
    public sealed record MonthlyTotals(
        decimal CarryOver,
        decimal IncomeForecast,
        decimal IncomeActual,
        decimal ExpenseForecast,
        decimal ExpenseActual,
        decimal PendingIncome,
        decimal PendingExpense,
        decimal TransferForecast,
        decimal TransferActual)
    {
        /// <summary>Arrastre + previsto: a dónde llega el mes si todo sale según el plan.</summary>
        public decimal ProjectedBalance => CarryOver + IncomeForecast - ExpenseForecast + TransferForecast;

        /// <summary>Arrastre + real: el dinero que hay de verdad a día de hoy.</summary>
        public decimal ActualBalance => CarryOver + IncomeActual - ExpenseActual + TransferActual;
    }

    public static class LedgerTotalsCalculator
    {
        /// <summary>
        /// Un asiento descartado no existe a efectos contables: se excluye de
        /// todos los totales.
        /// </summary>
        public static IEnumerable<LedgerEntry> Live(IEnumerable<LedgerEntry> entries) =>
            entries.Where(e => e.Status != EntryStatus.Skipped);

        private static decimal Signed(EntryDirection direction, decimal amount) =>
            direction == EntryDirection.In ? amount : -amount;

        public static MonthlyTotals Compute(decimal carryOver, IEnumerable<LedgerEntry> entries)
        {
            var live = Live(entries).ToList();

            // Los traspasos se apartan antes de sumar nada: mover dinero de la
            // cuenta a una hucha no es gastarlo.
            var transfers = live.Where(e => e.IsTransfer).ToList();
            var operating = live.Where(e => !e.IsTransfer).ToList();

            var incoming = operating.Where(e => e.Direction == EntryDirection.In).ToList();
            var outgoing = operating.Where(e => e.Direction == EntryDirection.Out).ToList();

            return new MonthlyTotals(
                CarryOver: carryOver,
                IncomeForecast: incoming.Sum(e => e.ForecastAmount),
                IncomeActual: incoming.Sum(e => e.ActualAmount ?? 0m),
                ExpenseForecast: outgoing.Sum(e => e.ForecastAmount),
                ExpenseActual: outgoing.Sum(e => e.ActualAmount ?? 0m),
                PendingIncome: incoming.Where(e => e.Status != EntryStatus.Paid).Sum(e => e.ForecastAmount),
                PendingExpense: outgoing.Where(e => e.Status != EntryStatus.Paid).Sum(e => e.ForecastAmount),
                TransferForecast: transfers.Sum(e => Signed(e.Direction, e.ForecastAmount)),
                TransferActual: transfers.Sum(e => Signed(e.Direction, e.ActualAmount ?? 0m)));
        }

        /// <summary>
        /// Saldo con el que se cierra el mes. Sólo cuentan los importes reales:
        /// una previsión sin confirmar no es dinero. Los traspasos sí entran
        /// aquí, porque el dinero salió de la cuenta de verdad.
        /// </summary>
        public static decimal ComputeClosingBalance(decimal carryOver, IEnumerable<LedgerEntry> entries)
        {
            var paid = Live(entries).Where(e => e.Status == EntryStatus.Paid).ToList();

            var income = paid.Where(e => !e.IsTransfer && e.Direction == EntryDirection.In)
                             .Sum(e => e.ActualAmount ?? 0m);

            var expense = paid.Where(e => !e.IsTransfer && e.Direction == EntryDirection.Out)
                              .Sum(e => e.ActualAmount ?? 0m);

            var transfers = paid.Where(e => e.IsTransfer)
                                .Sum(e => Signed(e.Direction, e.ActualAmount ?? 0m));

            return carryOver + income - expense + transfers;
        }
    }
}
