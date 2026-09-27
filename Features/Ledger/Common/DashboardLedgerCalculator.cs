using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record DashboardMonthAmounts(
        decimal ActualIncome,
        decimal ActualExpense,
        decimal PendingIncome,
        decimal PendingExpense);

    public static class DashboardLedgerCalculator
    {
        public static DashboardMonthAmounts BreakdownForMonth(
            IEnumerable<LedgerEntry> entries,
            int year,
            int month)
        {
            var counted = entries.Where(entry =>
                !entry.IsTransfer &&
                entry.Status != EntryStatus.Skipped &&
                entry.DueDate.Year == year &&
                entry.DueDate.Month == month)
                .ToList();

            return new DashboardMonthAmounts(
                counted
                    .Where(entry => entry.Direction == EntryDirection.In && entry.Status == EntryStatus.Paid)
                    .Sum(entry => entry.ActualAmount ?? entry.ForecastAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.Out && entry.Status == EntryStatus.Paid)
                    .Sum(entry => entry.ActualAmount ?? entry.ForecastAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.In && entry.Status != EntryStatus.Paid)
                    .Sum(entry => entry.ForecastAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.Out && entry.Status != EntryStatus.Paid)
                    .Sum(entry => entry.ForecastAmount));
        }

        public static (decimal Income, decimal Expense) ForMonth(
            IEnumerable<LedgerEntry> entries,
            int year,
            int month)
        {
            var totals = BreakdownForMonth(entries, year, month);
            return (totals.ActualIncome + totals.PendingIncome, totals.ActualExpense + totals.PendingExpense);
        }
    }
}
