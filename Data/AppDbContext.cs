using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Income> Incomes { get; set; }
        public DbSet<Category> Categories { get; set; }
}


}
