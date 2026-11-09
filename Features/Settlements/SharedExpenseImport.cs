using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Settlements
{
    public static class SharedExpenseImport
    {
        public const string ConceptName = "Anticipos y devoluciones de gastos compartidos";

        public static async Task<Concept> GetOrCreateConceptAsync(IAppDbContext db, Guid userId, CancellationToken ct)
        {
            var concept = await db.Concepts.FirstOrDefaultAsync(item =>
                item.UserId == userId && item.Kind == ConceptKind.Transfer && item.Name == ConceptName, ct);
            if (concept is not null)
            {
                if (!concept.IsActive || await db.Accounts.AnyAsync(account =>
                    account.UserId == userId && account.TransferConceptId == concept.Id, ct))
                    throw new BusinessRuleException("La categoría de gastos compartidos debe estar activa y no representar una cuenta propia.");
                return concept;
            }
            var group = await db.ConceptGroups.FirstOrDefaultAsync(item =>
                item.UserId == userId && item.Kind == ConceptKind.Transfer && item.Name == "Traspasos", ct);
            if (group is null)
            {
                group = new ConceptGroup { UserId = userId, Name = "Traspasos", Kind = ConceptKind.Transfer, SortOrder = 90 };
                db.ConceptGroups.Add(group);
            }
            if (!group.IsActive) throw new BusinessRuleException("Activa el grupo Traspasos antes de registrar un gasto compartido.");
            concept = new Concept
            {
                UserId = userId, GroupId = group.Id, Name = ConceptName,
                Kind = ConceptKind.Transfer, Nature = ConceptNature.Variable
            };
            db.Concepts.Add(concept);
            return concept;
        }

        public static async Task AddAdvanceAsync(
            IAppDbContext db, Guid userId, Guid personId, LedgerEntry advance, decimal fullAmount,
            DateTime now, CancellationToken ct)
        {
            if (advance.UserId != userId || advance.Status != EntryStatus.Paid || !advance.IsTransfer ||
                advance.Direction != EntryDirection.Out || advance.ActualAmount is not { } amount ||
                amount <= 0m || amount >= fullAmount)
                throw new BusinessRuleException("El adelanto debe ser una parte de un gasto pagado.");
            var start = new DateOnly(advance.DueDate.Year, advance.DueDate.Month, 1);
            var title = $"Gastos compartidos {start:MM/yyyy}";
            var settlement = db.Settlements.Local.Where(item =>
                item.UserId == userId && item.CounterpartyId == personId && item.IsExpenseReimbursement &&
                item.Status == SettlementStatus.Draft && item.PeriodStart == start)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefault()
                ?? await db.Settlements.Include(item => item.Lines)
                .Where(item => item.UserId == userId && item.CounterpartyId == personId &&
                    item.IsExpenseReimbursement && item.Status == SettlementStatus.Draft && item.PeriodStart == start)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(ct);
            settlement ??= CreateSettlement(db, userId, personId, start, title, now);
            var line = new SettlementLine
            {
                SettlementId = settlement.Id, Kind = SettlementLineKind.Charge,
                Description = SettlementService.CleanDescription(advance.Description),
                Amount = amount, FullAmount = fullAmount,
                LedgerEntryId = advance.Id, CreatedAt = now,
                SortOrder = settlement.Lines.Count
            };
            settlement.Lines.Add(line);
            db.SettlementLines.Add(line);
            settlement.UpdatedAt = now;
        }

        public static async Task ClassifyPaymentAsync(
            IAppDbContext db, Guid userId, Settlement settlement, LedgerEntry entry, CancellationToken ct)
        {
            if (!settlement.IsExpenseReimbursement) return;
            var concept = await GetOrCreateConceptAsync(db, userId, ct);
            entry.ConceptId = concept.Id;
            entry.Concept = concept;
            entry.IsTransfer = true;
        }

        private static Settlement CreateSettlement(
            IAppDbContext db, Guid userId, Guid personId, DateOnly start, string title, DateTime now)
        {
            var settlement = new Settlement
            {
                UserId = userId, CounterpartyId = personId, Title = title, IsExpenseReimbursement = true,
                PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), CreatedAt = now
            };
            db.Settlements.Add(settlement);
            return settlement;
        }
    }
}
