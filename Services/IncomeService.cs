using Microsoft.EntityFrameworkCore;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class IncomeService : IIncomeService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public IncomeService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<IncomeDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Incomes
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .ProjectTo<IncomeDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);
        }

        public async Task<IncomeDto?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Incomes
                .AsNoTracking()
                .Where(i => i.UserId == userId && i.Id == id)
                .ProjectTo<IncomeDto>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync(cancellationToken);
        }


        public async Task<Income> CreateAsync(Guid userId, CreateIncomeDto dto, CancellationToken ct)
        {
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be greater than zero", nameof(dto.Amount));
            if (dto.Date == default) throw new ArgumentException("Date is required", nameof(dto.Date));

            // Validar que la categoría existe
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId, ct);
            if (!categoryExists)
                throw new ArgumentException($"CategoryId {dto.CategoryId} no existe en la base de datos");

            var income = new Income
            {
                Amount = dto.Amount,
                Description = dto.Description,
                Date = dto.Date,
                Type = dto.Type,
                CategoryId = dto.CategoryId,
                UserId = userId,
                Start_Date = dto.Start_Date,
                End_Date = dto.End_Date,
                Notes = dto.Notes,
                LoanId = dto.LoanId,
                IsIndefinite = dto.IsIndefinite ?? false
            };

            _context.Incomes.Add(income);
            await _context.SaveChangesAsync(ct);
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

            if (dto.Type != null)
                income.Type = dto.Type;

            if (dto.LoanId.HasValue)
                income.LoanId = dto.LoanId.Value;

            if (dto.IsIndefinite.HasValue)
                income.IsIndefinite = dto.IsIndefinite.Value;


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