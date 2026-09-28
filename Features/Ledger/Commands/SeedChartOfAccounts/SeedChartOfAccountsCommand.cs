using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

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
            await NormalizeLegacyTemplateAsync(request.UserId, ct);

            var existingGroups = await _db.ConceptGroups
                .Where(g => g.UserId == request.UserId)
                .ToDictionaryAsync(g => g.Name, StringComparer.OrdinalIgnoreCase, ct);

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
                        DefaultMonthlyBudget = conceptTemplate.DefaultMonthlyBudget,
                        SortOrder = conceptTemplate.SortOrder
                    };

                    _db.Concepts.Add(concept);
                    existingConcepts[(group.Id, concept.Name)] = concept.Id;
                    result.ConceptsCreated++;

                    if (conceptTemplate.DefaultMonthlyBudget.HasValue)
                        budgetTargets.Add((concept.Id, conceptTemplate.DefaultMonthlyBudget.Value));
                }
            }

            await _db.SaveChangesAsync(ct);
            await MigrateLegacyConceptDataAsync(request.UserId, ct);
            await MigrateMerchantConceptsAsync(request.UserId, ct);

            if (request.BudgetYear.HasValue && request.BudgetMonth.HasValue && budgetTargets.Count > 0)
            {
                result.BudgetsCreated = await SeedBudgetsAsync(
                    request.UserId, request.BudgetYear.Value, request.BudgetMonth.Value, budgetTargets, ct);
            }

            await _db.SaveChangesAsync(ct);

            return result;
        }

        private async Task NormalizeLegacyTemplateAsync(Guid userId, CancellationToken ct)
        {
            var groups = await _db.ConceptGroups
                .Include(group => group.Concepts)
                .Where(group => group.UserId == userId)
                .ToListAsync(ct);

            var legacyGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "INCOMES",
                "Apartment costs",
                "Extra Payments",
                "Extra Fixed Payments",
                "Subscriptions",
                "Variable Costs",
                "Savings"
            };

            foreach (var group in groups.Where(group => legacyGroups.Contains(group.Name)))
            {
                group.IsActive = false;
                foreach (var concept in group.Concepts)
                    concept.IsActive = false;
            }

            await _db.SaveChangesAsync(ct);
        }

        private async Task MigrateLegacyConceptDataAsync(Guid userId, CancellationToken ct)
        {
            var aliases = new[]
            {
                ("My Salary", "Nómina", "Ingresos"),
                ("Income 2 (Mama Jenny)", "Aportaciones familiares", "Ingresos"),
                ("Additional Income 2 (Vane)", "Ingresos adicionales", "Ingresos"),
                ("Loans Done", "Préstamos cobrados", "Ingresos"),
                ("Loans TBP", "Préstamos cobrados", "Ingresos"),
                ("Rent Aparment", "Alquiler", "Hogar"),
                ("Electric Bill", "Electricidad", "Hogar"),
                ("Water Bill", "Agua", "Hogar"),
                ("Gas Bill", "Gas", "Hogar"),
                ("Internet + Mobile phone and Landline", "Internet y telefonía", "Hogar"),
                ("Food & Beverage", "Supermercado y alimentación del hogar", "Alimentación"),
                ("Personal costs", "Compras personales", "Vida personal"),
                ("Transport", "Transporte diario", "Transporte y viajes"),
                ("Bank fees", "Comisiones bancarias", "Finanzas y compromisos"),
                ("Loans TBP (Paid)", "Préstamos entregados", "Finanzas y compromisos"),
                ("Loans TBP (Pending)", "Préstamos entregados", "Finanzas y compromisos"),
                ("Pasanaco", "Pasanaco", "Finanzas y compromisos"),
                ("Savings", "Ahorro e inversión", "Ahorro e inversión"),
                ("Netflix", "Netflix", "Suscripciones y servicios digitales"),
                ("Disney+", "Disney+", "Suscripciones y servicios digitales"),
                ("Spotify", "Spotify", "Suscripciones y servicios digitales")
            };

            var groups = await _db.ConceptGroups
                .Where(group => group.UserId == userId)
                .ToListAsync(ct);
            var concepts = await _db.Concepts
                .Where(concept => concept.UserId == userId)
                .ToListAsync(ct);

            foreach (var (oldName, newName, targetGroupName) in aliases)
            {
                var targetGroup = groups.FirstOrDefault(group =>
                    group.IsActive &&
                    string.Equals(group.Name, targetGroupName, StringComparison.OrdinalIgnoreCase));
                if (targetGroup is null) continue;

                var target = concepts.FirstOrDefault(concept =>
                    concept.GroupId == targetGroup.Id &&
                    concept.IsActive &&
                    string.Equals(concept.Name, newName, StringComparison.OrdinalIgnoreCase));
                if (target is null) continue;

                var sources = concepts
                    .Where(concept =>
                        concept.Id != target.Id &&
                        !concept.IsActive &&
                        string.Equals(concept.Name, oldName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var source in sources)
                {
                    foreach (var entry in await _db.LedgerEntries
                                 .Where(item => item.UserId == userId && item.ConceptId == source.Id)
                                 .ToListAsync(ct))
                        entry.ConceptId = target.Id;

                    foreach (var rule in await _db.RecurringRules
                                 .Where(item => item.UserId == userId && item.ConceptId == source.Id)
                                 .ToListAsync(ct))
                        rule.ConceptId = target.Id;

                    foreach (var debt in await _db.Debts
                                 .Where(item => item.UserId == userId && item.ConceptId == source.Id)
                                 .ToListAsync(ct))
                        debt.ConceptId = target.Id;

                    foreach (var row in await _db.ImportRows
                                 .Where(item => item.UserId == userId &&
                                     (item.SuggestedConceptId == source.Id || item.ConfirmedConceptId == source.Id))
                                 .ToListAsync(ct))
                    {
                        if (row.SuggestedConceptId == source.Id) row.SuggestedConceptId = target.Id;
                        if (row.ConfirmedConceptId == source.Id) row.ConfirmedConceptId = target.Id;
                    }

                    foreach (var mapping in await _db.ConceptMappings
                                 .Where(item => item.UserId == userId && item.ConceptId == source.Id)
                                 .ToListAsync(ct))
                        mapping.ConceptId = target.Id;

                    var sourceBudgets = await _db.MonthlyBudgets
                        .Where(item => item.UserId == userId && item.ConceptId == source.Id)
                        .ToListAsync(ct);
                    foreach (var sourceBudget in sourceBudgets)
                    {
                        var targetBudget = await _db.MonthlyBudgets.FirstOrDefaultAsync(
                            item =>
                                item.UserId == userId &&
                                item.ConceptId == target.Id &&
                                item.Year == sourceBudget.Year &&
                                item.Month == sourceBudget.Month,
                            ct);
                        if (targetBudget is null)
                        {
                            sourceBudget.ConceptId = target.Id;
                        }
                        else
                        {
                            targetBudget.LimitAmount = sourceBudget.LimitAmount;
                            _db.MonthlyBudgets.Remove(sourceBudget);
                        }
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        private async Task MigrateMerchantConceptsAsync(Guid userId, CancellationToken ct)
        {
            var group = await _db.ConceptGroups.FirstOrDefaultAsync(
                item => item.UserId == userId &&
                        item.Name == "Suscripciones y servicios digitales" &&
                        item.IsActive,
                ct);
            if (group is null) return;

            var targets = await _db.Concepts
                .Where(item => item.UserId == userId && item.GroupId == group.Id && item.IsActive)
                .ToDictionaryAsync(item => item.Name, StringComparer.OrdinalIgnoreCase, ct);
            var merchantTargets = new[]
            {
                ("CRUNCHYROLL", "Crunchyroll"),
                ("RAILWAY", "Railway"),
                ("ANTHROPIC", "Anthropic")
            };
            if (merchantTargets.Any(item => !targets.ContainsKey(item.Item2))) return;

            var entries = await _db.LedgerEntries
                .Where(item => item.UserId == userId && item.Description != null)
                .ToListAsync(ct);
            foreach (var entry in entries)
            {
                var normalized = RevolutMovementClassifier.Normalize(entry.Description);
                var target = merchantTargets.FirstOrDefault(item => normalized.Contains(
                    item.Item1,
                    StringComparison.OrdinalIgnoreCase));
                if (target != default)
                    entry.ConceptId = targets[target.Item2].Id;
            }

            var pendingRows = await _db.ImportRows
                .Where(item => item.UserId == userId &&
                               item.Status == ImportRowStatus.Pending &&
                               item.ConfirmedConceptId == null)
                .ToListAsync(ct);
            foreach (var row in pendingRows)
            {
                var normalized = row.NormalizedDescription ??
                                RevolutMovementClassifier.Normalize(row.RawDescription);
                var target = merchantTargets.FirstOrDefault(item => normalized.Contains(
                    item.Item1,
                    StringComparison.OrdinalIgnoreCase));
                if (target == default) continue;

                row.SuggestedConceptId = targets[target.Item2].Id;
                row.SuggestionSource = "mapping";
                row.SuggestionConfidence = 1m;
            }

            await _db.SaveChangesAsync(ct);
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
