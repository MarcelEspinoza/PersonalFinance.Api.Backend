using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Trabajo compartido por los casos de uso del mes: abrir el periodo,
    /// calcular su arrastre y materializar las reglas recurrentes.
    /// </summary>
    public class PeriodProvisioner
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        public PeriodProvisioner(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public static void ValidateMonth(int year, int month)
        {
            if (month is < 1 or > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "El mes debe estar entre 1 y 12.");

            if (year is < 2000 or > 2100)
                throw new ArgumentOutOfRangeException(nameof(year), "El aÃ±o estÃ¡ fuera del rango admitido.");
        }

        public static (int Year, int Month) PreviousMonth(int year, int month) =>
            month == 1 ? (year - 1, 12) : (year, month - 1);

        public static (int Year, int Month) NextMonth(int year, int month) =>
            month == 12 ? (year + 1, 1) : (year, month + 1);

        /// <summary>
        /// Devuelve el periodo, creÃ¡ndolo si no existÃ­a. Si estÃ¡ abierto,
        /// materializa las recurrentes que falten y marca como pendientes las
        /// previsiones ya vencidas.
        /// </summary>
        public async Task<MonthlyPeriod> GetOrOpenAsync(Guid userId, int year, int month, CancellationToken ct)
        {
            ValidateMonth(year, month);

            var period = await _db.MonthlyPeriods
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Year == year && p.Month == month, ct);

            if (period is null)
            {
                period = new MonthlyPeriod
                {
                    UserId = userId,
                    Year = year,
                    Month = month,
                    Status = PeriodStatus.Open,
                    CarryOverAmount = await ResolveCarryOverAsync(userId, year, month, ct)
                };

                _db.MonthlyPeriods.Add(period);
                await _db.SaveChangesAsync(ct);
            }

            if (period.Status == PeriodStatus.Open)
            {
                await MaterializeRecurringRulesAsync(userId, period, ct);
                await RefreshOverdueStatusesAsync(userId, period, ct);
            }

            return period;
        }

        /// <summary>
        /// El arrastre es el cierre del mes anterior. Si nunca hubo mes
        /// anterior, se parte de los saldos de apertura de las cuentas.
        /// </summary>
        private async Task<decimal> ResolveCarryOverAsync(Guid userId, int year, int month, CancellationToken ct)
        {
            var (prevYear, prevMonth) = PreviousMonth(year, month);

            var previous = await _db.MonthlyPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Year == prevYear && p.Month == prevMonth, ct);

            if (previous?.ClosingBalance is not null) return previous.ClosingBalance.Value;
            if (previous is not null) return 0m;

            var firstDay = new DateOnly(year, month, 1);

            return await _db.Accounts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive && a.OpeningDate < firstDay)
                .SumAsync(a => (decimal?)a.OpeningBalance, ct) ?? 0m;
        }

        /// <summary>
        /// Crea sÃ³lo los asientos recurrentes que aÃºn no existen en el mes, de
        /// forma que repetir la llamada no duplica nada.
        /// </summary>
        private async Task MaterializeRecurringRulesAsync(Guid userId, MonthlyPeriod period, CancellationToken ct)
        {
            var firstDay = new DateOnly(period.Year, period.Month, 1);
            var lastDay = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));

            var rules = await _db.RecurringRules
                .Where(r => r.UserId == userId
                            && r.IsActive
                            && r.StartDate <= lastDay
                            && (r.EndDate == null || r.EndDate >= firstDay))
                .ToListAsync(ct);

            if (rules.Count == 0) return;

            var materialized = (await _db.LedgerEntries
                    .Where(e => e.UserId == userId && e.PeriodId == period.Id && e.RecurringRuleId != null)
                    .Select(e => e.RecurringRuleId!.Value)
                    .ToListAsync(ct))
                .ToHashSet();

            var created = 0;

            foreach (var rule in rules)
            {
                if (materialized.Contains(rule.Id)) continue;
                if (!RecurrenceCalculator.OccursIn(rule, period.Year, period.Month)) continue;

                _db.LedgerEntries.Add(new LedgerEntry
                {
                    UserId = userId,
                    PeriodId = period.Id,
                    ConceptId = rule.ConceptId,
                    AccountId = rule.AccountId,
                    RecurringRuleId = rule.Id,
                    Direction = rule.Direction,
                    Status = EntryStatus.Planned,
                    DueDate = RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, period.Year, period.Month),
                    ForecastAmount = rule.ForecastAmount,
                    Description = rule.Description
                });

                created++;
            }

            if (created > 0) await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Una previsiÃ³n cuya fecha ya pasÃ³ y sigue sin confirmarse se
        /// convierte en pendiente: es el "Pending" de la hoja de cÃ¡lculo.
        /// </summary>
        private async Task RefreshOverdueStatusesAsync(Guid userId, MonthlyPeriod period, CancellationToken ct)
        {
            var today = _clock.Today;

            var overdue = await _db.LedgerEntries
                .Where(e => e.UserId == userId
                            && e.PeriodId == period.Id
                            && e.Status == EntryStatus.Planned
                            && e.DueDate < today)
                .ToListAsync(ct);

            if (overdue.Count == 0) return;

            foreach (var entry in overdue)
            {
                entry.Status = EntryStatus.Pending;
                entry.UpdatedAt = _clock.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
