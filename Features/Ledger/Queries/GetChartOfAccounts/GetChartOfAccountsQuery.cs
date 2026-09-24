using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Dtos;

namespace PersonalFinance.Api.Features.Ledger.Queries.GetChartOfAccounts
{
    /// <summary>Grupos y conceptos del usuario. Es lo que alimenta los desplegables del frontend.</summary>
    public record GetChartOfAccountsQuery(Guid UserId, bool IncludeInactive = false)
        : IRequest<ChartOfAccountsDto>;

    public class GetChartOfAccountsQueryHandler : IRequestHandler<GetChartOfAccountsQuery, ChartOfAccountsDto>
    {
        private readonly IAppDbContext _db;

        public GetChartOfAccountsQueryHandler(IAppDbContext db) => _db = db;

        public async Task<ChartOfAccountsDto> Handle(GetChartOfAccountsQuery request, CancellationToken ct)
        {
            var groups = await _db.ConceptGroups
                .AsNoTracking()
                .Where(g => g.UserId == request.UserId && (request.IncludeInactive || g.IsActive))
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Name)
                .ToListAsync(ct);

            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(c => c.UserId == request.UserId && (request.IncludeInactive || c.IsActive))
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync(ct);

            var byGroup = concepts.ToLookup(c => c.GroupId);

            return new ChartOfAccountsDto
            {
                Groups = groups.Select(g => new ChartGroupDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Kind = g.Kind,
                    SortOrder = g.SortOrder,
                    IsActive = g.IsActive,
                    Concepts = byGroup[g.Id].Select(c => new ChartConceptDto
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Nature = c.Nature,
                        SortOrder = c.SortOrder,
                        IsActive = c.IsActive
                    }).ToList()
                }).ToList()
            };
        }
    }
}
