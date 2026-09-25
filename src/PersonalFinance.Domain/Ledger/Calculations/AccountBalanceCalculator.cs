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
        /// de la cuenta con fecha real hasta <paramref name="asOf"/>, ambos
        /// inclusive. Los traspasos cuentan: mueven dinero real de la cuenta.
        /// Las líneas descartadas (Skipped) no.
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
                    e.ValueDate.Value <= asOf)
                .Sum(e => e.SignedEffectiveAmount);

            return opening + paidMovements;
        }
    }
}
