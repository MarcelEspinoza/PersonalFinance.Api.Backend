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