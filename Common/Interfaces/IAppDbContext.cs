using Microsoft.EntityFrameworkCore;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Common.Interfaces
{
    /// <summary>
    /// Puerto de persistencia. La capa de aplicaciÃ³n depende de esta interfaz,
    /// no del DbContext concreto, asÃ­ que los casos de uso se pueden probar
    /// contra cualquier proveedor.
    /// </summary>
    public interface IAppDbContext
    {
        DbSet<Account> Accounts { get; }
        /// <summary>Tabla legada "Banco", mantenida en sincronía con Accounts para no romper Gastos/Ingresos/Compromisos/Conciliación.</summary>
        DbSet<PersonalFinance.Api.Models.Entities.Bank> Banks { get; }
        DbSet<ConceptGroup> ConceptGroups { get; }
        DbSet<Concept> Concepts { get; }
        DbSet<MonthlyPeriod> MonthlyPeriods { get; }
        DbSet<LedgerEntry> LedgerEntries { get; }
        DbSet<RecurringRule> RecurringRules { get; }
        DbSet<MonthlyBudget> MonthlyBudgets { get; }
        DbSet<Debt> Debts { get; }
        DbSet<DebtScheduleItem> DebtScheduleItems { get; }
        DbSet<Counterparty> Counterparties { get; }
        DbSet<PersonalLoan> PersonalLoans { get; }
        DbSet<SavingsGoal> SavingsGoals { get; }
        DbSet<ImportBatch> ImportBatches { get; }
        DbSet<ImportRow> ImportRows { get; }
        DbSet<ConceptMapping> ConceptMappings { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
