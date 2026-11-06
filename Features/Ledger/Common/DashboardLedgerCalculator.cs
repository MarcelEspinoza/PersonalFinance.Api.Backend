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
                    .Sum(entry => entry.ActualAmount ?? entry.CashForecastAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.Out && entry.Status == EntryStatus.Paid)
                    .Sum(entry => entry.ActualAmount ?? entry.CashForecastAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.In && entry.Status != EntryStatus.Paid)
                    .Sum(entry => entry.CashForecastAmount),
                counted
                    .Where(entry => entry.Direction == EntryDirection.Out && entry.Status != EntryStatus.Paid)
                    .Sum(entry => entry.CashForecastAmount));
        }

        public static (decimal Income, decimal Expense) ForMonth(
            IEnumerable<LedgerEntry> entries,
            int year,
            int month)
        {
            var totals = BreakdownForMonth(entries, year, month);
            return (totals.ActualIncome + totals.PendingIncome, totals.ActualExpense + totals.PendingExpense);
        }

        public static decimal PassingAdjustmentForMonth(
            IEnumerable<LedgerEntry> entries,
            int year,
            int month)
        {
            return entries
                .Where(entry =>
                    entry.IsTransfer &&
                    entry.Status != EntryStatus.Paid &&
                    entry.Status != EntryStatus.Skipped &&
                    entry.DueDate.Year == year &&
                    entry.DueDate.Month == month)
                .Sum(entry =>
                {
                    var amount = entry.Remaining > 0m ? entry.Remaining : entry.CashForecastAmount;
                    return entry.Direction == EntryDirection.In ? amount : -amount;
                });
        }
    }
}
