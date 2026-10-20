using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Calculations
{
    /// <summary>
    /// Saldo bancario real de una cuenta concreta a una fecha, para poder
    /// contrastarlo con el extracto del banco. Es aditivo y no toca
    /// <see cref="MonthlyPeriod"/>: el presupuesto combinado del hogar sigue
    /// siendo por (usuario, año, mes), esto sólo cuadra una cuenta.
    /// </summary>
    public static class AccountBalanceCalculator
    {
        /// <summary>
        /// Saldo de apertura + todos los asientos ya liquidados (Status=Paid)
        /// de la cuenta desde su fecha de apertura contable hasta
        /// <paramref name="asOf"/>, ambos inclusive. Los movimientos anteriores
        /// siguen en el histórico, pero no alteran un saldo rebasado.
        /// </summary>
        public static decimal ComputeBalance(
            Account account, IEnumerable<LedgerEntry> entries, DateOnly asOf)
        {
            ArgumentNullException.ThrowIfNull(account);

            var opening = account.OpeningDate <= asOf ? account.OpeningBalance : 0m;

            var paidMovements = entries
                .Where(e =>
                    e.AccountId == account.Id &&
                    e.Status == EntryStatus.Paid &&
                    e.ValueDate is not null &&
                    e.ValueDate.Value >= account.OpeningDate &&
                    e.ValueDate.Value <= asOf)
                .Sum(e => e.SignedEffectiveAmount);

            return opening + paidMovements;
        }

        /// <summary>
        /// Corrección del saldo real a primero de mes para que cuadre con un
        /// presupuesto que asigna cada movimiento a un mes que no siempre es el de
        /// su fecha real. La nómina de octubre cobrada el 28 de septiembre ya está
        /// en el saldo del día 1 y además es ingreso de octubre: se resta para no
        /// contarla dos veces. Un recibo de septiembre pagado el 2 de octubre aún
        /// no ha salido el día 1 pero ya se contó en septiembre: también se resta.
        /// </summary>
        /// <param name="belongsTo">Primer día del mes al que pertenece el asiento.</param>
        public static decimal ComputeMonthStartAdjustment(
            IEnumerable<LedgerEntry> entries, DateOnly monthStart, Func<LedgerEntry, DateOnly> belongsTo)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(belongsTo);

            var adjustment = 0m;
            foreach (var entry in entries)
            {
                if (entry.Status != EntryStatus.Paid || entry.ValueDate is null) continue;

                var month = belongsTo(entry);
                if (entry.ValueDate.Value < monthStart && month >= monthStart)
                    adjustment -= entry.SignedEffectiveAmount;
                else if (entry.ValueDate.Value >= monthStart && month < monthStart)
                    adjustment += entry.SignedEffectiveAmount;
            }

            return adjustment;
        }
    }
}
