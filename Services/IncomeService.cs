using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services
{
    public class IncomeService : IIncomeService
    {
        private readonly AppDbContext _context;

        public IncomeService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Income>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Incomes
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        public async Task<Income?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Incomes
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        }

        public async Task<Income> CreateAsync(Guid userId, CreateIncomeDto dto, CancellationToken cancellationToken = default)
        {
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be greater than zero", nameof(dto.Amount));
            if (dto.Date == default) throw new ArgumentException("Date is required", nameof(dto.Date));
            // CategoryId is required in DTO by design; if your model stores category relation, consider validating existence here.

            var income = new Income
            {
                Amount = dto.Amount,
                Description = dto.Description,
                Date = dto.Date,
                Type = dto.Type,
                CategoryId = dto.CategoryId,
                UserId = userId
            };

            _context.Incomes.Add(income);
            await _context.SaveChangesAsync(cancellationToken);
            return income;
        }

        public async Task<bool> UpdateAsync(int id, Guid userId, UpdateIncomeDto dto, CancellationToken cancellationToken = default)
        {
            var income = await _context.Incomes.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
            if (income == null) return false;

            if (dto.Amount.HasValue)
                income.Amount = dto.Amount.Value;

            if (dto.Description != null)
                income.Description = dto.Description;

            if (dto.Date.HasValue)
                income.Date = dto.Date.Value;

            // If your Income entity contains CategoryId, you can assign it when dto.CategoryId.HasValue
            if (dto.CategoryId.HasValue) income.CategoryId = dto.CategoryId.Value;

            // Update type - dto.Type is present in DTO; assign unconditionally to reflect intent to update
            income.Type = dto.Type;

            _context.Incomes.Update(income);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            var income = await _context.Incomes.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
            if (income == null) return false;

            _context.Incomes.Remove(income);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}