using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Features.Calendar
{
    public sealed class GoogleCalendarProjection(AppDbContext db, GoogleCalendarSettings settings)
    {
        public async Task ReconcileAsync(GoogleCalendarConnection connection, DateTime now, CancellationToken ct)
        {
            var userId = connection.UserId;
            var desired = new Dictionary<string, string>(StringComparer.Ordinal);
            var usages = await db.ZeusHourUsages.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(ct);
            foreach (var usage in usages)
            {
                var key = "zeus:" + usage.Id.ToString("N");
                var duration = $"{usage.Minutes / 60} h {usage.Minutes % 60:00} min";
                desired[key] = Event(userId, key, $"Zeus · Guardería · {duration}", usage.Date,
                    usage.StartTime, usage.EndTime, usage.Note, connection.ZeusColorId);
            }
            var pasanacos = await db.Pasanacos.AsNoTracking().Include(item => item.Participants)
                .Where(item => item.UserId == userId).ToListAsync(ct);
            foreach (var pasanaco in pasanacos)
            {
                foreach (var participant in pasanaco.Participants.Where(item =>
                    item.AssignedNumber >= 1 && item.AssignedNumber <= pasanaco.TotalParticipants
                    && (!pasanaco.IsCompleted || item.AssignedNumber <= pasanaco.CurrentRound)))
                {
                    var date = new DateOnly(pasanaco.StartYear, pasanaco.StartMonth, 5)
                        .AddMonths(participant.AssignedNumber - 1);
                    var key = "pasanaco:" + participant.Id;
                    desired[key] = Event(userId, key, $"Entregar pasanaco a {participant.Name}", date,
                        null, null, $"Pasanaco: {pasanaco.Name}. Este evento no confirma que se haya pagado.",
                        connection.PasanacoColorId);
                }
            }
            var reminders = await db.Reminders.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(ct);
            foreach (var reminder in reminders)
            {
                var dueDate = reminder.SnoozedUntil > reminder.DueDate ? reminder.SnoozedUntil.Value : reminder.DueDate;
                desired["reminder:" + reminder.Id.ToString("N")] = JsonSerializer.Serialize(new
                {
                    title = reminder.Title,
                    notes = reminder.Notes ?? string.Empty,
                    due = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z",
                    status = reminder.IsDone ? "completed" : "needsAction",
                    completed = reminder.CompletedAt.HasValue
                        ? DateTime.SpecifyKind(reminder.CompletedAt.Value, DateTimeKind.Utc)
                            .ToString("O", CultureInfo.InvariantCulture)
                        : null
                });
            }
            var existing = await db.GoogleSyncItems.Where(item => item.UserId == userId).ToListAsync(ct);
            var bySource = existing.ToDictionary(item => item.SourceKey, StringComparer.Ordinal);
            foreach (var (key, payload) in desired)
            {
                if (!bySource.TryGetValue(key, out var item))
                {
                    var isTask = key.StartsWith("reminder:", StringComparison.Ordinal);
                    item = new GoogleSyncItem
                    {
                        UserId = userId,
                        SourceKey = key,
                        ResourceKind = isTask ? "task" : "event",
                        RemoteId = isTask ? null : Guid.NewGuid().ToString("N")
                    };
                    db.GoogleSyncItems.Add(item);
                }
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                if (hash == item.DesiredHash) continue;
                item.Payload = payload;
                item.DesiredHash = hash;
                Reset(item, now);
            }
            foreach (var item in existing.Where(item => item.Payload is not null && !desired.ContainsKey(item.SourceKey)))
            {
                item.Payload = null;
                item.DesiredHash = string.Empty;
                Reset(item, now);
            }
            await db.SaveChangesAsync(ct);
        }

        private string Event(Guid userId, string source, string title, DateOnly date,
            TimeOnly? start, TimeOnly? end, string? note, int colorId)
        {
            object startValue;
            object endValue;
            if (start.HasValue && end.HasValue)
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
                var startsAt = LocalDateTime(date, start.Value, timeZone);
                var endsAt = LocalDateTime(date, end.Value, timeZone);
                if (endsAt <= startsAt)
                    throw new InvalidOperationException("Calendar event end must follow its start.");
                startValue = new { dateTime = startsAt, timeZone = settings.TimeZone };
                endValue = new { dateTime = endsAt, timeZone = settings.TimeZone };
            }
            else
            {
                startValue = new { date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                endValue = new { date = date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            }
            return JsonSerializer.Serialize(new
            {
                summary = title,
                colorId = colorId.ToString(CultureInfo.InvariantCulture),
                description = note ?? "Sincronizado desde PersonalFinance.",
                start = startValue,
                end = endValue,
                visibility = "private",
                reminders = new { useDefault = false },
                extendedProperties = new
                {
                    @private = new { app = "personalfinance", user = userId.ToString("N"), source }
                }
            });
        }

        private static DateTimeOffset LocalDateTime(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
        {
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(local))
                throw new GoogleCalendarRemoteException("Un horario de Zeus coincide con una hora inexistente por el cambio de hora. Corrige ese registro.");
            return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        }

        private static void Reset(GoogleSyncItem item, DateTime now)
        {
            item.NeedsSync = true;
            item.RetryAt = now;
            item.Attempts = 0;
            item.LastError = null;
        }
    }
}
