using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Rules shared by the review endpoints, the analyzer, the import chat and the apply step
    /// for deducting imported rows from a planned entry («previsto»).
    /// </summary>
    public static class ImportPlannedLinks
    {
        public const int CandidateWindowDays = 31;
        public const decimal CoverTolerance = 0.005m;
        private const int MaxNotesLength = 500;

        private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>
        /// Still-expected planned entry: overdue ones turn Pending, entries created from a bank row
        /// (ImportRowId) are not previstos, and transfers between own accounts are excluded.
        /// </summary>
        public static readonly Expression<Func<LedgerEntry, bool>> IsOpenExpression = entry =>
            (entry.Status == EntryStatus.Planned || entry.Status == EntryStatus.Pending) &&
            entry.ImportRowId == null &&
            entry.SettlementAllocatedAmount == 0m &&
            !entry.IsTransfer;

        public static bool IsOpen(LedgerEntry entry) =>
            entry.Status is EntryStatus.Planned or EntryStatus.Pending &&
            entry.ImportRowId == null &&
            entry.SettlementAllocatedAmount == 0m &&
            !entry.IsTransfer;

        public static EntryDirection DirectionOf(decimal amount) =>
            amount >= 0 ? EntryDirection.In : EntryDirection.Out;

        public static string DirectionText(EntryDirection direction) =>
            direction == EntryDirection.In ? "in" : "out";

        /// <summary>Linked rows that count for the deduction: pending, not split and entering as paid.</summary>
        public static bool Deducts(ImportRow row) =>
            HasLink(row) &&
            row.Status == ImportRowStatus.Pending &&
            row.Allocations.Count == 0 &&
            ImportBatchApplicationService.EntersAsPaid(row);

        public static bool HasLink(ImportRow row) => row.PlannedEntryId is not null || row.PlannedAllocations.Count > 0;

        public static IEnumerable<(Guid PlannedEntryId, decimal Amount)> Links(ImportRow row) =>
            row.PlannedAllocations.Count > 0
                ? row.PlannedAllocations.Select(link => (link.PlannedEntryId, link.Amount))
                : row.PlannedEntryId is { } id
                    ? new[] { (id, Math.Abs(row.Amount)) }
                    : Array.Empty<(Guid, decimal)>();

        public static bool IsValidLinks(ImportRow row, IReadOnlyDictionary<Guid, LedgerEntry> entries) =>
            Deducts(row) &&
            Links(row).All(link => entries.TryGetValue(link.PlannedEntryId, out var entry) &&
                IsOpen(entry) && LinkError(row.Amount, false, entry.Direction) is null) &&
            (row.PlannedAllocations.Count == 0 || AllocationError(row, Links(row).ToList(), entries) is null);

        public static string? AllocationError(
            ImportRow row,
            IReadOnlyList<(Guid PlannedEntryId, decimal Amount)> links,
            IReadOnlyDictionary<Guid, LedgerEntry> entries)
        {
            if (row.Allocations.Count > 0) return "El movimiento ya está dividido en categorías; quita esa división antes de repartirlo entre previstos.";
            if (row.Amount == 0m) return "No se puede repartir un movimiento de importe cero.";
            if (!ImportBatchApplicationService.EntersAsPaid(row))
                return "Solo se puede repartir un movimiento que se registrará como pagado.";
            if (links.Count == 0) return "Selecciona al menos un previsto.";
            if (links.Any(link => link.Amount <= 0m || decimal.Round(link.Amount, 2) != link.Amount))
                return "Cada parte debe tener un importe positivo con un máximo de dos decimales.";
            if (links.Any(link => link.Amount > Math.Abs(row.Amount)))
                return "Ninguna parte puede superar el importe del movimiento.";
            if (links.Select(link => link.PlannedEntryId).Distinct().Count() != links.Count)
                return "Selecciona un previsto distinto para cada parte.";
            if (links.Sum(link => link.Amount) != Math.Abs(row.Amount))
                return "La suma del reparto debe coincidir exactamente con el importe del movimiento.";
            foreach (var link in links)
            {
                if (!entries.TryGetValue(link.PlannedEntryId, out var entry) || !IsOpen(entry))
                    return "Algún previsto ya no está disponible. Actualiza la revisión.";
                if (entry.Concept is null || !entry.Concept.IsActive)
                    return "Algún previsto tiene una categoría inactiva.";
                var error = LinkError(row.Amount, false, entry.Direction);
                if (error is not null) return error;
                if (entry.Period?.Status == PeriodStatus.Closed)
                    return "No se puede repartir el pago entre previstos de un periodo cerrado.";
            }
            if (links.Select(link => entries[link.PlannedEntryId].ConceptId).Distinct().Count() != 1)
                return "Los previstos del reparto deben tener la misma categoría. Para categorías distintas, divide el movimiento en categorías.";
            return null;
        }

        public static async Task<string?> CareAllocationErrorAsync(
            IAppDbContext db, Guid userId, IReadOnlyList<(Guid PlannedEntryId, decimal Amount)> allocations,
            IReadOnlyDictionary<Guid, LedgerEntry> entries, CancellationToken ct)
        {
            var links = allocations.ToDictionary(link => link.PlannedEntryId, link => link.Amount);
            var ids = links.Keys.ToList();
            if (await db.ZeusHourPasses.AnyAsync(pass =>
                pass.UserId == userId && ids.Contains(pass.LedgerEntryId), ct))
                return "Una previsión compra un bono de Zeus y no se puede repartir como un pago de cuidados.";
            var stays = await db.ZeusCareStays.AsNoTracking()
                .Where(stay => stay.UserId == userId && stay.LedgerEntryId.HasValue && ids.Contains(stay.LedgerEntryId.Value))
                .ToListAsync(ct);
            if (stays.Select(stay => stay.Provider).Distinct().Count() > 1)
                return "Un pago de cuidados no puede cubrir a Alessia y a la guardería a la vez.";
            foreach (var group in stays.GroupBy(stay => stay.LedgerEntryId!.Value))
            {
                var planned = entries[group.Key];
                if (planned.RecurringRuleId.HasValue || links[group.Key] != planned.ForecastAmount ||
                    group.Sum(stay => stay.Amount) > links[group.Key])
                    return "El reparto debe cubrir completamente cada previsión de cuidados y el precio de sus estancias.";
            }
            return null;
        }
        /// <summary>Whether the row can still be deducted from the entry when the batch is applied.</summary>
        public static bool IsValidLink(ImportRow row, LedgerEntry? entry) =>
            entry is not null &&
            Deducts(row) &&
            IsOpen(entry) &&
            LinkError(row.Amount, false, entry.Direction) is null;

        /// <summary>Why a row cannot be deducted from a planned entry (Spanish), or null when it can.</summary>
        public static string? LinkError(decimal rowAmount, bool isSplit, EntryDirection plannedDirection)
        {
            if (isSplit) return "Este movimiento está dividido; no se puede descontar de un previsto.";
            if (rowAmount == 0m) return "Este movimiento no tiene importe; no se puede descontar de un previsto.";

            var rowDirection = DirectionOf(rowAmount);
            if (rowDirection == plannedDirection) return null;
            return rowDirection == EntryDirection.Out
                ? "Este movimiento es un gasto y el previsto es un ingreso; no se puede descontar de él."
                : "Este movimiento es un ingreso y el previsto es un gasto; no se puede descontar de él.";
        }

        public static string Money(decimal amount) => amount.ToString("N2", Spanish) + " €";

        public static string Movements(int count) =>
            count == 1 ? "1 movimiento importado" : $"{count} movimientos importados";

        /// <summary>Appends a note keeping the new text whole and trimming the oldest text to fit.</summary>
        public static string AppendNote(string? notes, string note)
        {
            var current = notes?.Trim();
            if (string.IsNullOrEmpty(current)) return note.Length <= MaxNotesLength ? note : note[..MaxNotesLength];

            var room = MaxNotesLength - note.Length - 1;
            if (room <= 1) return note.Length <= MaxNotesLength ? note : note[..MaxNotesLength];
            if (current.Length > room) current = current[..(room - 1)].TrimEnd() + "…";
            return current + " " + note;
        }
    }
}
