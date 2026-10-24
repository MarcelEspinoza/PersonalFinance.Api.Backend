using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/expense-planning")]
    public sealed class ExpensePlanningController : ControllerBase
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        // Frecuencias que se gestionan desde la planificación.
        private static readonly RecurrenceFrequency[] PlanningFrequencies =
        {
            RecurrenceFrequency.Monthly,
            RecurrenceFrequency.Bimonthly
        };

        public ExpensePlanningController(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        [HttpGet]
        public async Task<ActionResult<ExpensePlanningDto>> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            await EnsureKnownPlanningDefaultsAsync(userId.Value, ct);

            var groups = await _db.ConceptGroups
                .AsNoTracking()
                .Where(group =>
                    group.UserId == userId.Value &&
                    group.IsActive &&
                    (group.Kind == ConceptKind.Income || group.Kind == ConceptKind.Expense))
                .OrderBy(group => group.SortOrder)
                .ToListAsync(ct);
            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(concept =>
                    concept.UserId == userId.Value &&
                    concept.IsActive &&
                    (concept.Kind == ConceptKind.Income || concept.Kind == ConceptKind.Expense))
                .OrderBy(concept => concept.SortOrder)
                .ToListAsync(ct);
            var rules = await _db.RecurringRules
                .AsNoTracking()
                .Where(rule =>
                    rule.UserId == userId.Value &&
                    rule.IsActive &&
                    PlanningFrequencies.Contains(rule.Frequency))
                .OrderBy(rule => rule.CreatedAt)
                .ToListAsync(ct);
            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(account => account.UserId == userId.Value && account.IsActive)
                .OrderBy(account => account.Name)
                .ToListAsync(ct);
            var monthStart = new DateOnly(_clock.Today.Year, _clock.Today.Month, 1);

            var rulesByConcept = rules
                .GroupBy(rule => rule.ConceptId)
                .ToDictionary(group => group.Key, group => group.First());
            var conceptsByGroup = concepts.ToLookup(concept => concept.GroupId);

            return Ok(new ExpensePlanningDto
            {
                Groups = groups.Select(group => new ExpensePlanningGroupDto
                {
                    Name = group.Name,
                    Kind = group.Kind,
                    SortOrder = group.SortOrder,
                    Items = conceptsByGroup[group.Id].Select(concept =>
                    {
                        rulesByConcept.TryGetValue(concept.Id, out var rule);
                        return new ExpensePlanningItemDto
                        {
                            ConceptId = concept.Id,
                            Name = concept.Name,
                            Nature = concept.Nature,
                            MonthlyAmount = rule?.ForecastAmount,
                            MonthlyBudget = concept.DefaultMonthlyBudget,
                            DayOfMonth = rule?.DayOfMonth,
                            AccountId = concept.Nature == ConceptNature.Variable
                                ? concept.AccountId
                                : rule?.AccountId,
                            Frequency = concept.Nature == ConceptNature.Fixed ? rule?.Frequency : null,
                            NextDueMonth = concept.Nature == ConceptNature.Fixed
                                ? NextDueMonth(rule, monthStart)
                                : null
                        };
                    }).ToList()
                }).Where(group => group.Items.Count > 0).ToList(),
                Accounts = accounts.Select(account => new ExpensePlanningAccountDto
                {
                    Id = account.Id,
                    Name = account.Name
                }).ToList()
            });
        }

        private const int PlanMonthCount = 6;

        /// <summary>Fixed and variable expenses of 6 months (from the given month, or the current one).</summary>
        [HttpGet("months")]
        public async Task<ActionResult<ExpensePlanMonthsDto>> GetMonths(
            [FromQuery] int? year,
            [FromQuery] int? month,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var currentMonth = new DateOnly(_clock.Today.Year, _clock.Today.Month, 1);
            var start = year is >= 2000 and <= 2100 && month is >= 1 and <= 12
                ? new DateOnly(year.Value, month.Value, 1)
                : currentMonth;
            var months = Enumerable.Range(0, PlanMonthCount).Select(start.AddMonths).ToList();
            var last = months[^1];
            var windowEnd = new DateOnly(last.Year, last.Month, DateTime.DaysInMonth(last.Year, last.Month));
            var startKey = start.Year * 12 + start.Month;
            var endKey = last.Year * 12 + last.Month;

            var entries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId.Value && entry.DueDate >= start && entry.DueDate <= windowEnd)
                .ToListAsync(ct);
            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(concept => concept.UserId == userId.Value)
                .ToListAsync(ct);
            var groups = await _db.ConceptGroups
                .AsNoTracking()
                .Where(group => group.UserId == userId.Value)
                .ToListAsync(ct);
            var budgets = await _db.MonthlyBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.UserId == userId.Value &&
                    budget.Year * 12 + budget.Month >= startKey &&
                    budget.Year * 12 + budget.Month <= endKey)
                .ToListAsync(ct);
            var rules = await _db.RecurringRules
                .AsNoTracking()
                .Where(rule => rule.UserId == userId.Value && rule.IsActive)
                .ToListAsync(ct);
            var closedMonths = (await _db.MonthlyPeriods
                    .AsNoTracking()
                    .Where(period =>
                        period.UserId == userId.Value &&
                        period.Status == PeriodStatus.Closed &&
                        period.Year * 12 + period.Month >= startKey &&
                        period.Year * 12 + period.Month <= endKey)
                    .Select(period => new { period.Year, period.Month })
                    .ToListAsync(ct))
                .Select(period => new DateOnly(period.Year, period.Month, 1))
                .ToHashSet();

            return Ok(PersonalFinance.Api.Features.Ledger.Common.ExpensePlanCalculator.Build(
                months, currentMonth, entries, rules, concepts, groups, budgets, closedMonths));
        }

        /// <summary>Usual monthly limit of a variable expense. Null removes it.</summary>
        [HttpPut("{conceptId:guid}/budget")]
        public async Task<IActionResult> UpdateBudget(
            Guid conceptId,
            [FromBody] UpdateExpenseBudgetDto dto,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (dto.Limit is < 0m) return BadRequest("El límite mensual no puede ser negativo.");

            var concept = await FindVariableExpenseAsync(userId.Value, conceptId, ct);
            if (concept is null) return NotFound("Ese gasto variable ya no existe.");

            concept.DefaultMonthlyBudget = dto.Limit is null ? null : Math.Round(dto.Limit.Value, 2);
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>Limit of a variable expense for one month only. Null goes back to the usual limit.</summary>
        [HttpPut("{conceptId:guid}/budget/{year:int}/{month:int}")]
        public async Task<IActionResult> UpdateMonthBudget(
            Guid conceptId,
            int year,
            int month,
            [FromBody] UpdateExpenseBudgetDto dto,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (year is < 2000 or > 2100 || month is < 1 or > 12) return BadRequest("Mes no válido.");
            if (dto.Limit is < 0m) return BadRequest("El límite mensual no puede ser negativo.");

            var concept = await FindVariableExpenseAsync(userId.Value, conceptId, ct);
            if (concept is null) return NotFound("Ese gasto variable ya no existe.");

            var closed = await _db.MonthlyPeriods.AnyAsync(period =>
                period.UserId == userId.Value &&
                period.Year == year &&
                period.Month == month &&
                period.Status == PeriodStatus.Closed, ct);
            if (closed) return BadRequest("Ese mes está cerrado: ya no se puede cambiar su presupuesto.");

            var budget = await _db.MonthlyBudgets.FirstOrDefaultAsync(item =>
                item.UserId == userId.Value &&
                item.ConceptId == conceptId &&
                item.Year == year &&
                item.Month == month, ct);
            if (dto.Limit is null)
            {
                if (budget is not null) _db.MonthlyBudgets.Remove(budget);
            }
            else
            {
                if (budget is null)
                {
                    budget = new MonthlyBudget
                    {
                        UserId = userId.Value,
                        ConceptId = conceptId,
                        Year = year,
                        Month = month
                    };
                    _db.MonthlyBudgets.Add(budget);
                }

                budget.LimitAmount = Math.Round(dto.Limit.Value, 2);
            }

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        private Task<Concept?> FindVariableExpenseAsync(Guid userId, Guid conceptId, CancellationToken ct) =>
            _db.Concepts.FirstOrDefaultAsync(concept =>
                concept.Id == conceptId &&
                concept.UserId == userId &&
                concept.IsActive &&
                concept.Kind == ConceptKind.Expense &&
                concept.Nature == ConceptNature.Variable, ct);

        private async Task EnsureKnownPlanningDefaultsAsync(Guid userId, CancellationToken ct)
        {
            var accounts = await _db.Accounts
                .Where(account => account.UserId == userId && account.IsActive)
                .ToListAsync(ct);

            var jointAccount = accounts.FirstOrDefault(account =>
                string.Equals(account.Name, "Conjunta", StringComparison.OrdinalIgnoreCase));
            var personalAccount = accounts.FirstOrDefault(account =>
                string.Equals(account.Name, "Revolut 6931", StringComparison.OrdinalIgnoreCase));

            if (jointAccount is null || personalAccount is null)
            {
                var importedAccounts = await _db.ImportBatches
                    .AsNoTracking()
                    .Where(batch => batch.UserId == userId && batch.AccountId != null)
                    .Select(batch => new { batch.AccountId, batch.FileName })
                    .ToListAsync(ct);

                if (jointAccount is null)
                {
                    var jointAccountId = importedAccounts.FirstOrDefault(batch =>
                        batch.FileName != null &&
                        (batch.FileName.Contains("fad8bc") ||
                         batch.FileName.Contains("2026-03-01_2026-09-25")))?.AccountId;
                    jointAccount = accounts.FirstOrDefault(account => account.Id == jointAccountId);
                    if (jointAccount is not null) jointAccount.Name = "Conjunta";
                }

                if (personalAccount is null)
                {
                    var personalAccountId = importedAccounts.FirstOrDefault(batch =>
                        batch.FileName != null && batch.FileName.Contains("547cf9"))?.AccountId;
                    personalAccount = accounts.FirstOrDefault(account => account.Id == personalAccountId);
                    if (personalAccount is not null) personalAccount.Name = "Revolut 6931";
                }
            }

            var defaults = new[]
            {
                new PlanningDefault("Nómina", personalAccount, EntryDirection.In, 2360m, 1),
                new PlanningDefault("Alquiler", jointAccount, EntryDirection.Out, 737.97m, 7),
                new PlanningDefault("Agua", jointAccount, EntryDirection.Out, 74m, 7),
                new PlanningDefault("Electricidad", jointAccount, EntryDirection.Out, 92m, 19),
                new PlanningDefault("Gas", jointAccount, EntryDirection.Out, 85m, 4),
                new PlanningDefault("Internet y telefonía", jointAccount, EntryDirection.Out, 70.60m, 3),
                new PlanningDefault("Seguridad del hogar", jointAccount, EntryDirection.Out, 60.11m, 7),
                new PlanningDefault("Seguro dental", jointAccount, EntryDirection.Out, 21.58m, 1),
                new PlanningDefault("Seguro médico", jointAccount, EntryDirection.Out, 180.39m, 1),
                new PlanningDefault("Transporte diario", personalAccount, EntryDirection.Out, 22.80m, 5),
                new PlanningDefault("Netflix", personalAccount, EntryDirection.Out, 8.99m, 3),
                new PlanningDefault("Amazon Prime", personalAccount, EntryDirection.Out, 4.99m, 7),
                new PlanningDefault("Apple", personalAccount, EntryDirection.Out, 9.99m, 18)
            };

            var availableDefaults = defaults.Where(item => item.Account is not null).ToList();
            var conceptNames = availableDefaults.Select(item => item.ConceptName).ToHashSet();
            var concepts = await _db.Concepts
                .Where(concept =>
                    concept.UserId == userId &&
                    concept.IsActive &&
                    concept.Nature == ConceptNature.Fixed &&
                    conceptNames.Contains(concept.Name))
                .ToListAsync(ct);
            var conceptIds = concepts.Select(concept => concept.Id).ToHashSet();
            var rules = await _db.RecurringRules
                .Where(rule =>
                    rule.UserId == userId &&
                    rule.IsActive &&
                    conceptIds.Contains(rule.ConceptId))
                .OrderBy(rule => rule.CreatedAt)
                .ToListAsync(ct);

            foreach (var defaultValue in availableDefaults)
            {
                var concept = concepts.FirstOrDefault(item => item.Name == defaultValue.ConceptName);
                if (concept is null) continue;

                var rule = rules.FirstOrDefault(item => item.ConceptId == concept.Id);
                if (rule is not null && rule.ForecastAmount > 0m && rule.AccountId is not null) continue;

                if (rule is null)
                {
                    rule = new RecurringRule
                    {
                        UserId = userId,
                        ConceptId = concept.Id,
                        StartDate = new DateOnly(2026, 10, 1)
                    };
                    _db.RecurringRules.Add(rule);
                }

                rule.AccountId = defaultValue.Account!.Id;
                rule.Description = concept.Name;
                rule.Direction = defaultValue.Direction;
                rule.Frequency = RecurrenceFrequency.Monthly;
                rule.DayOfMonth = defaultValue.DayOfMonth;
                rule.ForecastAmount = defaultValue.Amount;
                rule.EndDate = null;
                rule.IsActive = true;
            }

            await _db.SaveChangesAsync(ct);
        }

        private sealed record PlanningDefault(
            string ConceptName,
            Account? Account,
            EntryDirection Direction,
            decimal Amount,
            int DayOfMonth);

        [HttpPut("{conceptId:guid}")]
        public async Task<ActionResult<ExpensePlanningItemDto>> Update(
            Guid conceptId,
            [FromBody] UpdateExpensePlanningDto dto,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var concept = await _db.Concepts.FirstOrDefaultAsync(
                item =>
                    item.Id == conceptId &&
                    item.UserId == userId.Value &&
                    item.IsActive &&
                    (item.Kind == ConceptKind.Income || item.Kind == ConceptKind.Expense),
                ct);
            if (concept is null) return NotFound();

            if (dto.AccountId.HasValue)
            {
                var accountExists = await _db.Accounts.AnyAsync(
                    account =>
                        account.Id == dto.AccountId.Value &&
                        account.UserId == userId.Value &&
                        account.IsActive,
                    ct);
                if (!accountExists) return BadRequest("La cuenta seleccionada no existe.");
            }

            var rules = await _db.RecurringRules
                .Where(rule =>
                    rule.UserId == userId.Value &&
                    rule.ConceptId == conceptId &&
                    PlanningFrequencies.Contains(rule.Frequency))
                .OrderBy(rule => rule.CreatedAt)
                .ToListAsync(ct);
            var monthStart = new DateOnly(_clock.Today.Year, _clock.Today.Month, 1);

            concept.Nature = dto.Nature;

            // Un fijo sin importe, día ni cuenta no lleva regla: sus cuotas se apuntan a mano
            // (por ejemplo, un pasanaco que termina en una fecha concreta).
            var fixedWithoutRule = dto.Nature == ConceptNature.Fixed &&
                dto.MonthlyAmount is null &&
                dto.DayOfMonth is null &&
                dto.AccountId is null;

            if (dto.Nature == ConceptNature.Fixed && !fixedWithoutRule)
            {
                if (dto.MonthlyAmount is null or <= 0m)
                    return BadRequest("Indica un importe mensual mayor que cero.");
                if (dto.DayOfMonth is null or < 1 or > 31)
                    return BadRequest("El día de cobro debe estar entre 1 y 31.");
                if (dto.AccountId is null)
                    return BadRequest("Selecciona la cuenta del movimiento fijo.");

                var previousFrequency = rules.FirstOrDefault()?.Frequency;
                var frequency = dto.Frequency ?? previousFrequency ?? RecurrenceFrequency.Monthly;
                if (!PlanningFrequencies.Contains(frequency))
                    return BadRequest("La frecuencia debe ser cada mes o cada 2 meses.");
                DateOnly? firstDueMonth = dto.FirstDueMonth is { } requested
                    ? new DateOnly(requested.Year, requested.Month, 1)
                    : null;
                if (firstDueMonth < monthStart)
                    return BadRequest("El próximo recibo no puede ser de un mes que ya pasó.");

                concept.DefaultMonthlyBudget = null;
                concept.AccountId = null;
                var rule = rules.FirstOrDefault() ?? new RecurringRule
                {
                    UserId = userId.Value,
                    ConceptId = concept.Id,
                    Direction = concept.Kind == ConceptKind.Income
                        ? EntryDirection.In
                        : EntryDirection.Out,
                    Frequency = RecurrenceFrequency.Monthly,
                    StartDate = monthStart
                };
                if (rules.Count == 0) _db.RecurringRules.Add(rule);

                rule.AccountId = dto.AccountId;
                rule.Direction = concept.Kind == ConceptKind.Income
                    ? EntryDirection.In
                    : EntryDirection.Out;
                rule.Description = concept.Name;
                rule.DayOfMonth = dto.DayOfMonth.Value;
                rule.ForecastAmount = dto.MonthlyAmount.Value;
                rule.IsActive = true;
                rule.EndDate = null;
                rule.Frequency = frequency;
                if (frequency == RecurrenceFrequency.Bimonthly)
                {
                    // El mes de inicio marca en qué meses toca (uno sí, otro no).
                    if (firstDueMonth is not null)
                        rule.StartDate = firstDueMonth.Value;
                    else if (previousFrequency != RecurrenceFrequency.Bimonthly)
                        rule.StartDate = monthStart;
                }
                else if (previousFrequency == RecurrenceFrequency.Bimonthly && rule.StartDate > monthStart)
                {
                    rule.StartDate = monthStart;
                }

                foreach (var duplicate in rules.Skip(1))
                    duplicate.IsActive = false;

                var duplicateRuleIds = rules.Skip(1).Select(item => item.Id).ToHashSet();
                var duplicateEntries = await _db.LedgerEntries
                    .Where(entry =>
                        entry.UserId == userId.Value &&
                        entry.RecurringRuleId != null &&
                        duplicateRuleIds.Contains(entry.RecurringRuleId.Value) &&
                        entry.DueDate >= monthStart &&
                        entry.Status != EntryStatus.Paid)
                    .ToListAsync(ct);
                _db.LedgerEntries.RemoveRange(duplicateEntries);

                var futureEntries = await _db.LedgerEntries
                    .Where(entry =>
                        entry.UserId == userId.Value &&
                        entry.RecurringRuleId == rule.Id &&
                        entry.DueDate >= monthStart &&
                        entry.Status != EntryStatus.Paid)
                    .ToListAsync(ct);
                foreach (var entry in futureEntries)
                {
                    if (rule.Frequency != RecurrenceFrequency.Monthly &&
                        !RecurrenceCalculator.OccursIn(rule, entry.DueDate.Year, entry.DueDate.Month))
                    {
                        _db.LedgerEntries.Remove(entry);
                        continue;
                    }

                    entry.AccountId = dto.AccountId;
                    entry.ForecastAmount = dto.MonthlyAmount.Value;
                    entry.Description = concept.Name;
                    entry.DueDate = RecurrenceCalculator.ResolveDueDate(
                        dto.DayOfMonth.Value, entry.DueDate.Year, entry.DueDate.Month);
                    entry.UpdatedAt = _clock.UtcNow;
                }
            }
            else
            {
                if (dto.MonthlyBudget is < 0m)
                    return BadRequest("El límite mensual no puede ser negativo.");

                concept.DefaultMonthlyBudget = fixedWithoutRule ? null : dto.MonthlyBudget;
                concept.AccountId = dto.AccountId;
                var ruleIds = rules.Select(rule => rule.Id).ToHashSet();
                foreach (var rule in rules) rule.IsActive = false;

                var generatedEntries = await _db.LedgerEntries
                    .Where(entry =>
                        entry.UserId == userId.Value &&
                        entry.RecurringRuleId != null &&
                        ruleIds.Contains(entry.RecurringRuleId.Value) &&
                        entry.DueDate >= monthStart &&
                        entry.Status != EntryStatus.Paid)
                    .ToListAsync(ct);
                _db.LedgerEntries.RemoveRange(generatedEntries);
            }

            await _db.SaveChangesAsync(ct);

            var activeRule = dto.Nature == ConceptNature.Fixed && !fixedWithoutRule
                ? rules.FirstOrDefault() ?? await _db.RecurringRules
                    .AsNoTracking()
                    .Where(rule => rule.UserId == userId.Value && rule.ConceptId == conceptId && rule.IsActive)
                    .OrderBy(rule => rule.CreatedAt)
                    .FirstOrDefaultAsync(ct)
                : null;

            return Ok(new ExpensePlanningItemDto
            {
                ConceptId = concept.Id,
                Name = concept.Name,
                Nature = concept.Nature,
                MonthlyAmount = activeRule?.ForecastAmount ?? dto.MonthlyAmount,
                MonthlyBudget = concept.DefaultMonthlyBudget,
                DayOfMonth = activeRule?.DayOfMonth ?? dto.DayOfMonth,
                AccountId = activeRule?.AccountId ?? dto.AccountId,
                Frequency = activeRule?.Frequency,
                NextDueMonth = NextDueMonth(activeRule, monthStart)
            });
        }

        private static DateOnly? NextDueMonth(RecurringRule? rule, DateOnly monthStart)
        {
            if (rule is null) return null;

            for (var offset = 0; offset < 24; offset++)
            {
                var month = monthStart.AddMonths(offset);
                if (RecurrenceCalculator.OccursIn(rule, month.Year, month.Month)) return month;
            }

            return null;
        }
    }
}
