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
        decimal PendingExpense)
    {
        /// <summary>Arrastre + previsto: a dónde llega el mes si todo sale según el plan.</summary>
        public decimal ProjectedBalance => CarryOver + IncomeForecast - ExpenseForecast;

        /// <summary>Arrastre + real: el dinero que hay de verdad a día de hoy.</summary>
        public decimal ActualBalance => CarryOver + IncomeActual - ExpenseActual;
    }

    public static class LedgerTotalsCalculator
    {
        /// <summary>
        /// Un asiento descartado no existe a efectos contables: se excluye de
        /// todos los totales.
        /// </summary>
        public static IEnumerable<LedgerEntry> Live(IEnumerable<LedgerEntry> entries) =>
            entries.Where(e => e.Status != EntryStatus.Skipped);

        public static MonthlyTotals Compute(decimal carryOver, IEnumerable<LedgerEntry> entries)
        {
            var live = Live(entries).ToList();

            var incoming = live.Where(e => e.Direction == EntryDirection.In).ToList();
            var outgoing = live.Where(e => e.Direction == EntryDirection.Out).ToList();

            return new MonthlyTotals(
                CarryOver: carryOver,
                IncomeForecast: incoming.Sum(e => e.ForecastAmount),
                IncomeActual: incoming.Sum(e => e.ActualAmount ?? 0m),
                ExpenseForecast: outgoing.Sum(e => e.ForecastAmount),
                ExpenseActual: outgoing.Sum(e => e.ActualAmount ?? 0m),
                PendingIncome: incoming.Where(e => e.Status != EntryStatus.Paid).Sum(e => e.ForecastAmount),
                PendingExpense: outgoing.Where(e => e.Status != EntryStatus.Paid).Sum(e => e.ForecastAmount));
        }

        /// <summary>
        /// Saldo con el que se cierra el mes. Sólo cuentan los importes reales:
        /// una previsión sin confirmar no es dinero.
        /// </summary>
        public static decimal ComputeClosingBalance(decimal carryOver, IEnumerable<LedgerEntry> entries)
        {
            var paid = Live(entries).Where(e => e.Status == EntryStatus.Paid).ToList();

            var income = paid.Where(e => e.Direction == EntryDirection.In).Sum(e => e.ActualAmount ?? 0m);
            var expense = paid.Where(e => e.Direction == EntryDirection.Out).Sum(e => e.ActualAmount ?? 0m);

            return carryOver + income - expense;
        }
    }
}
