using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts
{
    /// <summary>
    /// Siembra el plan de cuentas del Excel. Es idempotente: sólo crea lo que
    /// falta, así que se puede lanzar tantas veces como haga falta.
    /// Si se indica año y mes, además crea los presupuestos por defecto.
    /// </summary>
    public record SeedChartOfAccountsCommand(Guid UserId, int? BudgetYear = null, int? BudgetMonth = null)
        : IRequest<SeedChartOfAccountsResultDto>;

    public class SeedChartOfAccountsCommandHandler
        : IRequestHandler<SeedChartOfAccountsCommand, SeedChartOfAccountsResultDto>
    {
        private readonly IAppDbContext _db;

        public SeedChartOfAccountsCommandHandler(IAppDbContext db) => _db = db;

        public async Task<SeedChartOfAccountsResultDto> Handle(
            SeedChartOfAccountsCommand request, CancellationToken ct)
        {
            var result = new SeedChartOfAccountsResultDto();

            var existingGroups = await _db.ConceptGroups
                .Where(g => g.UserId == request.UserId)
                .ToDictionaryAsync(g => g.Name, ct);

            var existingConcepts = (await _db.Concepts
                    .Where(c => c.UserId == request.UserId)
                    .Select(c => new { c.GroupId, c.Name, c.Id })
                    .ToListAsync(ct))
                .ToDictionary(c => (c.GroupId, c.Name), c => c.Id);

            var budgetTargets = new List<(Guid ConceptId, decimal Limit)>();

            foreach (var groupTemplate in ChartOfAccountsTemplate.Groups)
            {
                if (existingGroups.TryGetValue(groupTemplate.Name, out var group))
                {
                    result.GroupsAlreadyPresent++;
                }
                else
                {
                    group = new ConceptGroup
                    {
                        UserId = request.UserId,
                        Name = groupTemplate.Name,
                        Kind = groupTemplate.Kind,
                        SortOrder = groupTemplate.SortOrder
                    };

                    _db.ConceptGroups.Add(group);
                    existingGroups[group.Name] = group;
                    result.GroupsCreated++;
                }

                foreach (var conceptTemplate in groupTemplate.Concepts)
                {
                    if (existingConcepts.TryGetValue((group.Id, conceptTemplate.Name), out var existingId))
                    {
                        result.ConceptsAlreadyPresent++;

                        if (conceptTemplate.DefaultMonthlyBudget.HasValue)
                            budgetTargets.Add((existingId, conceptTemplate.DefaultMonthlyBudget.Value));

                        continue;
                    }

                    var concept = new Concept
                    {
                        UserId = request.UserId,
                        GroupId = group.Id,
                        Name = conceptTemplate.Name,
                        Kind = groupTemplate.Kind,
                        Nature = conceptTemplate.Nature,
                        SortOrder = conceptTemplate.SortOrder
                    };

                    _db.Concepts.Add(concept);
                    existingConcepts[(group.Id, concept.Name)] = concept.Id;
                    result.ConceptsCreated++;

                    if (conceptTemplate.DefaultMonthlyBudget.HasValue)
                        budgetTargets.Add((concept.Id, conceptTemplate.DefaultMonthlyBudget.Value));
                }
            }

            if (request.BudgetYear.HasValue && request.BudgetMonth.HasValue && budgetTargets.Count > 0)
            {
                result.BudgetsCreated = await SeedBudgetsAsync(
                    request.UserId, request.BudgetYear.Value, request.BudgetMonth.Value, budgetTargets, ct);
            }

            await _db.SaveChangesAsync(ct);

            return result;
        }

        private async Task<int> SeedBudgetsAsync(
            Guid userId, int year, int month, List<(Guid ConceptId, decimal Limit)> targets, CancellationToken ct)
        {
            PeriodProvisioner.ValidateMonth(year, month);

            var alreadyBudgeted = (await _db.MonthlyBudgets
                    .Where(b => b.UserId == userId && b.Year == year && b.Month == month)
                    .Select(b => b.ConceptId)
                    .ToListAsync(ct))
                .ToHashSet();

            var created = 0;

            foreach (var (conceptId, limit) in targets)
            {
                if (!alreadyBudgeted.Add(conceptId)) continue;

                _db.MonthlyBudgets.Add(new MonthlyBudget
                {
                    UserId = userId,
                    ConceptId = conceptId,
                    Year = year,
                    Month = month,
                    LimitAmount = limit
                });

                created++;
            }

            return created;
        }
    }
}
