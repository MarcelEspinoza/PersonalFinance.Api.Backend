using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Carga de un tirón todo lo que necesita el cuadro mensual, para que los
    /// casos de uso no repitan la misma consulta.
    /// </summary>
    public class MonthlySummaryLoader
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        public MonthlySummaryLoader(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<MonthlySummaryDto> LoadAsync(Guid userId, MonthlyPeriod period, CancellationToken ct)
        {
            var chart = await _db.ConceptGroups
                .AsNoTracking()
                .Where(g => g.UserId == userId && g.IsActive)
                .Select(g => new ConceptGroup
                {
                    Id = g.Id,
                    UserId = g.UserId,
                    Name = g.Name,
                    Kind = g.Kind,
                    SortOrder = g.SortOrder,
                    IsActive = g.IsActive,
                    Concepts = g.Concepts.Where(c => c.IsActive).ToList()
                })
                .ToListAsync(ct);

            var entries = await _db.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Concept)!.ThenInclude(c => c!.Group)
                .Include(e => e.Account)
                .Where(e => e.UserId == userId && e.PeriodId == period.Id)
                .ToListAsync(ct);

            var budgets = await _db.Concepts
                .AsNoTracking()
                .Where(concept =>
                    concept.UserId == userId &&
                    concept.IsActive &&
                    concept.DefaultMonthlyBudget != null)
                .ToDictionaryAsync(
                    concept => concept.Id,
                    concept => concept.DefaultMonthlyBudget!.Value,
                    ct);
            var monthlyBudgets = await _db.MonthlyBudgets
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.Year == period.Year && b.Month == period.Month)
                .ToListAsync(ct);
            foreach (var budget in monthlyBudgets)
                budgets[budget.ConceptId] = budget.LimitAmount;

            var summary = MonthlySummaryBuilder.Build(period, chart, entries, budgets);

            // Same numbers as the dashboard boxes and the expense plan. Past months have no forecast.
            var today = _clock.Today;
            var periodMonth = new DateOnly(period.Year, period.Month, 1);
            if (periodMonth >= new DateOnly(today.Year, today.Month, 1))
            {
                var forecasts = await MonthForecastLoader.LoadAsync(_db, userId, periodMonth, 1, today, ct);
                summary.Forecast = MonthForecastSummaryDto.From(forecasts[0]);
            }

            return summary;
        }
    }
}
