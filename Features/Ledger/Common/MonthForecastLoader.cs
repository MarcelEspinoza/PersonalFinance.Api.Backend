using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>Loads what <see cref="MonthForecastCalculator"/> needs for a run of months.</summary>
    public static class MonthForecastLoader
    {
        public static async Task<IReadOnlyList<MonthForecast>> LoadAsync(
            IAppDbContext db,
            Guid userId,
            DateOnly firstMonth,
            int monthCount,
            DateOnly today,
            CancellationToken ct = default)
        {
            if (monthCount <= 0) return Array.Empty<MonthForecast>();

            var first = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
            var months = Enumerable.Range(0, monthCount).Select(offset => first.AddMonths(offset)).ToList();
            var last = months[^1];
            var lastDay = last.AddMonths(1).AddDays(-1);

            var entries = await db.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId && entry.DueDate >= first && entry.DueDate <= lastDay)
                .ToListAsync(ct);
            var rules = await db.RecurringRules
                .AsNoTracking()
                .Where(rule => rule.UserId == userId && rule.IsActive)
                .ToListAsync(ct);
            var concepts = await db.Concepts
                .AsNoTracking()
                .Where(concept => concept.UserId == userId)
                .ToListAsync(ct);
            var budgets = await db.MonthlyBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.UserId == userId &&
                    (budget.Year > first.Year || (budget.Year == first.Year && budget.Month >= first.Month)) &&
                    (budget.Year < last.Year || (budget.Year == last.Year && budget.Month <= last.Month)))
                .ToListAsync(ct);
            var reminders = await LoadRemindersAsync(db, userId, ct);
            var settlements = await SettlementForecast.LoadExpectationsAsync(db, userId, ct);

            return MonthForecastCalculator.Build(
                new MonthForecastInput(months, today, entries, rules, concepts, budgets, reminders, settlements));
        }

        /// <summary>Pending reminders with an amount: the only ones the forecast counts.</summary>
        public static Task<List<Reminder>> LoadRemindersAsync(IAppDbContext db, Guid userId, CancellationToken ct = default) =>
            db.Reminders
                .AsNoTracking()
                .Where(reminder => reminder.UserId == userId && !reminder.IsDone && reminder.Amount != null && reminder.Amount > 0m)
                .ToListAsync(ct);
    }
}
