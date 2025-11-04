using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Monthly;
using PersonalFinance.Api.Services.Contracts;

public class MonthlyService : IMonthlyService
{
    private readonly AppDbContext _context;

    public MonthlyService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyDataResponseDto> GetMonthDataAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        // Incomes
        var incomes = await _context.Incomes
        .Include(i => i.Category) 
        .Where(i => i.UserId == userId && i.Date >= startDate && i.Date <= endDate)
        .Select(i => new MonthlyTransactionDto   
        {
            Id = i.Id.ToString(),
            Name = i.Description,
            Amount = i.Amount,
            Date = i.Date,
            CategoryId = i.CategoryId,                
            CategoryName = i.Category != null ? i.Category.Name : "", 
            Type = "income",
            Source = i.Type.ToLower()
        })
        .ToListAsync(ct);

        var expenses = await _context.Expenses
            .Include(e => e.Category)
            .Where(e => e.UserId == userId && e.Date >= startDate && e.Date <= endDate)
            .Select(e => new MonthlyTransactionDto
            {
                Id = e.Id.ToString(),
                Name = e.Description,
                Amount = e.Amount,
                Date = e.Date,
                CategoryId = e.CategoryId,
                CategoryName = e.Category != null ? e.Category.Name : "",
                Type = "expense",
                Source = e.Type.ToLower()
            })
            .ToListAsync(ct);


        var all = incomes.Concat(expenses)
                         .OrderByDescending(t => t.Date)
                         .ToList();

        return new MonthlyDataResponseDto { Transactions = all };
    }
}
