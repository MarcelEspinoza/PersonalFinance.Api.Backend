using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Api.Models.Enums;

namespace PersonalFinance.Api.Services
{
    public class LoanService : ILoanService
    {
        private readonly AppDbContext _context;
        private readonly AutoMapper.IMapper _mapper;

        public LoanService(AppDbContext context, AutoMapper.IMapper mapper)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public async Task<IEnumerable<Loan>> GetLoansAsync(Guid userId) =>
            await _context.Loans.Where(l => l.UserId == userId).ToListAsync();

        public async Task<Loan?> GetLoanAsync(Guid id, Guid userId) =>
            await _context.Loans.Include(l => l.Payments).FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);

        public async Task<bool> SetArchivedAsync(
            Guid userId, IReadOnlyCollection<Guid> loanIds, bool archived, CancellationToken ct)
        {
            var ids = loanIds.Distinct().ToArray();
            var loans = await _context.Loans
                .Where(loan => loan.UserId == userId && ids.Contains(loan.Id))
                .ToListAsync(ct);
            if (loans.Count != ids.Length) return false;
            var now = DateTimeOffset.UtcNow;
            foreach (var loan in loans)
                loan.ArchivedAt = archived ? loan.ArchivedAt ?? now : null;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<LoanOverviewDto> GetOverviewAsync(
            Guid userId, int year, int month, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var loans = await _context.Loans.AsNoTracking()
                .Where(loan => loan.UserId == userId).ToListAsync(ct);
            var items = new List<LoanOverviewItemDto>();
            foreach (var loan in loans.Where(loan => loan.ArchivedAt is null &&
                !loan.Status.Equals("paid", StringComparison.OrdinalIgnoreCase) &&
                !loan.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) &&
                loan.OutstandingAmount > 0m))
            {
                var bank = loan.Type == LoanType.Bank;
                var next = bank ? loan.NextPaymentDate : loan.DueDate;
                var nextDate = next.HasValue ? DateOnly.FromDateTime(next.Value.DateTime) : (DateOnly?)null;
                var completion = loan.DueDate.HasValue
                    ? DateOnly.FromDateTime(loan.DueDate.Value.DateTime) : (DateOnly?)null;
                if (completion < today) completion = null;
                items.Add(new(
                    loan.Id, "loan", loan.Name, loan.Type == LoanType.Given ? "in" : "out",
                    loan.OutstandingAmount, nextDate,
                    bank ? loan.NextPaymentAmount : loan.OutstandingAmount,
                    completion, null, null));
            }

            var settlements = await _context.Settlements.AsNoTracking()
                .Include(settlement => settlement.Lines)
                .Where(settlement => settlement.UserId == userId).ToListAsync(ct);
            var names = await _context.Counterparties.AsNoTracking()
                .Where(person => person.UserId == userId && person.IsActive)
                .ToDictionaryAsync(person => person.Id, person => person.Name, ct);
            var forecast = SettlementForecast.Evaluate(settlements, names)
                .ToDictionary(state => state.SettlementId);
            foreach (var group in settlements.GroupBy(settlement => settlement.CounterpartyId))
            {
                if (!names.TryGetValue(group.Key, out var name)) continue;
                foreach (var settlement in SettlementForecast.Open(group))
                {
                    var remaining = SettlementForecast.Remaining(settlement);
                    var expected = forecast[settlement.Id].Expectation;
                    var completion = settlement.ExpectedDate >= today ? settlement.ExpectedDate : null;
                    items.Add(new(
                        settlement.Id, "settlement", settlement.Title, remaining > 0m ? "in" : "out",
                        Math.Abs(remaining), expected?.Date, expected?.Amount,
                        completion, name, null));
                }
            }

            bool InMonth(LoanOverviewItemDto item) =>
                item.NextDate is { } date && date.Year == year && date.Month == month;
            var payable = items.Where(item => item.Direction == "out").ToList();
            var missingDates = payable.Count(item => item.CompletionDate is null ||
                item.NextDate is null || item.NextAmount is not > 0m || item.NextDate < today);
            var lastDate = payable.Count > 0 && missingDates == 0
                ? payable.Max(item => item.CompletionDate) : null;
            var archived = loans.Where(loan => loan.ArchivedAt.HasValue).ToList();
            return new(
                payable.Sum(item => item.RemainingAmount),
                items.Where(item => item.Direction == "in").Sum(item => item.RemainingAmount),
                payable.Where(InMonth).Sum(item => item.NextAmount ?? 0m),
                items.Where(item => item.Direction == "in").Where(InMonth).Sum(item => item.NextAmount ?? 0m),
                lastDate, missingDates, archived.Count, archived.Sum(loan => loan.OutstandingAmount),
                items.OrderBy(item => item.NextDate is null).ThenBy(item => item.NextDate)
                    .ThenBy(item => item.Title).ToList());
        }

