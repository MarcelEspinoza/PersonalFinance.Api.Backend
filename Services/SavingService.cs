namespace PersonalFinance.Api.Services
{
    using Microsoft.EntityFrameworkCore;
    using PersonalFinance.Api.Data;
    using PersonalFinance.Api.Models;
    using PersonalFinance.Api.Models.Dtos.Saving;
    using PersonalFinance.Api.Models.Entities;
    using PersonalFinance.Api.Services.Contracts;

    public class SavingService : ISavingService
    {
        private readonly AppDbContext _context;

        public SavingService(AppDbContext context)
        {
            _context = context;
        }

        public async Task PlanSavingsAsync(Guid userId, PlanSavingsDto dto, CancellationToken ct = default)
        {
            var start = dto.StartDate ?? DateTime.UtcNow;

            for (int i = 0; i < dto.Months; i++)
            {
                var date = new DateTime(start.Year, start.Month, 1).AddMonths(i);

                var expense = new Expense
                {
                    UserId = userId,
                    Amount = dto.MonthlyAmount,
                    Description = "Ahorro planificado",
                    Date = date,
                    Type = "Temporary", // 👈 marca como temporal
                    CategoryId = DefaultCategories.Savings
                };

                _context.Expenses.Add(expense);
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task<decimal> GetSavingsForMonthAsync(Guid userId, DateTime month, CancellationToken ct = default)
        {
            var start = new DateTime(month.Year, month.Month, 1);
            var end = start.AddMonths(1).AddTicks(-1);

            return await _context.SavingMovements
                .Where(m => m.SavingAccount.UserId == userId && m.Date >= start && m.Date <= end)
                .SumAsync(m => m.Amount, ct);
        }
    }

}
