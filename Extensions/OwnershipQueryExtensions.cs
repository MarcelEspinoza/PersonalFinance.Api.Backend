using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Extensions
{
    /// <summary>
    /// Comprobaciones de que un id recibido en una petición se puede referenciar desde los datos del usuario.
    /// No distinguen "no existe" de "es de otro usuario", para no filtrar información.
    /// </summary>
    public static class OwnershipQueryExtensions
    {
        public static Task<bool> IsCategoryVisibleAsync(this AppDbContext db, Guid userId, int categoryId, CancellationToken ct = default) =>
            db.Categories.AnyAsync(c => c.Id == categoryId && (c.IsSystem || c.UserId == userId), ct);

        public static async Task<bool> IsBankOwnedOrNullAsync(this AppDbContext db, Guid userId, Guid? bankId, CancellationToken ct = default) =>
            !bankId.HasValue || await db.Banks.AnyAsync(b => b.Id == bankId.Value && b.UserId == userId, ct);

        public static async Task<bool> IsLoanOwnedOrNullAsync(this AppDbContext db, Guid userId, Guid? loanId, CancellationToken ct = default) =>
            !loanId.HasValue || await db.Loans.AnyAsync(l => l.Id == loanId.Value && l.UserId == userId, ct);
    }
}