        // El propietario es dto.UserId: el controlador lo fija desde el token y PasanacoService
        // con el propietario ya verificado del pasanaco.
        public async Task<Loan> CreateLoanAsync(LoanDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // Mapear propiedades escalares con AutoMapper (el MappingProfile ignora navegaciones en ReverseMap)
            var loan = _mapper.Map<Loan>(dto);

            // Asegurar Id
            if (loan.Id == Guid.Empty)
                loan.Id = Guid.NewGuid();

            // Resolver Category por Id si el DTO incluye uno válido (> 0)
            // Nota: LoanDto.CategoryId es int en tu DTO actual.
            if (dto.CategoryId > 0)
            {
                loan.Category = await GetVisibleCategoryAsync(dto.CategoryId, dto.UserId);
            }

            // Resolver Pasanaco por Id si el DTO incluye uno (PasanacoId es string? en tu DTO actual)
            if (!string.IsNullOrWhiteSpace(dto.PasanacoId))
            {
                loan.Pasanaco = await GetOwnedPasanacoAsync(dto.PasanacoId, dto.UserId);
            }

            _context.Loans.Add(loan);
            await _context.SaveChangesAsync();
            return loan;
        }

        public async Task DeleteLoanAsync(Guid id, Guid userId)
        {
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);
            if (loan != null)
            {
                _context.Loans.Remove(loan);
                await _context.SaveChangesAsync();
            }
        }

        // Nota: actualicé la firma para aceptar el id de la ruta más explícitamente
        public async Task UpdateLoanAsync(Guid id, Guid userId, LoanDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // Recuperar el préstamo existente con colecciones relevantes
            var loan = await _context.Loans
                .Include(l => l.Payments)
                .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);

            if (loan == null)
                throw new NotFoundException("Préstamo no encontrado");

            // Mapear propiedades escalares del DTO sobre la entidad existente.
            // Asumimos que el MappingProfile actualiza sólo propiedades escalares y no navegaciones complejas.
            _mapper.Map(dto, loan);

            // Reafirmar el Id desde la ruta (no confiar en que el cliente pase el id en el body)
            loan.Id = id;
            // El propietario nunca cambia desde el body
            loan.UserId = userId;

            // CategoryId: si el DTO contiene un id > 0 lo resolvemos; si viene 0 (o falta), lo limpiamos.
            if (dto.CategoryId > 0)
            {
                loan.Category = await GetVisibleCategoryAsync(dto.CategoryId, userId);
            }
            else
            {
                // Si el cliente envía 0 explícito o no quiere categoría, limpiamos la relación.
                loan.Category = null;
            }

            // PasanacoId: si el DTO contiene cadena no vacía la resolvemos, si viene null/empty limpiamos.
            if (!string.IsNullOrWhiteSpace(dto.PasanacoId))
            {
                loan.Pasanaco = await GetOwnedPasanacoAsync(dto.PasanacoId, userId);
            }
            else
            {
                loan.Pasanaco = null;
            }

            // Nota: no se manipulan explícitamente las colecciones Payments aquí para evitar problemas
            // de sincronización sin una especificación clara de cómo deben actualizarse.

            // Guardar cambios
            await _context.SaveChangesAsync();
        }

        private async Task<Category> GetVisibleCategoryAsync(int categoryId, Guid userId) =>
            await _context.Categories.FirstOrDefaultAsync(c => c.Id == categoryId && (c.IsSystem || c.UserId == userId))
                ?? throw new NotFoundException("Categoría no encontrada");

        private async Task<Pasanaco> GetOwnedPasanacoAsync(string pasanacoId, Guid userId) =>
            await _context.Pasanacos.FirstOrDefaultAsync(p => p.Id == pasanacoId && p.UserId == userId)
                ?? throw new NotFoundException("Pasanaco no encontrado");
    }
}