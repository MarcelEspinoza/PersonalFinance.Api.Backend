using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.CreateOwnTransfer
{
    public record CreateOwnTransferCommand(Guid UserId, OwnTransferDto Payload) : IRequest<IReadOnlyList<MonthlyEntryDto>>;

    public class CreateOwnTransferCommandHandler : IRequestHandler<CreateOwnTransferCommand, IReadOnlyList<MonthlyEntryDto>>
    {
        private readonly IAppDbContext _db;
        private readonly PeriodProvisioner _provisioner;

        public CreateOwnTransferCommandHandler(IAppDbContext db, PeriodProvisioner provisioner)
        {
            _db = db;
            _provisioner = provisioner;
        }

        public async Task<IReadOnlyList<MonthlyEntryDto>> Handle(CreateOwnTransferCommand request, CancellationToken ct)
        {
            var dto = request.Payload;

            if (dto.FromAccountId == dto.ToAccountId)
                throw new BusinessRuleException("Elige dos cuentas distintas.");
            if (dto.Amount <= 0)
                throw new BusinessRuleException("El importe debe ser mayor que cero.");

            var concept = await LedgerGuards.EnsureConceptBelongsToUserAsync(_db, request.UserId, dto.ConceptId, ct);
            if (concept.Kind != ConceptKind.Transfer)
                throw new BusinessRuleException("Elige un concepto de «Traspasos» para que no cuente como gasto ni ingreso.");

            var accounts = await _db.Accounts
                .Where(a => a.UserId == request.UserId && (a.Id == dto.FromAccountId || a.Id == dto.ToAccountId))
                .ToListAsync(ct);
            var from = accounts.FirstOrDefault(a => a.Id == dto.FromAccountId)
                ?? throw new BusinessRuleException("La cuenta de origen no existe o no pertenece al usuario.");
            var to = accounts.FirstOrDefault(a => a.Id == dto.ToAccountId)
                ?? throw new BusinessRuleException("La cuenta de destino no existe o no pertenece al usuario.");

            var period = await _provisioner.GetOrOpenAsync(request.UserId, dto.Date.Year, dto.Date.Month, ct);
            if (period.Status == PeriodStatus.Closed)
                throw new BusinessRuleException(
                    $"El periodo {period.Year}-{period.Month:D2} está cerrado. Reábrelo para añadir asientos.");

            var description = string.IsNullOrWhiteSpace(dto.Description)
                ? $"Traspaso de {from.Name} a {to.Name}"
                : dto.Description.Trim();

            var outgoing = NewEntry(request.UserId, period.Id, dto, from, EntryDirection.Out, description);
            _db.LedgerEntries.Add(outgoing);
            if (dto.SourceOnly)
            {
                await _db.SaveChangesAsync(ct);
                return [MonthlySummaryBuilder.MapEntry(outgoing)];
            }

            var incoming = NewEntry(request.UserId, period.Id, dto, to, EntryDirection.In, description);
            _db.LedgerEntries.Add(incoming);
            await _db.SaveChangesAsync(ct);

            return [MonthlySummaryBuilder.MapEntry(outgoing), MonthlySummaryBuilder.MapEntry(incoming)];
        }

        private static LedgerEntry NewEntry(
            Guid userId, Guid periodId, OwnTransferDto dto, Account account, EntryDirection direction, string description) => new()
        {
            UserId = userId,
            PeriodId = periodId,
            ConceptId = dto.ConceptId,
            AccountId = account.Id,
            Account = account,
            Direction = direction,
            IsTransfer = true,
            DueDate = dto.Date,
            ForecastAmount = dto.Amount,
            ActualAmount = dto.Amount,
            ValueDate = dto.Date,
            Description = description,
            Status = EntryStatus.Paid
        };
    }
}
