using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Settlements
{
    public static class SettlementEntryCoverage
    {
        public static async Task AllocateAsync(
            IAppDbContext db,
            Guid userId,
            SettlementLine line,
            CancellationToken ct,
            bool allowPassthrough = false)
        {
            if (line.LedgerEntryId is not { } entryId)
            {
                line.AllocatesPendingEntry = false;
                return;
            }

            var entry = await db.LedgerEntries
                .Include(entry => entry.Period)
                .FirstOrDefaultAsync(entry => entry.Id == entryId && entry.UserId == userId, ct)
                ?? throw new NotFoundException("No encuentro ese movimiento.");

            if (entry.Status == EntryStatus.Paid && !line.AllocatesPendingEntry) return;
            if (entry.Status == EntryStatus.Skipped ||
                entry.IsTransfer && !(allowPassthrough || line.AllocatesPendingEntry))
                throw new BusinessRuleException("Ese movimiento no se puede incluir como importe pendiente.");
            if (entry.IsTransfer && await db.Accounts.AnyAsync(account =>
                account.UserId == userId && account.TransferConceptId == entry.ConceptId, ct))
                throw new BusinessRuleException("Un traspaso entre cuentas propias no se compensa en una liquidación.");
            if (entry.Period?.Status == PeriodStatus.Closed)
                throw new BusinessRuleException("El mes del movimiento está cerrado.");

            var expectedKind = entry.Direction == EntryDirection.In
                ? SettlementLineKind.Charge
                : SettlementLineKind.Deduction;
            if (entry.IsTransfer
                ? line.Kind is not (SettlementLineKind.Charge or SettlementLineKind.Deduction)
                : line.Kind != expectedKind)
                throw new BusinessRuleException(
                    entry.Direction == EntryDirection.In
                        ? "Un ingreso pendiente se incluye como importe que te deben, no como dinero ya recibido."
                        : "Un gasto pendiente se incluye en «A restar», no como dinero ya pagado.");

            var otherAllocations = await db.SettlementLines
                .Where(other =>
                    other.Settlement!.UserId == userId
                    && other.LedgerEntryId == entryId
                    && other.Id != line.Id
                    && other.AllocatesPendingEntry)
                .SumAsync(other => (decimal?)other.Amount, ct) ?? 0m;
            var available = entry.ForecastAmount - (entry.ActualAmount ?? 0m) - otherAllocations;
            if (line.Amount <= 0m || line.Amount > available)
                throw new BusinessRuleException("El importe supera la parte pendiente disponible del movimiento.");

            line.AllocatesPendingEntry = true;
        }

        public static async Task RefreshAsync(
            IAppDbContext db,
            Settlement settlement,
            CancellationToken ct,
            IEnumerable<Guid>? previousEntryIds = null)
        {
            var entryIds = settlement.Lines
                .Where(line => line.LedgerEntryId != null)
                .Select(line => line.LedgerEntryId!.Value)
                .Concat(previousEntryIds ?? Array.Empty<Guid>())
                .Distinct()
                .ToList();
            if (entryIds.Count == 0) return;

            var otherLines = await db.SettlementLines
                .Include(line => line.Settlement)
                .Where(line =>
                    line.Settlement!.UserId == settlement.UserId
                    && line.SettlementId != settlement.Id
                    && line.LedgerEntryId != null
                    && entryIds.Contains(line.LedgerEntryId.Value)
                    && line.AllocatesPendingEntry)
                .ToListAsync(ct);
            var allocations = otherLines.Select(line => new
                {
                    EntryId = line.LedgerEntryId!.Value,
                    line.Amount,
                    Closed = line.Settlement!.Status == SettlementStatus.Closed
                })
                .ToList();
            allocations.AddRange(settlement.Lines
                .Where(line => line.AllocatesPendingEntry && line.LedgerEntryId != null)
                .Select(line => new
                {
                    EntryId = line.LedgerEntryId!.Value,
                    line.Amount,
                    Closed = settlement.Status == SettlementStatus.Closed
                }));

            var entries = await db.LedgerEntries
                .Where(entry => entry.UserId == settlement.UserId && entryIds.Contains(entry.Id))
                .ToListAsync(ct);
            foreach (var entry in entries)
            {
                entry.SettlementAllocatedAmount = allocations
                    .Where(line => line.EntryId == entry.Id)
                    .Sum(line => line.Amount);
                entry.SettlementSettledAmount = allocations
                    .Where(line => line.EntryId == entry.Id && line.Closed)
                    .Sum(line => line.Amount);
            }
        }
    }
}
