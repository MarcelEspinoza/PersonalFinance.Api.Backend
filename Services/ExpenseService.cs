using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class ExpenseService : IExpenseService
    {
        private readonly AppDbContext _context;

        public ExpenseService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Expense>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Expenses
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        public async Task<Expense?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Expenses
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        }

        public async Task<Expense> CreateAsync(Guid userId, CreateExpenseDto dto, CancellationToken ct)
        {
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be greater than zero", nameof(dto.Amount));
            if (dto.Date == default) throw new ArgumentException("Date is required", nameof(dto.Date));
            // CategoryId is required in DTO by design; if your model stores category relation, consider validating existence here.
            var Expense = new Expense
            {
                Amount = dto.Amount,
                Description = dto.Description,
                Date = dto.Date,
                Type = dto.Type,
                CategoryId = dto.CategoryId,
                UserId = userId,
                Start_Date = dto.Start_Date,
                End_Date = dto.End_Date,
                Notes = dto.Notes
            };

            _context.Expenses.Add(Expense);
            await _context.SaveChangesAsync(ct);
            return Expense;
        }

        public async Task<bool> UpdateAsync(int id, Guid userId, UpdateExpenseDto dto, CancellationToken cancellationToken = default)
        {
            var Expense = await _context.Expenses.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
            if (Expense == null) return false;

            if (dto.Amount.HasValue)
                Expense.Amount = dto.Amount.Value;

            if (dto.Description != null)
                Expense.Description = dto.Description;

            if (dto.Date.HasValue)
                Expense.Date = dto.Date.Value;

            // If your Expense entity contains CategoryId, you can assign it when dto.CategoryId.HasValue
            if (dto.CategoryId.HasValue) Expense.CategoryId = dto.CategoryId.Value;

            if (dto.Type != null) 
            Expense.Type = dto.Type;

            _context.Expenses.Update(Expense);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            var Expense = await _context.Expenses.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
            if (Expense == null) return false;

            _context.Expenses.Remove(Expense);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}