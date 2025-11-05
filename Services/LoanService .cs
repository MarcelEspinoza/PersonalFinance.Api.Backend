using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class LoanService : ILoanService
    {
        private readonly AppDbContext _context;
        public LoanService(AppDbContext context) => _context = context;

        public async Task<IEnumerable<Loan>> GetLoansAsync(Guid userId) =>
            await _context.Loans.Where(l => l.UserId == userId).ToListAsync();

        public async Task<Loan?> GetLoanAsync(Guid id) =>
            await _context.Loans.Include(l => l.Payments).FirstOrDefaultAsync(l => l.Id == id);

        public async Task<Loan> CreateLoanAsync(Loan loan)
        {
            _context.Loans.Add(loan);
            await _context.SaveChangesAsync();
            return loan;
        }

        public async Task UpdateLoanAsync(Loan loan)
        {
            _context.Loans.Update(loan);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteLoanAsync(Guid id)
        {
            var loan = await _context.Loans.FindAsync(id);
            if (loan != null)
            {
                _context.Loans.Remove(loan);
                await _context.SaveChangesAsync();
            }
        }
    }
}
