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
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be > 0");
            if (dto.Date == default) throw new ArgumentException("Date is required");

            var expense = new Expense
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
                LoanId = dto.LoanId
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync(ct); // 👈 primero guardamos, ya tenemos Expense.Id

            // Si está vinculado a préstamo, crear LoanPayment y actualizar el saldo
            if (expense.LoanId.HasValue && (expense.CategoryId == 100 || expense.CategoryId == 101))
            {
                var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == expense.LoanId.Value && l.UserId == userId, ct);
                if (loan == null) throw new InvalidOperationException("Loan not found or not owned by user.");

                var payment = new LoanPayment
                {
                    LoanId = loan.Id,
                    ExpenseId = expense.Id,
                    Amount = expense.Amount,
                    PaymentDate = expense.Date,
                    Notes = expense.Description
                };
                _context.LoanPayments.Add(payment);

                loan.OutstandingAmount = Math.Max(0, loan.OutstandingAmount - expense.Amount);
                loan.Status = loan.OutstandingAmount == 0 ? "paid" : "active";
                _context.Loans.Update(loan);

                await _context.SaveChangesAsync(ct);
            }

            return expense;
        }

        public async Task<bool> UpdateAsync(int id, Guid userId, UpdateExpenseDto dto, CancellationToken ct = default)
        {
            var expense = await _context.Expenses.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
            if (expense == null) return false;

            var oldAmount = expense.Amount;
            var oldLoanId = expense.LoanId;
            var oldDate = expense.Date;

            if (dto.Amount.HasValue) expense.Amount = dto.Amount.Value;
            if (dto.Description != null) expense.Description = dto.Description;
            if (dto.Date.HasValue) expense.Date = dto.Date.Value;
            if (dto.CategoryId.HasValue) expense.CategoryId = dto.CategoryId.Value;
            if (dto.Type != null) expense.Type = dto.Type;
            expense.Start_Date = dto.Start_Date;
            expense.End_Date = dto.End_Date;
            expense.Notes = dto.Notes;
            expense.LoanId = dto.LoanId;

            _context.Expenses.Update(expense);

            // Sincronía LoanPayment por ExpenseId (evita ambigüedades)
            var payment = await _context.LoanPayments.FirstOrDefaultAsync(p => p.ExpenseId == id, ct);

            // Caso 1: desvinculado ahora
            if (oldLoanId.HasValue && !expense.LoanId.HasValue)
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

            // Caso 2: sigue vinculado (mismo préstamo) → actualizar payment + ajustar diferencia
            if (expense.LoanId.HasValue && oldLoanId == expense.LoanId)
            {
                var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == expense.LoanId.Value && l.UserId == userId, ct);
                if (loan == null) throw new InvalidOperationException("Loan not found.");

                var diff = expense.Amount - oldAmount;

                if (payment == null)
                {
                    payment = new LoanPayment
                    {
                        LoanId = loan.Id,
                        ExpenseId = expense.Id,
                        Amount = expense.Amount,
                        PaymentDate = expense.Date,
                        Notes = expense.Description
                    };
                    _context.LoanPayments.Add(payment);
                    loan.OutstandingAmount = Math.Max(0, loan.OutstandingAmount - expense.Amount);
                }
                else
                {
                    payment.Amount = expense.Amount;
                    payment.PaymentDate = expense.Date;
                    payment.Notes = expense.Description;
                    loan.OutstandingAmount = Math.Max(0, loan.OutstandingAmount - diff);
                }

                loan.Status = loan.OutstandingAmount == 0 ? "paid" : "active";
                _context.Loans.Update(loan);
            }

            // Caso 3: cambió de préstamo → eliminar payment anterior y crear uno nuevo
            if (oldLoanId.HasValue && expense.LoanId.HasValue && oldLoanId != expense.LoanId)
            {
                // Restituir en préstamo anterior
                var oldLoan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == oldLoanId.Value && l.UserId == userId, ct);
                if (oldLoan != null)
                {
                    if (payment != null) _context.LoanPayments.Remove(payment);
                    oldLoan.OutstandingAmount += oldAmount;
                    oldLoan.Status = oldLoan.OutstandingAmount == 0 ? "paid" : "active";
                    _context.Loans.Update(oldLoan);
                }

                // Crear nuevo payment en nuevo préstamo
                var newLoan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == expense.LoanId.Value && l.UserId == userId, ct);
                if (newLoan == null) throw new InvalidOperationException("New loan not found.");

                var newPayment = new LoanPayment
                {
                    LoanId = newLoan.Id,
                    ExpenseId = expense.Id,
                    Amount = expense.Amount,
                    PaymentDate = expense.Date,
                    Notes = expense.Description
                };
                _context.LoanPayments.Add(newPayment);

                newLoan.OutstandingAmount = Math.Max(0, newLoan.OutstandingAmount - expense.Amount);
                newLoan.Status = newLoan.OutstandingAmount == 0 ? "paid" : "active";
                _context.Loans.Update(newLoan);
            }

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, Guid userId, CancellationToken ct = default)
        {
            var expense = await _context.Expenses.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
            if (expense == null) return false;

            // Si estaba vinculado a préstamo: eliminar payment y restaurar saldo
            var payment = await _context.LoanPayments.FirstOrDefaultAsync(p => p.ExpenseId == id, ct);
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

            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync(ct);
            return true;
        }


    }
}