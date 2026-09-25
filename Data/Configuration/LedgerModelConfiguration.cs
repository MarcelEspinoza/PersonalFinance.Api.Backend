using Microsoft.EntityFrameworkCore;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Data.Configuration
{
    /// <summary>
    /// Mapeo del dominio contable. Vive aquÃ­, y no en las entidades, para que
    /// el dominio no sepa nada de bases de datos.
    /// </summary>
    public static class LedgerModelConfiguration
    {
        private const string Money = "decimal(18,2)";
        private const string Rate = "decimal(9,6)";

        public static ModelBuilder ApplyLedgerModel(this ModelBuilder b)
        {
            ConfigureAccounts(b);
            ConfigureConcepts(b);
            ConfigurePeriods(b);
            ConfigureLedgerEntries(b);
            ConfigureRecurringRules(b);
            ConfigureBudgets(b);
            ConfigureDebts(b);
            ConfigureCounterparties(b);
            ConfigureSavings(b);
            ConfigureImports(b);

            return b;
        }

        private static void ConfigureAccounts(ModelBuilder b) => b.Entity<Account>(e =>
        {
            e.ToTable("LedgerAccounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.OpeningBalance).HasColumnType(Money);

            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        private static void ConfigureConcepts(ModelBuilder b)
        {
            b.Entity<ConceptGroup>(e =>
            {
                e.ToTable("ConceptGroups");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);

                e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            });

            b.Entity<Concept>(e =>
            {
                e.ToTable("Concepts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);

                e.HasOne(x => x.Group)
                    .WithMany(g => g.Concepts)
                    .HasForeignKey(x => x.GroupId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.UserId, x.GroupId, x.Name }).IsUnique();
                e.HasIndex(x => x.UserId);
            });
        }

        private static void ConfigurePeriods(ModelBuilder b) => b.Entity<MonthlyPeriod>(e =>
        {
            e.ToTable("MonthlyPeriods");
            e.HasKey(x => x.Id);
            e.Property(x => x.CarryOverAmount).HasColumnType(Money);
            e.Property(x => x.ClosingBalance).HasColumnType(Money);
            e.Ignore(x => x.FirstDay);

            // Un usuario sÃ³lo puede tener un periodo por mes: es la garantÃ­a de
            // que no se dupliquen cuadros mensuales por una llamada simultÃ¡nea.
            e.HasIndex(x => new { x.UserId, x.Year, x.Month }).IsUnique();
        });

        private static void ConfigureLedgerEntries(ModelBuilder b) => b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("LedgerEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.ForecastAmount).HasColumnType(Money);
            e.Property(x => x.ActualAmount).HasColumnType(Money);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Fingerprint).HasMaxLength(64);

            e.Ignore(x => x.Remaining);
            e.Ignore(x => x.EffectiveAmount);
            e.Ignore(x => x.SignedEffectiveAmount);

            e.HasOne(x => x.Period)
                .WithMany(p => p.Entries)
                .HasForeignKey(x => x.PeriodId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Concept)
                .WithMany(c => c.Entries)
                .HasForeignKey(x => x.ConceptId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Account)
                .WithMany(a => a.Entries)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.RecurringRule)
                .WithMany(r => r.Entries)
                .HasForeignKey(x => x.RecurringRuleId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.DebtScheduleItem)
                .WithMany()
                .HasForeignKey(x => x.DebtScheduleItemId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.PersonalLoan)
                .WithMany(l => l.Entries)
                .HasForeignKey(x => x.PersonalLoanId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.SavingsGoal)
                .WithMany(s => s.Entries)
                .HasForeignKey(x => x.SavingsGoalId)
                .OnDelete(DeleteBehavior.SetNull);

            // Huella antiduplicados de la importaciÃ³n bancaria. MySQL admite
            // varios NULL en un Ã­ndice Ãºnico, que es justo lo que hace falta:
            // los asientos creados a mano no tienen huella y no deben chocar.
            e.HasIndex(x => new { x.UserId, x.Fingerprint }).IsUnique();

            e.HasIndex(x => new { x.UserId, x.PeriodId });
            e.HasIndex(x => new { x.UserId, x.DueDate });
            e.HasIndex(x => new { x.UserId, x.Status });
        });

        private static void ConfigureRecurringRules(ModelBuilder b) => b.Entity<RecurringRule>(e =>
        {
            e.ToTable("RecurringRules");
            e.HasKey(x => x.Id);
            e.Property(x => x.ForecastAmount).HasColumnType(Money);
            e.Property(x => x.Description).HasMaxLength(300);

            e.HasOne(x => x.Concept)
                .WithMany(c => c.Rules)
                .HasForeignKey(x => x.ConceptId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => new { x.UserId, x.IsActive });
        });

        private static void ConfigureBudgets(ModelBuilder b) => b.Entity<MonthlyBudget>(e =>
        {
            e.ToTable("MonthlyBudgets");
            e.HasKey(x => x.Id);
            e.Property(x => x.LimitAmount).HasColumnType(Money);

            e.HasOne(x => x.Concept)
                .WithMany()
                .HasForeignKey(x => x.ConceptId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.UserId, x.ConceptId, x.Year, x.Month }).IsUnique();
        });

        private static void ConfigureDebts(ModelBuilder b)
        {
            b.Entity<Debt>(e =>
            {
                e.ToTable("Debts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);
                e.Property(x => x.Lender).HasMaxLength(120);
                e.Property(x => x.Notes).HasMaxLength(500);
                e.Property(x => x.PrincipalAmount).HasColumnType(Money);
                e.Property(x => x.InstallmentAmount).HasColumnType(Money);
                e.Property(x => x.AnnualNominalRate).HasColumnType(Rate);
                e.Property(x => x.AnnualEquivalentRate).HasColumnType(Rate);

                e.HasOne(x => x.Concept)
                    .WithMany()
                    .HasForeignKey(x => x.ConceptId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Account)
                    .WithMany()
                    .HasForeignKey(x => x.AccountId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(x => new { x.UserId, x.Status });
            });

            b.Entity<DebtScheduleItem>(e =>
            {
                e.ToTable("DebtScheduleItems");
                e.HasKey(x => x.Id);
                e.Property(x => x.InstallmentAmount).HasColumnType(Money);
                e.Property(x => x.PrincipalPortion).HasColumnType(Money);
                e.Property(x => x.InterestPortion).HasColumnType(Money);
                e.Property(x => x.RemainingPrincipal).HasColumnType(Money);
                e.Property(x => x.PaidAmount).HasColumnType(Money);

                e.HasOne(x => x.Debt)
                    .WithMany(d => d.Schedule)
                    .HasForeignKey(x => x.DebtId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.DebtId, x.InstallmentNumber }).IsUnique();
            });
        }

        private static void ConfigureCounterparties(ModelBuilder b)
        {
            b.Entity<Counterparty>(e =>
            {
                e.ToTable("Counterparties");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);
                e.Property(x => x.Notes).HasMaxLength(500);

                e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            });

            b.Entity<PersonalLoan>(e =>
            {
                e.ToTable("PersonalLoans");
                e.HasKey(x => x.Id);
                e.Property(x => x.Description).HasMaxLength(300);
                e.Property(x => x.Notes).HasMaxLength(500);
                e.Property(x => x.PrincipalAmount).HasColumnType(Money);

                e.HasOne(x => x.Counterparty)
                    .WithMany(c => c.Loans)
                    .HasForeignKey(x => x.CounterpartyId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.UserId, x.IsSettled });
            });
        }

        private static void ConfigureSavings(ModelBuilder b) => b.Entity<SavingsGoal>(e =>
        {
            e.ToTable("SavingsGoals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.TargetAmount).HasColumnType(Money);
            e.Property(x => x.OpeningBalance).HasColumnType(Money);

            e.HasOne(x => x.Counterparty)
                .WithMany()
                .HasForeignKey(x => x.CounterpartyId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => new { x.UserId, x.IsActive });
        });

        private static void ConfigureImports(ModelBuilder b)
        {
            b.Entity<ImportBatch>(e =>
            {
                e.ToTable("ImportBatches");
                e.HasKey(x => x.Id);
                e.Property(x => x.FileName).HasMaxLength(260);

                e.HasOne(x => x.Account)
                    .WithMany()
                    .HasForeignKey(x => x.AccountId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(x => new { x.UserId, x.Status });
            });

            b.Entity<ImportRow>(e =>
            {
                e.ToTable("ImportRows");
                e.HasKey(x => x.Id);
                e.Property(x => x.Amount).HasColumnType(Money);
                e.Property(x => x.Currency).HasMaxLength(3);
                e.Property(x => x.RawDescription).IsRequired().HasMaxLength(500);
                e.Property(x => x.NormalizedDescription).HasMaxLength(500);
                e.Property(x => x.Fingerprint).IsRequired().HasMaxLength(64);
                e.Property(x => x.SuggestionSource).HasMaxLength(40);
                e.Property(x => x.SuggestionConfidence).HasColumnType("decimal(5,4)");

                e.HasOne(x => x.Batch)
                    .WithMany(bt => bt.Rows)
                    .HasForeignKey(x => x.BatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.SuggestedConcept)
                    .WithMany()
                    .HasForeignKey(x => x.SuggestedConceptId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(x => new { x.UserId, x.Fingerprint });
                e.HasIndex(x => new { x.BatchId, x.RowNumber }).IsUnique();
            });

            b.Entity<ConceptMapping>(e =>
            {
                e.ToTable("ConceptMappings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Pattern).IsRequired().HasMaxLength(200);

                e.HasOne(x => x.Concept)
                    .WithMany()
                    .HasForeignKey(x => x.ConceptId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Account)
                    .WithMany()
                    .HasForeignKey(x => x.AccountId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(x => new { x.UserId, x.AccountId, x.Pattern }).IsUnique();
            });
        }
    }
}
