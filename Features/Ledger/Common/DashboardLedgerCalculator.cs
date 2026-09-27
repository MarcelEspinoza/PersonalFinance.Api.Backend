using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public static class DashboardLedgerCalculator
    {
        public static (decimal Income, decimal Expense) ForMonth(
            IEnumerable<LedgerEntry> entries,
            int year,
            int month)
        {
            var counted = entries.Where(entry =>
                !entry.IsTransfer &&
                entry.Status != EntryStatus.Skipped &&
                entry.DueDate.Year == year &&
                entry.DueDate.Month == month);

            return (
                counted
                    .Where(entry => entry.Direction == EntryDirection.In)
                    .Sum(EffectiveAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.Out)
                    .Sum(EffectiveAmount));
        }

        private static decimal EffectiveAmount(LedgerEntry entry) =>
            entry.Status == EntryStatus.Paid
                ? entry.ActualAmount ?? entry.ForecastAmount
                : entry.ForecastAmount;
    }
}
