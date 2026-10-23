using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Enums;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly AppDbContext _context;
        public PaymentService(AppDbContext context) => _context = context;

        public async Task<LoanPayment> CreatePaymentAsync(Guid loanId, LoanPayment payment)
        {
            var loan = await _context.Loans.FindAsync(loanId);
            if (loan == null) throw new Exception("Loan not found");

            return await RegisterPaymentAsync(loan, payment);
        }

        public async Task<LoanPayment?> CreatePaymentAsync(Guid userId, Guid loanId, LoanPayment payment)
        {
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId && l.UserId == userId);
            if (loan == null) return null;

            // Solo se aceptan los datos del pago: FKs/navegaciones del cuerpo (Loan, Expense, Income) se ignoran.
            var sanitized = new LoanPayment
            {
                Amount = payment.Amount,
                PaymentDate = payment.PaymentDate,
                Notes = payment.Notes
            };

            return await RegisterPaymentAsync(loan, sanitized);
        }

        private async Task<LoanPayment> RegisterPaymentAsync(Loan loan, LoanPayment payment)
        {
            payment.LoanId = loan.Id;
            _context.LoanPayments.Add(payment);

            // Actualizar saldo
            loan.OutstandingAmount -= payment.Amount;
            if (loan.OutstandingAmount <= 0)
            {
                loan.OutstandingAmount = 0;
                loan.Status = "paid";
            }

            if (loan.Type == LoanType.Bank || loan.Type == LoanType.Received)
            {
                var expense = new Expense
                {
                    UserId = loan.UserId,
                    Description = loan.Type == LoanType.Bank
                        ? $"Pago préstamo bancario {loan.Name}"
                        : $"Devolución préstamo recibido {loan.Name}",
                    Amount = payment.Amount,
                    Date = payment.PaymentDate,
                    CategoryId = loan.CategoryId, // 👈 directo del Loan
                    Notes = payment.Notes,
                    Type = loan.Type == LoanType.Bank ? "Fixed" : "Variable"
                };
                _context.Expenses.Add(expense);
            }
            else if (loan.Type == LoanType.Given)
            {
                var income = new Income
                {
                    UserId = loan.UserId,
                    Description = $"Cobro préstamo prestado {loan.Name}",
                    Amount = payment.Amount,
                    Date = payment.PaymentDate,
                    CategoryId = loan.CategoryId, // 👈 directo del Loan
                    Notes = payment.Notes,
                    Type = "Variable"
                };
                _context.Incomes.Add(income);
            }


            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task<IEnumerable<LoanPayment>?> GetPaymentsAsync(Guid userId, Guid loanId)
        {
            var ownsLoan = await _context.Loans.AnyAsync(l => l.Id == loanId && l.UserId == userId);
            if (!ownsLoan) return null;

            return await _context.LoanPayments.Where(p => p.LoanId == loanId).ToListAsync();
        }
    }

}
