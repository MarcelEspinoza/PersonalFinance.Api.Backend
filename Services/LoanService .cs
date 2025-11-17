using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

        public async Task<Loan?> GetLoanAsync(Guid id) =>
            await _context.Loans.Include(l => l.Payments).FirstOrDefaultAsync(l => l.Id == id);

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
                var category = await _context.Categories.FindAsync(dto.CategoryId);
                if (category != null) loan.Category = category;
            }

            // Resolver Pasanaco por Id si el DTO incluye uno (PasanacoId es string? en tu DTO actual)
            if (!string.IsNullOrWhiteSpace(dto.PasanacoId))
            {
                var pasanaco = await _context.Pasanacos.FindAsync(dto.PasanacoId);
                if (pasanaco != null) loan.Pasanaco = pasanaco;
            }

            _context.Loans.Add(loan);
            await _context.SaveChangesAsync();
            return loan;
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

        // Nota: actualicé la firma para aceptar el id de la ruta más explícitamente
        public async Task UpdateLoanAsync(Guid id, LoanDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // Recuperar el préstamo existente con colecciones relevantes
            var loan = await _context.Loans
                .Include(l => l.Payments)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (loan == null)
                throw new KeyNotFoundException($"Loan with Id '{id}' not found.");

            // Mapear propiedades escalares del DTO sobre la entidad existente.
            // Asumimos que el MappingProfile actualiza sólo propiedades escalares y no navegaciones complejas.
            _mapper.Map(dto, loan);

            // Reafirmar el Id desde la ruta (no confiar en que el cliente pase el id en el body)
            loan.Id = id;

            // CategoryId: si el DTO contiene un id > 0 lo resolvemos; si viene 0 (o falta), lo limpiamos.
            if (dto.CategoryId > 0)
            {
                var category = await _context.Categories.FindAsync(dto.CategoryId);
                loan.Category = category; // puede quedar null si no existe la categoría
            }
            else
            {
                // Si el cliente envía 0 explícito o no quiere categoría, limpiamos la relación.
                loan.Category = null;
            }

            // PasanacoId: si el DTO contiene cadena no vacía la resolvemos, si viene null/empty limpiamos.
            if (!string.IsNullOrWhiteSpace(dto.PasanacoId))
            {
                var pasanaco = await _context.Pasanacos.FindAsync(dto.PasanacoId);
                loan.Pasanaco = pasanaco; // puede quedar null si no existe el pasanaco
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
    }
}