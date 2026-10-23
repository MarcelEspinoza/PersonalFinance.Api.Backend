using System.Text.Json.Serialization;

namespace PersonalFinance.Api.Features.Reminders
{
    /// <summary>
    /// Reminder as the frontend sees it. Optional fields are always written
    /// (explicit null) so every key of the contract is present.
    /// </summary>
    public sealed record ReminderDto(
        Guid Id,
        string Title,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Notes,
        DateOnly DueDate,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? Amount,
        string Repeat,
        int NotifyDaysBefore,
        bool EmailEnabled,
        bool IsDone,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? CompletedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateOnly? SnoozedUntil,
        string Source,
        DateTime CreatedAt);

    public sealed record DueRemindersDto(int Count, IReadOnlyList<ReminderDto> Items);

    /// <summary>
    /// Body of POST/PUT api/reminders. Everything is optional at binding time so
    /// the service can answer with its own Spanish validation messages.
    /// </summary>
    public sealed class SaveReminderDto
    {
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public string? DueDate { get; set; } // yyyy-MM-dd
        public decimal? Amount { get; set; }
        public string? Repeat { get; set; } // none | weekly | monthly | yearly
        public int? NotifyDaysBefore { get; set; }
        public bool? EmailEnabled { get; set; }
        public string? Source { get; set; } // manual | ai
    }

    public sealed class SnoozeReminderDto
    {
        public int? Days { get; set; }
    }
}
