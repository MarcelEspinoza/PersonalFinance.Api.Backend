using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Income> Incomes { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Loan> Loans { get; set; }
        public DbSet<LoanPayment> LoanPayments { get; set; }
        public DbSet<SavingAccount> SavingAccounts { get; set; }
        public DbSet<SavingMovement> SavingMovements { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Category>()
                .Property(c => c.Id)
                .ValueGeneratedOnAdd(); // normal para usuarios

            modelBuilder.Entity<Category>().HasData(
                new Category
                {
                    Id = 100,
                    Name = "Préstamo personal",
                    Description = "Categoría de sistema",
                    UserId = Guid.Empty,
                    IsSystem = true,
                    IsActive = true,
                    CreatedAt = new DateTime(2025, 1, 1)
                },
                new Category
                {
                    Id = 101,
                    Name = "Préstamo bancario",
                    Description = "Categoría de sistema",
                    UserId = Guid.Empty,
                    IsSystem = true,
                    IsActive = true,
                    CreatedAt = new DateTime(2025, 1, 1)
                },
                new Category
                {
                    Id = 200,
                    Name = "Ahorro",
                    Description = "Categoría de sistema para registrar aportes de ahorro",
                    UserId = Guid.Empty,
                    IsSystem = true,
                    IsActive = true,
                    CreatedAt = new DateTime(2025, 1, 1)
                }
            );

            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Loan)
                .WithMany()
                .HasForeignKey(e => e.LoanId)
                .OnDelete(DeleteBehavior.Restrict); // no borra ni toca Expenses al borrar Loan

            modelBuilder.Entity<LoanPayment>()
                .HasOne(lp => lp.Loan)
                .WithMany(l => l.Payments)
                .HasForeignKey(lp => lp.LoanId)
                .OnDelete(DeleteBehavior.Cascade); // al borrar Loan, sí se borran sus pagos

            modelBuilder.Entity<LoanPayment>()
                .HasOne(lp => lp.Expense)
                .WithMany() // relación opcional y unidireccional
                .HasForeignKey(lp => lp.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict); // 👈 evita cascadas múltiples


        }


    }
}
