using System.Globalization;
using System.Linq.Expressions;
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
            !entry.IsTransfer;

        public static bool IsOpen(LedgerEntry entry) =>
            entry.Status is EntryStatus.Planned or EntryStatus.Pending &&
            entry.ImportRowId == null &&
            !entry.IsTransfer;

        public static EntryDirection DirectionOf(decimal amount) =>
            amount >= 0 ? EntryDirection.In : EntryDirection.Out;

        public static string DirectionText(EntryDirection direction) =>
            direction == EntryDirection.In ? "in" : "out";

        /// <summary>Linked rows that count for the deduction: pending, not split and entering as paid.</summary>
        public static bool Deducts(ImportRow row) =>
            row.PlannedEntryId is not null &&
            row.Status == ImportRowStatus.Pending &&
            row.Allocations.Count == 0 &&
            ImportBatchApplicationService.EntersAsPaid(row);

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
