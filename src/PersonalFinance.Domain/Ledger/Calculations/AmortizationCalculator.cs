using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Domain.Ledger.Calculations
{
    /// <summary>
    /// Cuadro de amortización francés: cuota constante, en la que la parte de
    /// intereses baja y la de capital sube a medida que avanza el préstamo.
    /// </summary>
    public static class AmortizationCalculator
    {
        /// <summary>
        /// Cuota mensual constante.
        /// c = P · i / (1 − (1+i)^−n), con i = TIN anual / 12.
        /// Si el tipo es cero, se reparte el capital a partes iguales.
        /// </summary>
        public static decimal MonthlyInstallment(decimal principal, decimal annualNominalRatePercent, int termMonths)
        {
            if (termMonths <= 0)
                throw new ArgumentOutOfRangeException(nameof(termMonths), "El plazo debe ser de al menos un mes.");

            if (principal <= 0m) return 0m;

            var monthlyRate = annualNominalRatePercent / 100m / 12m;

            if (monthlyRate == 0m)
                return Round(principal / termMonths);

            var factor = Math.Pow(1 + (double)monthlyRate, -termMonths);
            var installment = (double)principal * (double)monthlyRate / (1 - factor);

            return Round((decimal)installment);
        }

        /// <summary>
        /// Genera el cuadro completo. La última cuota absorbe el redondeo para
        /// que el capital pendiente acabe exactamente en cero.
        /// </summary>
        public static List<DebtScheduleItem> BuildSchedule(Debt debt)
        {
            if (debt.TermMonths <= 0)
                throw new ArgumentOutOfRangeException(nameof(debt), "El plazo debe ser de al menos un mes.");

            var monthlyRate = debt.AnnualNominalRate / 100m / 12m;

            var installment = debt.InstallmentAmount > 0m
                ? debt.InstallmentAmount
                : MonthlyInstallment(debt.PrincipalAmount, debt.AnnualNominalRate, debt.TermMonths);

            var schedule = new List<DebtScheduleItem>(debt.TermMonths);
            var balance = debt.PrincipalAmount;

            for (var n = 1; n <= debt.TermMonths; n++)
            {
                var interest = Round(balance * monthlyRate);
                var principalPortion = Round(installment - interest);
                var isLast = n == debt.TermMonths;

                // La última cuota liquida lo que quede, pase lo que pase con
                // los redondeos de los meses anteriores.
                if (isLast || principalPortion > balance)
                {
                    principalPortion = balance;
                    installment = Round(principalPortion + interest);
                }

                balance = Round(balance - principalPortion);

                schedule.Add(new DebtScheduleItem
                {
                    UserId = debt.UserId,
                    DebtId = debt.Id,
                    InstallmentNumber = n,
                    DueDate = RecurrenceCalculator.ResolveDueDate(
                        debt.PaymentDayOfMonth,
                        debt.StartDate.AddMonths(n - 1).Year,
                        debt.StartDate.AddMonths(n - 1).Month),
                    InstallmentAmount = installment,
                    InterestPortion = interest,
                    PrincipalPortion = principalPortion,
                    RemainingPrincipal = balance
                });

                if (balance <= 0m) break;
            }

            return schedule;
        }

        /// <summary>Intereses totales que se pagarán a lo largo de la vida del préstamo.</summary>
        public static decimal TotalInterest(IEnumerable<DebtScheduleItem> schedule) =>
            schedule.Sum(i => i.InterestPortion);

        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
