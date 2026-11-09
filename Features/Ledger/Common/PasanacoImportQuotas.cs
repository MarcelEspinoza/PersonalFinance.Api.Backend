using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Domain.Ledger.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>Monthly quota that one participant owes to a pasanaco.</summary>
    public sealed record PasanacoQuota(
        string PasanacoName,
        string ParticipantId,
        string ParticipantName,
        DateOnly Month,
        decimal Amount,
        string? Recipient);

    /// <summary>Active pasanaco with the quotas that are still unpaid up to next month.</summary>
    public sealed record PasanacoImportSummary(
        string Name,
        decimal MonthlyAmount,
        int TotalParticipants,
        int CurrentRound,
        DateOnly CurrentMonth,
        string? CurrentRecipient,
        IReadOnlyList<PasanacoQuota> PendingQuotas);

    /// <summary>
    /// Links an income row of an import to the monthly quota of a pasanaco participant.
    /// The quota is marked as paid when the batch is applied; the row still books its own
    /// ledger entry, so no extra income is created.
    /// </summary>
    public static class PasanacoImportQuotas
    {
        private const int MaxPendingQuotasPerPasanaco = 60;
        private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

        public static DateOnly MonthOf(Pasanaco pasanaco, int round) =>
            new DateOnly(pasanaco.StartYear, pasanaco.StartMonth, 1).AddMonths(Math.Max(1, round) - 1);

        public static int RoundOf(Pasanaco pasanaco, DateOnly month) =>
            (month.Year - pasanaco.StartYear) * 12 + month.Month - pasanaco.StartMonth + 1;

        public static DateOnly FirstDayOf(DateOnly date) => new(date.Year, date.Month, 1);

        public static string MonthName(DateOnly month) => month.ToString("MMMM 'de' yyyy", Spanish);

        public static string Label(PasanacoQuota quota) =>
            $"cuota de {quota.ParticipantName} de {MonthName(quota.Month)} del pasanaco «{quota.PasanacoName}»";

        public static async Task<IReadOnlyList<PasanacoImportSummary>> LoadActiveAsync(
            IAppDbContext db, Guid userId, CancellationToken ct)
        {
            var pasanacos = await db.Pasanacos.AsNoTracking()
                .Include(pasanaco => pasanaco.Participants)
                .Where(pasanaco => pasanaco.UserId == userId && !pasanaco.IsCompleted)
                .OrderBy(pasanaco => pasanaco.Name)
                .Take(10)
                .ToListAsync(ct);
            if (pasanacos.Count == 0) return Array.Empty<PasanacoImportSummary>();

            var ids = pasanacos.Select(pasanaco => pasanaco.Id).ToList();
            var paid = (await db.PasanacoPayments.AsNoTracking()
                    .Where(payment => ids.Contains(payment.PasanacoId) && payment.Paid)
                    .Select(payment => new { payment.ParticipantId, payment.Year, payment.Month })
                    .ToListAsync(ct))
                .Select(payment => (payment.ParticipantId, new DateOnly(payment.Year, payment.Month, 1)))
                .ToHashSet();

            return pasanacos.Select(pasanaco =>
            {
                var participants = pasanaco.Participants.OrderBy(participant => participant.AssignedNumber).ToList();
                var currentMonth = MonthOf(pasanaco, pasanaco.CurrentRound);
                var lastRound = Math.Min(pasanaco.TotalParticipants, Math.Max(1, pasanaco.CurrentRound) + 1);
                var pending = Enumerable.Range(1, Math.Max(0, lastRound))
                    .Reverse()
                    .SelectMany(round => participants
                        .Where(participant => !paid.Contains((participant.Id, MonthOf(pasanaco, round))))
                        .Select(participant => new PasanacoQuota(
                            pasanaco.Name,
                            participant.Id,
                            participant.Name,
                            MonthOf(pasanaco, round),
                            pasanaco.MonthlyAmount,
                            RecipientOf(participants, round))))
                    .Take(MaxPendingQuotasPerPasanaco)
                    .ToList();
                return new PasanacoImportSummary(
                    pasanaco.Name,
                    pasanaco.MonthlyAmount,
                    pasanaco.TotalParticipants,
                    pasanaco.CurrentRound,
                    currentMonth,
                    RecipientOf(participants, pasanaco.CurrentRound),
                    pending);
            }).ToList();
        }

        /// <summary>Returns the quota when it belongs to an active pasanaco of the user and is still unpaid.</summary>
        public static async Task<(PasanacoQuota? Quota, string? Error)> ResolveAsync(
            IAppDbContext db, Guid userId, string participantId, DateOnly month, CancellationToken ct)
        {
            var participant = await db.Participants.AsNoTracking()
                .Include(item => item.Pasanaco)
                .FirstOrDefaultAsync(item => item.Id == participantId && item.Pasanaco.UserId == userId, ct);
            if (participant?.Pasanaco is not { } pasanaco)
                return (null, "Ese participante no está en tus pasanacos.");
            if (pasanaco.IsCompleted)
                return (null, "Ese pasanaco ya está terminado.");

            month = FirstDayOf(month);
            var round = RoundOf(pasanaco, month);
            if (round < 1 || round > pasanaco.TotalParticipants)
                return (null, "Ese mes no forma parte del pasanaco.");
            if (await db.PasanacoPayments.AnyAsync(payment =>
                    payment.PasanacoId == pasanaco.Id && payment.ParticipantId == participantId &&
                    payment.Year == month.Year && payment.Month == month.Month && payment.Paid, ct))
                return (null, "Esa cuota ya está marcada como pagada en Pasanaco.");

            var recipient = await db.Participants.AsNoTracking()
                .Where(item => item.PasanacoId == pasanaco.Id && item.AssignedNumber == round)
                .Select(item => item.Name)
                .FirstOrDefaultAsync(ct);
            return (new PasanacoQuota(pasanaco.Name, participant.Id, participant.Name, month, pasanaco.MonthlyAmount, recipient), null);
        }

        public static async Task<IReadOnlyDictionary<(string ParticipantId, DateOnly Month), string>> DescribeAsync(
            IAppDbContext db, Guid userId, IEnumerable<ImportRow> rows, CancellationToken ct)
        {
            var keys = rows
                .Where(row => row.PasanacoParticipantId is not null && row.PasanacoQuotaMonth is not null)
                .Select(row => (ParticipantId: row.PasanacoParticipantId!, Month: row.PasanacoQuotaMonth!.Value))
                .Distinct()
                .ToList();
            if (keys.Count == 0) return new Dictionary<(string, DateOnly), string>();

            var participantIds = keys.Select(key => key.ParticipantId).Distinct().ToList();
            var participants = await db.Participants.AsNoTracking()
                .Where(item => participantIds.Contains(item.Id) && item.Pasanaco.UserId == userId)
                .Select(item => new { item.Id, item.Name, PasanacoName = item.Pasanaco.Name })
                .ToDictionaryAsync(item => item.Id, ct);
            return keys
                .Where(key => participants.ContainsKey(key.ParticipantId))
                .ToDictionary(
                    key => key,
                    key =>
                    {
                        var participant = participants[key.ParticipantId];
                        return Label(new PasanacoQuota(participant.PasanacoName, participant.Id, participant.Name, key.Month, 0m, null));
                    });
        }

        /// <summary>Marks the quotas collected by the applied rows as paid on the row date.</summary>
        public static async Task MarkPaidAsync(
            IAppDbContext db, Guid userId, IEnumerable<ImportRow> rows, CancellationToken ct)
        {
            var payments = new Dictionary<(string ParticipantId, DateOnly Month), PasanacoPayment?>();
            foreach (var row in rows)
            {
                if (row.PasanacoParticipantId is not { } participantId || row.PasanacoQuotaMonth is not { } quotaMonth)
                    continue;
                var month = FirstDayOf(quotaMonth);
                var key = (participantId, month);
                if (!payments.TryGetValue(key, out var payment))
                {
                    var participant = await db.Participants.AsNoTracking()
                        .Include(item => item.Pasanaco)
                        .FirstOrDefaultAsync(item => item.Id == participantId && item.Pasanaco.UserId == userId, ct);
                    if (participant?.Pasanaco is not { IsCompleted: false } pasanaco)
                    {
                        payments[key] = null;
                        continue;
                    }

                    payment = await db.PasanacoPayments.FirstOrDefaultAsync(item =>
                        item.PasanacoId == pasanaco.Id && item.ParticipantId == participantId &&
                        item.Year == month.Year && item.Month == month.Month, ct);
                    if (payment is null)
                    {
                        payment = new PasanacoPayment
                        {
                            PasanacoId = pasanaco.Id,
                            ParticipantId = participantId,
                            Month = month.Month,
                            Year = month.Year
                        };
                        db.PasanacoPayments.Add(payment);
                    }
                    payments[key] = payment;
                }

                if (payment is null || payment.Paid) continue;
                payment.Paid = true;
                payment.PaymentDate = row.ValueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            }
        }

        private static string? RecipientOf(IReadOnlyList<Participant> participants, int round) =>
            participants.FirstOrDefault(participant => participant.AssignedNumber == round)?.Name;
    }
}
