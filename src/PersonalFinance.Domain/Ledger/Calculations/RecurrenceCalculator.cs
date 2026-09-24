using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Calculations
{
    /// <summary>
    /// Decide si una regla recurrente toca en un mes dado y en qué día cae.
    /// Sin dependencias: se puede probar sin base de datos.
    /// </summary>
    public static class RecurrenceCalculator
    {
        public static bool OccursIn(RecurringRule rule, int year, int month)
        {
            if (!rule.IsActive) return false;

            var firstDay = new DateOnly(year, month, 1);
            var lastDay = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            if (rule.StartDate > lastDay) return false;
            if (rule.EndDate is not null && rule.EndDate < firstDay) return false;

            var monthsSinceStart = ((year - rule.StartDate.Year) * 12) + (month - rule.StartDate.Month);
            if (monthsSinceStart < 0) return false;

            return rule.Frequency switch
            {
                RecurrenceFrequency.Monthly => true,
                RecurrenceFrequency.Quarterly => monthsSinceStart % 3 == 0,
                RecurrenceFrequency.Yearly => monthsSinceStart % 12 == 0,
                _ => false
            };
        }

        /// <summary>
        /// Fecha de vencimiento dentro del mes. Un día 31 en febrero se
        /// recorta al último día en lugar de reventar.
        /// </summary>
        public static DateOnly ResolveDueDate(int dayOfMonth, int year, int month)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            return new DateOnly(year, month, Math.Clamp(dayOfMonth, 1, daysInMonth));
        }
    }
}
