using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models;
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

        private static DateTime NormalizeToUtc(DateTime d)
        {
            if (d.Kind == DateTimeKind.Utc) return d;
            if (d.Kind == DateTimeKind.Local) return d.ToUniversalTime();
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        }

        private static DateTime? NormalizeNullable(DateTime? d) => d.HasValue ? NormalizeToUtc(d.Value) : (DateTime?)null;

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

            // Convertir/normalizar fechas a UTC
            var dateUtc = NormalizeToUtc(dto.Date);
            var startUtc = NormalizeNullable(dto.Start_Date);
            var endUtc = NormalizeNullable(dto.End_Date);

            // Validar categoría
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId, ct);
            if (!categoryExists) throw new ArgumentException($"CategoryId {dto.CategoryId} no existe en la base de datos");

            var income = new Income
            {
                Amount = dto.Amount,
                Description = dto.Description,
                Date = dateUtc,
                Type = dto.Type,
                CategoryId = dto.CategoryId,
                UserId = userId,
                Start_Date = startUtc,
                End_Date = endUtc,
                Notes = dto.Notes,
                LoanId = dto.LoanId,
                IsIndefinite = dto.IsIndefinite ?? false
            };

            _context.Incomes.Add(income);
            await _context.SaveChangesAsync(ct);

            // === Caso 1: ingreso vinculado a préstamo ===
            if (income.LoanId.HasValue && (income.CategoryId == DefaultCategories.PersonalLoan || income.CategoryId == DefaultCategories.BankLoan))
            {
                var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == income.LoanId.Value && l.UserId == userId, ct);
                if (loan == null) throw new InvalidOperationException("Loan not found or not owned by user.");

                var payment = new LoanPayment
                {
                    LoanId = loan.Id,
                    IncomeId = income.Id,
                    Amount = income.Amount,
                    PaymentDate = income.Date,
                    Notes = income.Description
                };
                _context.LoanPayments.Add(payment);

                loan.OutstandingAmount = Math.Max(0, loan.OutstandingAmount - income.Amount);
                loan.Status = loan.OutstandingAmount == 0 ? "paid" : "active";
                _context.Loans.Update(loan);

                await _context.SaveChangesAsync(ct);
            }

            // === Caso 2: ingreso de ahorro ===
            if (income.CategoryId == DefaultCategories.Savings)
            {
                var account = await _context.SavingAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
                if (account == null)
                {
                    account = new SavingAccount { UserId = userId, Balance = 0 };
                    _context.SavingAccounts.Add(account);
                    await _context.SaveChangesAsync(ct);
                }

                var movement = new SavingMovement
                {
                    SavingAccountId = account.Id,
                    Date = income.Date,
                    Amount = income.Amount,
                    Notes = income.Description
                };

                account.Balance += income.Amount;
                _context.SavingMovements.Add(movement);
                _context.SavingAccounts.Update(account);

                await _context.SaveChangesAsync(ct);
            }

            return income;
        }

        public async Task<bool> UpdateAsync(int id, Guid userId, UpdateIncomeDto dto, CancellationToken ct = default)
        {
            var income = await _context.Incomes.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
            if (income == null) return false;

            var oldAmount = income.Amount;
            var oldLoanId = income.LoanId;

            // Actualizar campos
            if (dto.Amount.HasValue) income.Amount = dto.Amount.Value;
            if (dto.Description != null) income.Description = dto.Description;
            if (dto.Date.HasValue) income.Date = NormalizeToUtc(dto.Date.Value);
            if (dto.CategoryId.HasValue) income.CategoryId = dto.CategoryId.Value;
            if (dto.Type != null) income.Type = dto.Type;
            income.Start_Date = NormalizeNullable(dto.Start_Date);
            income.End_Date = NormalizeNullable(dto.End_Date);
            income.Notes = dto.Notes;
            income.LoanId = dto.LoanId;
            income.BankId = dto.BankId;
            if (dto.IsIndefinite.HasValue) income.IsIndefinite = dto.IsIndefinite.Value;

            _context.Incomes.Update(income);

            // === Sincronía LoanPayment ===
            var payment = await _context.LoanPayments.FirstOrDefaultAsync(p => p.IncomeId == id, ct);

            // Caso 1: desvinculado
            if (oldLoanId.HasValue && !income.LoanId.HasValue)
            {
                if (payment != null) _context.LoanPayments.Remove(payment);

                var oldLoan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == oldLoanId.Value && l.UserId == userId, ct);
                if (oldLoan != null)
                {
                    oldLoan.OutstandingAmount += oldAmount;
                    oldLoan.Status = oldLoan.OutstandingAmount == 0 ? "paid" : "active";
                    _context.Loans.Update(oldLoan);
                }
            }

            // Caso 2: mismo préstamo
            if (income.LoanId.HasValue && oldLoanId == income.LoanId)
            {
                var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == income.LoanId.Value && l.UserId == userId, ct);
                if (loan == null) throw new InvalidOperationException("Loan not found.");

                var diff = income.Amount - oldAmount;

                if (payment == null)
                {
                    payment = new LoanPayment
                    {
                        LoanId = loan.Id,
                        IncomeId = income.Id,
                        Amount = income.Amount,
                        PaymentDate = income.Date,
                        Notes = income.Description
                    };
                    _context.LoanPayments.Add(payment);
                    loan.OutstandingAmount = Math.Max(0, loan.OutstandingAmount - income.Amount);
                }
                else
                {
                    payment.Amount = income.Amount;
                    payment.PaymentDate = income.Date;
                    payment.Notes = income.Description;
                    loan.OutstandingAmount = Math.Max(0, loan.OutstandingAmount - diff);
                }

                loan.Status = loan.OutstandingAmount == 0 ? "paid" : "active";
                _context.Loans.Update(loan);
            }

            // Caso 3: cambio de préstamo
            if (oldLoanId.HasValue && income.LoanId.HasValue && oldLoanId != income.LoanId)
            {
                var oldLoan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == oldLoanId.Value && l.UserId == userId, ct);
                if (oldLoan != null)
                {
                    if (payment != null) _context.LoanPayments.Remove(payment);
                    oldLoan.OutstandingAmount += oldAmount;
                    oldLoan.Status = oldLoan.OutstandingAmount == 0 ? "paid" : "active";
                    _context.Loans.Update(oldLoan);
                }

                var newLoan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == income.LoanId.Value && l.UserId == userId, ct);
                if (newLoan == null) throw new InvalidOperationException("New loan not found.");

                var newPayment = new LoanPayment
                {
                    LoanId = newLoan.Id,
                    IncomeId = income.Id,
                    Amount = income.Amount,
                    PaymentDate = income.Date,
                    Notes = income.Description
                };
                _context.LoanPayments.Add(newPayment);

                newLoan.OutstandingAmount = Math.Max(0, newLoan.OutstandingAmount - income.Amount);
                newLoan.Status = newLoan.OutstandingAmount == 0 ? "paid" : "active";
                _context.Loans.Update(newLoan);
            }

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, Guid userId, CancellationToken ct = default)
        {
            var income = await _context.Incomes.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
            if (income == null) return false;

            // LoanPayment
            var payment = await _context.LoanPayments.FirstOrDefaultAsync(p => p.IncomeId == id, ct);
            if (payment != null)
            {
                var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == payment.LoanId && l.UserId == userId, ct);
                if (loan != null)
                {
                    loan.OutstandingAmount += payment.Amount;
                    loan.Status = loan.OutstandingAmount == 0 ? "paid" : "active";
                    _context.Loans.Update(loan);
                }
                _context.LoanPayments.Remove(payment);
            }

            // Movimientos de ahorro
            if (income.CategoryId == DefaultCategories.Savings)
            {
                var account = await _context.SavingAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
                if (account != null)
                {
                    var movement = await _context.SavingMovements
                        .FirstOrDefaultAsync(m => m.SavingAccountId == account.Id && m.Amount == income.Amount && m.Date == income.Date, ct);

                    if (movement != null)
                    {
                        account.Balance -= movement.Amount;
                        _context.SavingMovements.Remove(movement);
                        _context.SavingAccounts.Update(account);
                    }
                }
            }

            _context.Incomes.Remove(income);
            await _context.SaveChangesAsync(ct);
            return true;
        }
    }
}