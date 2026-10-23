using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Enums;

namespace PersonalFinance.Api.Features.Reminders
{
    public interface IReminderService
    {
        Task<IReadOnlyList<ReminderDto>> ListAsync(Guid userId, string? status, CancellationToken ct);
        Task<DueRemindersDto> GetDueAsync(Guid userId, CancellationToken ct);
        Task<ReminderDto> CreateAsync(Guid userId, SaveReminderDto? dto, CancellationToken ct);
        /// <summary>Creates a reminder the user confirmed from an AI proposal (source "ai").</summary>
        Task<ReminderDto> CreateFromAssistantAsync(Guid userId, SaveReminderDto dto, CancellationToken ct);
        Task<ReminderDto> UpdateAsync(Guid userId, Guid id, SaveReminderDto? dto, CancellationToken ct);
        Task DeleteAsync(Guid userId, Guid id, CancellationToken ct);
        Task<ReminderDto> CompleteAsync(Guid userId, Guid id, CancellationToken ct);
        Task<ReminderDto> SnoozeAsync(Guid userId, Guid id, SnoozeReminderDto? dto, CancellationToken ct);
    }

    public sealed class ReminderService : IReminderService
    {
        private readonly AppDbContext _db;
        private readonly IClock _clock;

        public ReminderService(AppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<IReadOnlyList<ReminderDto>> ListAsync(Guid userId, string? status, CancellationToken ct)
        {
            var filter = string.IsNullOrWhiteSpace(status) ? "active" : status.Trim().ToLowerInvariant();
            if (filter is not ("active" or "done" or "all"))
                throw new BusinessRuleException("El estado debe ser active, done o all.");

            var query = _db.Reminders.AsNoTracking().Where(r => r.UserId == userId);
            if (filter == "active") query = query.Where(r => !r.IsDone);
            else if (filter == "done") query = query.Where(r => r.IsDone);
            var reminders = await query.ToListAsync(ct);

            var active = reminders.Where(r => !r.IsDone).OrderBy(r => r.DueDate).ThenBy(r => r.CreatedAt);
            var done = reminders.Where(r => r.IsDone)
                .OrderByDescending(r => r.CompletedAt)
                .ThenByDescending(r => r.CreatedAt);
            return active.Concat(done).Select(ReminderRules.ToDto).ToList();
        }

        public async Task<DueRemindersDto> GetDueAsync(Guid userId, CancellationToken ct)
        {
            var today = ReminderRules.LocalToday(_clock.UtcNow);
            // Nothing due later than today + the maximum notice can be visible yet.
            var horizon = today.AddDays(ReminderRules.MaxNotifyDaysBefore);
            var candidates = await _db.Reminders
                .AsNoTracking()
                .Where(r => r.UserId == userId && !r.IsDone && r.DueDate <= horizon)
                .ToListAsync(ct);

            var items = candidates
                .Where(r => ReminderRules.IsDue(r, today))
                .OrderBy(r => r.DueDate)
                .ThenBy(r => r.CreatedAt)
                .Select(ReminderRules.ToDto)
                .ToList();
            return new DueRemindersDto(items.Count, items);
        }

        public Task<ReminderDto> CreateAsync(Guid userId, SaveReminderDto? dto, CancellationToken ct)
        {
            if (!ReminderRules.TryNormalize(dto, null, out var input, out var error))
                throw new BusinessRuleException(error);
            return AddAsync(userId, input, ct);
        }

        public Task<ReminderDto> CreateFromAssistantAsync(Guid userId, SaveReminderDto dto, CancellationToken ct)
        {
            var today = ReminderRules.LocalToday(_clock.UtcNow);
            if (!ReminderRules.TryNormalizeFromAssistant(dto, today, out var input, out var error))
                throw new BusinessRuleException(error);
            return AddAsync(userId, input, ct);
        }

        public async Task<ReminderDto> UpdateAsync(Guid userId, Guid id, SaveReminderDto? dto, CancellationToken ct)
        {
            var reminder = await FindAsync(userId, id, ct);
            if (!ReminderRules.TryNormalize(dto, reminder, out var input, out var error))
                throw new BusinessRuleException(error);

            if (input.DueDate != reminder.DueDate)
            {
                // A new date is a new anchor, and an old snooze no longer applies.
                reminder.DueDate = input.DueDate;
                reminder.AnchorDay = input.DueDate.Day;
                reminder.SnoozedUntil = null;
            }

            reminder.Title = input.Title;
            reminder.Notes = input.Notes;
            reminder.Amount = input.Amount;
            reminder.Repeat = input.Repeat;
            reminder.NotifyDaysBefore = input.NotifyDaysBefore;
            reminder.EmailEnabled = input.EmailEnabled;
            reminder.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(ct);
            return ReminderRules.ToDto(reminder);
        }

        public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var reminder = await FindAsync(userId, id, ct);
            _db.Reminders.Remove(reminder);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<ReminderDto> CompleteAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var reminder = await FindAsync(userId, id, ct);
            if (reminder.IsDone) return ReminderRules.ToDto(reminder);

            var now = _clock.UtcNow;
            if (reminder.Repeat == ReminderRepeat.None)
            {
                reminder.IsDone = true;
                reminder.CompletedAt = now;
            }
            else
            {
                reminder.DueDate = ReminderRules.NextDueDate(reminder);
                reminder.SnoozedUntil = null;
            }

            reminder.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return ReminderRules.ToDto(reminder);
        }

        public async Task<ReminderDto> SnoozeAsync(Guid userId, Guid id, SnoozeReminderDto? dto, CancellationToken ct)
        {
            if (dto?.Days is not { } days || days < 1 || days > ReminderRules.MaxSnoozeDays)
                throw new BusinessRuleException($"Puedes posponer un recordatorio de 1 a {ReminderRules.MaxSnoozeDays} días.");

            var reminder = await FindAsync(userId, id, ct);
            if (reminder.IsDone)
                throw new BusinessRuleException("Este recordatorio ya está completado.");

            var now = _clock.UtcNow;
            reminder.SnoozedUntil = ReminderRules.LocalToday(now).AddDays(days);
            // Email again when the snooze ends.
            reminder.LastEmailedForDueDate = null;
            reminder.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);
            return ReminderRules.ToDto(reminder);
        }

        private async Task<ReminderDto> AddAsync(Guid userId, ReminderInput input, CancellationToken ct)
        {
            var reminder = new Reminder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = input.Title,
                Notes = input.Notes,
                DueDate = input.DueDate,
                AnchorDay = input.DueDate.Day,
                Amount = input.Amount,
                Repeat = input.Repeat,
                NotifyDaysBefore = input.NotifyDaysBefore,
                EmailEnabled = input.EmailEnabled,
                Source = input.Source,
                CreatedAt = _clock.UtcNow
            };
            _db.Reminders.Add(reminder);
            await _db.SaveChangesAsync(ct);
            return ReminderRules.ToDto(reminder);
        }

        private async Task<Reminder> FindAsync(Guid userId, Guid id, CancellationToken ct) =>
            await _db.Reminders.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct)
            ?? throw new NotFoundException("No encuentro ese recordatorio.");
    }
}
