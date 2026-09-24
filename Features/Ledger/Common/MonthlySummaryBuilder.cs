using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Arma el cuadro mensual a partir de las entidades ya cargadas.
    /// La aritmÃ©tica vive en el dominio; aquÃ­ sÃ³lo se agrupa y se proyecta.
    /// </summary>
    public static class MonthlySummaryBuilder
    {
        /// <summary>
        /// El cuadro se arma sobre el plan de cuentas entero, no sobre los
        /// apuntes: igual que en la hoja del Excel, un concepto sin movimiento
        /// sigue apareciendo con sus ceros. Si sólo se listasen los conceptos
        /// con apuntes, un mes recién abierto saldría en blanco y no habría
        /// dónde anotar el primero.
        /// </summary>
        public static MonthlySummaryDto Build(
            MonthlyPeriod period,
            IReadOnlyCollection<ConceptGroup> chart,
            IReadOnlyCollection<LedgerEntry> entries,
            IReadOnlyDictionary<Guid, decimal> budgets)
        {
            var totals = LedgerTotalsCalculator.Compute(period.CarryOverAmount, entries);

            // Los descartados se listan igual: si no se devolviesen, no habría
            // forma de recuperarlos. Quedan fuera de las sumas, no de la vista.
            var entriesByConcept = entries
                .GroupBy(e => e.ConceptId)
                .ToDictionary(g => g.Key, g => (IReadOnlyCollection<LedgerEntry>)g.ToList());

            var groups = BuildLayout(chart, entries)
                .Select(layout => BuildGroup(layout.Group, layout.Concepts, entriesByConcept, budgets))
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Name)
                .ToList();

            return new MonthlySummaryDto
            {
                PeriodId = period.Id,
                Year = period.Year,
                Month = period.Month,
                Status = period.Status,
                CarryOverAmount = period.CarryOverAmount,
                IncomeGroups = groups.Where(g => g.Kind == ConceptKind.Income).ToList(),
                ExpenseGroups = groups.Where(g => g.Kind == ConceptKind.Expense).ToList(),
                Totals = new MonthlyTotalsDto
                {
                    IncomeForecast = totals.IncomeForecast,
                    IncomeActual = totals.IncomeActual,
                    ExpenseForecast = totals.ExpenseForecast,
                    ExpenseActual = totals.ExpenseActual,
                    ProjectedBalance = totals.ProjectedBalance,
                    ActualBalance = totals.ActualBalance,
                    PendingExpense = totals.PendingExpense,
                    PendingIncome = totals.PendingIncome
                }
            };
        }

        /// <summary>
        /// Filas que debe enseñar el cuadro: las del plan de cuentas más las de
        /// cualquier concepto que aún tenga apuntes este mes aunque ya esté
        /// desactivado. Si no se incluyesen esos, sus importes seguirían
        /// sumando en los totales pero no habría fila que los explicase.
        /// </summary>
        private static List<(ConceptGroup Group, List<Concept> Concepts)> BuildLayout(
            IReadOnlyCollection<ConceptGroup> chart,
            IReadOnlyCollection<LedgerEntry> entries)
        {
            var layout = chart.ToDictionary(
                g => g.Id,
                g => (Group: g, Concepts: g.Concepts.ToList()));

            foreach (var concept in entries
                .Where(e => e.Concept?.Group is not null)
                .Select(e => e.Concept!)
                .DistinctBy(c => c.Id))
            {
                if (!layout.TryGetValue(concept.GroupId, out var entry))
                {
                    entry = (concept.Group!, new List<Concept>());
                    layout[concept.GroupId] = entry;
                }

                if (!entry.Concepts.Any(c => c.Id == concept.Id)) entry.Concepts.Add(concept);
            }

            return layout.Values.ToList();
        }

        private static MonthlyGroupDto BuildGroup(
            ConceptGroup group,
            List<Concept> groupConcepts,
            IReadOnlyDictionary<Guid, IReadOnlyCollection<LedgerEntry>> entriesByConcept,
            IReadOnlyDictionary<Guid, decimal> budgets)
        {
            var concepts = groupConcepts
                .Select(concept =>
                {
                    var conceptEntries = entriesByConcept.TryGetValue(concept.Id, out var found)
                        ? found
                        : Array.Empty<LedgerEntry>();

                    var counted = LedgerTotalsCalculator.Live(conceptEntries).ToList();

                    var forecast = counted.Sum(e => e.ForecastAmount);
                    var actual = counted.Sum(e => e.ActualAmount ?? 0m);
                    var hasBudget = budgets.TryGetValue(concept.Id, out var limit);

                    return new MonthlyConceptDto
                    {
                        ConceptId = concept.Id,
                        Name = concept.Name,
                        Nature = concept.Nature,
                        SortOrder = concept.SortOrder,
                        ForecastTotal = forecast,
                        ActualTotal = actual,
                        RemainingTotal = forecast - actual,
                        BudgetLimit = hasBudget ? limit : null,
                        IsOverBudget = hasBudget && actual > limit,
                        Entries = conceptEntries.OrderBy(e => e.DueDate).Select(MapEntry).ToList()
                    };
                })
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToList();

            return new MonthlyGroupDto
            {
                GroupId = group.Id,
                Name = group.Name,
                Kind = group.Kind,
                SortOrder = group.SortOrder,
                ForecastTotal = concepts.Sum(c => c.ForecastTotal),
                ActualTotal = concepts.Sum(c => c.ActualTotal),
                RemainingTotal = concepts.Sum(c => c.RemainingTotal),
                Concepts = concepts
            };
        }

        public static MonthlyEntryDto MapEntry(LedgerEntry e) => new()
        {
            Id = e.Id,
            Direction = e.Direction,
            Status = e.Status,
            DueDate = e.DueDate,
            ValueDate = e.ValueDate,
            ForecastAmount = e.ForecastAmount,
            ActualAmount = e.ActualAmount,
            Remaining = e.ForecastAmount - (e.ActualAmount ?? 0m),
            Description = e.Description,
            AccountId = e.AccountId,
            AccountName = e.Account?.Name,
            FromRecurringRule = e.RecurringRuleId.HasValue
        };
    }
}
