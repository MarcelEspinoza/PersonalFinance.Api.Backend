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
            // Mapear propiedades escalares con AutoMapper (el MappingProfile ignora navegaciones en ReverseMap)
            var loan = _mapper.Map<Loan>(dto);

            // Asegurar Id
            if (loan.Id == Guid.Empty)
                loan.Id = Guid.NewGuid();

            // Resolver Category por Id si el DTO expone CategoryId
            if (dto is not null && dto.GetType().GetProperty("CategoryId") is not null)
            {
                var categoryIdProp = dto.GetType().GetProperty("CategoryId")!;
                var categoryId = (Guid?)categoryIdProp.GetValue(dto);
                if (categoryId.HasValue)
                {
                    var category = await _context.Set<Category>().FindAsync(categoryId.Value);
                    if (category != null) loan.Category = category;
                }
            }

            // Resolver Pasanaco por Id si el DTO expone PasanacoId
            if (dto is not null && dto.GetType().GetProperty("PasanacoId") is not null)
            {
                var pasanacoIdProp = dto.GetType().GetProperty("PasanacoId")!;
                var pasanacoId = (Guid?)pasanacoIdProp.GetValue(dto);
                if (pasanacoId.HasValue)
                {
                    var pasanaco = await _context.Set<Pasanaco>().FindAsync(pasanacoId.Value);
                    if (pasanaco != null) loan.Pasanaco = pasanaco;
                }
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

        public async Task UpdateLoanAsync(LoanDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // Recuperar el préstamo existente con colecciones relevantes
            var loan = await _context.Loans
                .Include(l => l.Payments)
                .FirstOrDefaultAsync(l => l.Id == dto.Id);

            if (loan == null)
                throw new KeyNotFoundException($"Loan with Id '{dto.Id}' not found.");

            // Mapear propiedades escalares del DTO sobre la entidad existente.
            // Asumimos que el MappingProfile actualiza sólo propiedades escalares y no navegaciones complejas.
            _mapper.Map(dto, loan);

            // Reafirmar el Id por seguridad
            loan.Id = dto.Id;

            // Resolver Category por Id si el DTO expone CategoryId (mismo enfoque que en CreateLoanAsync)
            if (dto is not null && dto.GetType().GetProperty("CategoryId") is not null)
            {
                var categoryIdProp = dto.GetType().GetProperty("CategoryId")!;
                var categoryId = (Guid?)categoryIdProp.GetValue(dto);
                if (categoryId.HasValue)
                {
                    var category = await _context.Set<Category>().FindAsync(categoryId.Value);
                    loan.Category = category; // puede ser null si no se encuentra
                }
                else
                {
                    loan.Category = null;
                }
            }

            // Resolver Pasanaco por Id si el DTO expone PasanacoId
            if (dto is not null && dto.GetType().GetProperty("PasanacoId") is not null)
            {
                var pasanacoIdProp = dto.GetType().GetProperty("PasanacoId")!;
                var pasanacoId = (Guid?)pasanacoIdProp.GetValue(dto);
                if (pasanacoId.HasValue)
                {
                    var pasanaco = await _context.Set<Pasanaco>().FindAsync(pasanacoId.Value);
                    loan.Pasanaco = pasanaco; // puede ser null si no se encuentra
                }
                else
                {
                    loan.Pasanaco = null;
                }
            }

            // Nota: no se manipulan explicitamente las colecciones Payments aquí para evitar problemas
            // de sincronización sin una especificación clara de cómo deben actualizarse.

            // Guardar cambios
            await _context.SaveChangesAsync();
        }
    }
}
