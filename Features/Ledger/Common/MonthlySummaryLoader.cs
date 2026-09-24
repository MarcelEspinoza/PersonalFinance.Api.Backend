using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Carga de un tirÃ³n todo lo que necesita el cuadro mensual, para que los
    /// casos de uso no repitan la misma consulta.
    /// </summary>
    public class MonthlySummaryLoader
    {
        private readonly IAppDbContext _db;

        public MonthlySummaryLoader(IAppDbContext db) => _db = db;

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

            var budgets = await _db.MonthlyBudgets
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.Year == period.Year && b.Month == period.Month)
                .ToDictionaryAsync(b => b.ConceptId, b => b.LimitAmount, ct);

            return MonthlySummaryBuilder.Build(period, chart, entries, budgets);
        }
    }
}
