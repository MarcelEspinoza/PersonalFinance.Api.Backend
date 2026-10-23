using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Features.Reminders
{
    /// <summary>
    /// Sends one email per user with the reminders that became due and were not
    /// emailed yet for their current due date. Only approved users (Admin/User role)
    /// with a confirmed email receive it.
    /// </summary>
    public sealed class ReminderEmailDispatcher
    {
        private static readonly TimeSpan SendFrom = TimeSpan.FromHours(8);

        private readonly AppDbContext _db;
        private readonly IEmailSender _email;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReminderEmailDispatcher> _logger;

        public ReminderEmailDispatcher(
            AppDbContext db,
            IEmailSender email,
            IConfiguration configuration,
            ILogger<ReminderEmailDispatcher> logger)
        {
            _db = db;
            _email = email;
            _configuration = configuration;
            _logger = logger;
        }

        /// <returns>Number of emails sent.</returns>
        public async Task<int> SendDueAsync(DateTime utcNow, CancellationToken ct)
        {
            var local = ReminderRules.ToLocal(utcNow);
            if (local.TimeOfDay < SendFrom) return 0;

            var today = DateOnly.FromDateTime(local);
            var horizon = today.AddDays(ReminderRules.MaxNotifyDaysBefore);
            var candidates = await _db.Reminders
                .AsNoTracking()
                .Where(r => r.EmailEnabled && !r.IsDone && r.DueDate <= horizon)
                .ToListAsync(ct);
            var pending = candidates
                .Where(r => r.LastEmailedForDueDate != r.DueDate && ReminderRules.IsDue(r, today))
                .ToList();
            if (pending.Count == 0) return 0;

            var userIds = pending.Select(r => r.UserId).Distinct().ToList();
            var approvedRoleIds = await _db.Roles
                .Where(r => r.NormalizedName == "ADMIN" || r.NormalizedName == "USER")
                .Select(r => r.Id)
                .ToListAsync(ct);
            var recipients = await _db.Set<ApplicationUser>()
                .AsNoTracking()
                .Where(u =>
                    userIds.Contains(u.Id) &&
                    u.EmailConfirmed &&
                    u.Email != null && u.Email != "" &&
                    _db.UserRoles.Any(ur => ur.UserId == u.Id && approvedRoleIds.Contains(ur.RoleId)))
                .Select(u => new { u.Id, u.Email, u.FullName })
                .ToListAsync(ct);

            var remindersUrl = BuildRemindersUrl();
            var sent = 0;
            foreach (var recipient in recipients)
            {
                var items = pending
                    .Where(r => r.UserId == recipient.Id)
                    .OrderBy(r => r.DueDate)
                    .ThenBy(r => r.CreatedAt)
                    .ToList();
                try
                {
                    var subject = items.Count == 1
                        ? "Tienes 1 recordatorio pendiente"
                        : $"Tienes {items.Count} recordatorios pendientes";
                    var html = EmailTemplates.RemindersDigestTemplate(recipient.FullName, items, remindersUrl);
                    if (!await _email.TrySendEmailAsync(recipient.Email!, subject, html))
                    {
                        _logger.LogWarning("Reminder email for user {UserId} was not sent; will retry later.", recipient.Id);
                        continue;
                    }

                    foreach (var item in items)
                    {
                        // Guarded by the loaded date: if the user completed it meanwhile,
                        // the new occurrence stays pending for its own email.
                        DateOnly? emailedFor = item.DueDate;
                        await _db.Reminders
                            .Where(r => r.Id == item.Id && r.DueDate == item.DueDate)
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastEmailedForDueDate, emailedFor), ct);
                    }

                    sent++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send reminder email to user {UserId}.", recipient.Id);
                }
            }

            return sent;
        }

        private string? BuildRemindersUrl()
        {
            var baseUrl = _configuration["Frontend:Url"];
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = _configuration["Cors:AllowedOrigins:0"];
            return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/') + "/reminders";
        }
    }
}
