using MediatR;
using System.Data;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.ConfirmLedgerEntry
{
    public record ConfirmLedgerEntryCommand(Guid UserId, Guid EntryId, ConfirmLedgerEntryDto Payload)
        : IRequest<MonthlyEntryDto>;

    public class ConfirmLedgerEntryCommandHandler : IRequestHandler<ConfirmLedgerEntryCommand, MonthlyEntryDto>
    {
        private readonly AppDbContext _db;
        private readonly IClock _clock;

        public ConfirmLedgerEntryCommandHandler(AppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<MonthlyEntryDto> Handle(ConfirmLedgerEntryCommand request, CancellationToken ct)
        {
            await using var transaction = _db.Database.CurrentTransaction is null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var entry = await LedgerGuards.LoadEditableEntryAsync(
                _db, request.UserId, request.EntryId, ct, allowSettlementAllocation: true, allowZeusCareForecast: true);

            var careAmount = await _db.ZeusCareStays.Where(stay =>
                    stay.UserId == request.UserId && stay.LedgerEntryId == entry.Id)
                .SumAsync(stay => (decimal?)stay.Amount, ct) ?? 0m;
            if (careAmount > 0m && (request.Payload.ActualAmount < careAmount
                || request.Payload.ActualAmount > 999_999_999m
                || decimal.Round(request.Payload.ActualAmount, 2) != request.Payload.ActualAmount
                || (request.Payload.ValueDate ?? _clock.Today) > _clock.Today
                || (request.Payload.ValueDate ?? _clock.Today).Year < 2000))
                throw new BusinessRuleException("El pago debe cubrir los cuidados vinculados y tener una fecha real de hoy o anterior. Revisa primero sus precios en «Zeus» si ha cambiado el acuerdo.");

            if (entry.Status == EntryStatus.Skipped)
                throw new BusinessRuleException("El asiento está descartado. Reactívalo antes de confirmarlo.");
            if (entry.SettlementAllocatedAmount > 0m
                && (entry.Status == EntryStatus.Paid || entry.Remaining <= 0m
                    || request.Payload.ActualAmount > entry.Remaining))
                throw new BusinessRuleException("Solo puedes confirmar la parte que no está incluida en la liquidación.");

            await LedgerGuards.EnsureAccountBelongsToUserAsync(_db, request.UserId, request.Payload.AccountId, ct);

            entry.ActualAmount = request.Payload.ActualAmount;
            entry.ValueDate = request.Payload.ValueDate ?? _clock.Today;
            entry.Status = EntryStatus.Paid;
            entry.UpdatedAt = _clock.UtcNow;

            if (request.Payload.AccountId.HasValue)
                entry.AccountId = request.Payload.AccountId.Value;

            if (careAmount > 0m)
            {
                var account = await _db.Accounts.FirstOrDefaultAsync(account =>
                    account.Id == entry.AccountId && account.UserId == request.UserId && account.IsActive, ct);
                if (account is null || account.OpeningDate > entry.ValueDate)
                    throw new BusinessRuleException("Elige una cuenta activa con inicio contable anterior al pago de Zeus.");
            }

            await _db.SaveChangesAsync(ct);

            entry.Account = entry.AccountId is null
                ? null
                : await _db.Accounts.FirstOrDefaultAsync(a => a.Id == entry.AccountId.Value, ct);

            if (transaction is not null) await transaction.CommitAsync(ct);

            return MonthlySummaryBuilder.MapEntry(entry);
        }
    }
}
