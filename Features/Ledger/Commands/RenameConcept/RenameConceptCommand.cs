using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.RenameConcept
{
    public record RenameConceptCommand(Guid UserId, Guid ConceptId, string? Name) : IRequest<ChartConceptDto>;

    /// <summary>
    /// Cambia el nombre de un concepto. Las reglas y las previsiones que aún no
    /// se han pagado se llevan el nombre nuevo; lo ya confirmado o descartado
    /// se queda como está, porque es historia.
    /// </summary>
    public class RenameConceptCommandHandler : IRequestHandler<RenameConceptCommand, ChartConceptDto>
    {
        private const int MaxNameLength = 120;
        private readonly IAppDbContext _db;

        public RenameConceptCommandHandler(IAppDbContext db) => _db = db;

        public async Task<ChartConceptDto> Handle(RenameConceptCommand request, CancellationToken ct)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new BusinessRuleException("El nombre del concepto no puede quedar vacío.");
            if (name.Length > MaxNameLength)
                throw new BusinessRuleException($"El nombre del concepto admite como máximo {MaxNameLength} caracteres.");

            var concept = await _db.Concepts
                .FirstOrDefaultAsync(c => c.Id == request.ConceptId && c.UserId == request.UserId, ct)
                ?? throw new NotFoundException("El concepto no existe.");

            var oldName = concept.Name;
            if (oldName != name)
            {
                var lowered = name.ToLower();
                var taken = await _db.Concepts.AnyAsync(c =>
                    c.UserId == request.UserId &&
                    c.GroupId == concept.GroupId &&
                    c.Id != concept.Id &&
                    c.Name.ToLower() == lowered, ct);
                if (taken)
                    throw new BusinessRuleException("Ya hay otro concepto con ese nombre en el mismo grupo.");

                concept.Name = name;

                var rules = await _db.RecurringRules
                    .Where(r => r.UserId == request.UserId && r.ConceptId == concept.Id && r.Description == oldName)
                    .ToListAsync(ct);
                foreach (var rule in rules) rule.Description = name;

                var forecasts = await _db.LedgerEntries
                    .Where(e =>
                        e.UserId == request.UserId &&
                        e.ConceptId == concept.Id &&
                        e.Description == oldName &&
                        (e.Status == EntryStatus.Planned || e.Status == EntryStatus.Pending))
                    .ToListAsync(ct);
                foreach (var entry in forecasts) entry.Description = name;

                await _db.SaveChangesAsync(ct);
            }

            return new ChartConceptDto
            {
                Id = concept.Id,
                Name = concept.Name,
                Nature = concept.Nature,
                SortOrder = concept.SortOrder,
                IsActive = concept.IsActive
            };
        }
    }
}
