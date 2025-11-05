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

            payment.LoanId = loanId;
            _context.LoanPayments.Add(payment);

            // Actualizar saldo
            loan.OutstandingAmount -= payment.Amount;
            if (loan.OutstandingAmount <= 0)
            {
                loan.OutstandingAmount = 0;
                loan.Status = "paid";
            }

            // Buscar categorías
            var catPersonal = _context.Categories.FirstOrDefault(c => c.Name == "Préstamo personal");
            var catBank = _context.Categories.FirstOrDefault(c => c.Name == "Préstamo bancario");

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

        public async Task<IEnumerable<LoanPayment>> GetPaymentsAsync(Guid loanId) =>
            await _context.LoanPayments.Where(p => p.LoanId == loanId).ToListAsync();
    }

}
