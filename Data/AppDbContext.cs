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
        public DbSet<Pasanaco> Pasanacos { get; set; }
        public DbSet<Participant> Participants { get; set; }
        public DbSet<PasanacoPayment> PasanacoPayments { get; set; }


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
                },
                new Category
                {
                    Id = 300,
                    Name = "Pasanaco",
                    Description = "Pagos mensuales del juego Pasanaco",
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

            modelBuilder.Entity<Pasanaco>()
                .HasMany(p => p.Participants)
                .WithOne(p => p.Pasanaco)
                .HasForeignKey(p => p.PasanacoId);

            modelBuilder.Entity<Pasanaco>()
                .HasMany(p => p.Payments)
                .WithOne(p => p.Pasanaco)
                .HasForeignKey(p => p.PasanacoId);

            modelBuilder.Entity<Participant>()
                .HasMany(p => p.Payments)
                .WithOne(p => p.Participant)
                .HasForeignKey(p => p.ParticipantId);

            modelBuilder.Entity<PasanacoPayment>()
                .HasOne(p => p.Participant)
                .WithMany(p => p.Payments)
                .HasForeignKey(p => p.ParticipantId);

            modelBuilder.Entity<PasanacoPayment>()
                .HasOne(p => p.Pasanaco)
                .WithMany(p => p.Payments)
                .HasForeignKey(p => p.PasanacoId)
                .OnDelete(DeleteBehavior.Restrict);



        }


    }
}
