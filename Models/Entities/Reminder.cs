using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PersonalFinance.Api.Models.Enums;

namespace PersonalFinance.Api.Models.Entities
{
    public class Reminder
    {
        public const int TitleMaxLength = 200;
        public const int NotesMaxLength = 2000;
        public const int SourceMaxLength = 20;

        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        [Required]
        [MaxLength(TitleMaxLength)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(NotesMaxLength)]
        public string? Notes { get; set; }

        public DateOnly DueDate { get; set; }

        // Day of month the user picked. Monthly/yearly repeats go back to it so that
        // 31 Jan -> 28 Feb -> 31 Mar instead of drifting to the 28th forever.
        public int AnchorDay { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Amount { get; set; }

        // true = money coming in (a cobro): the forecast adds the amount instead of subtracting it.
        public bool IsIncome { get; set; }

        public ReminderRepeat Repeat { get; set; } = ReminderRepeat.None;

        public int NotifyDaysBefore { get; set; } = 1;

        public bool EmailEnabled { get; set; } = true;

        public bool IsDone { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateOnly? SnoozedUntil { get; set; }

        // Occurrence (due date) already covered by an email, so each one is emailed once.
        public DateOnly? LastEmailedForDueDate { get; set; }

        // "manual" or "ai"
        [Required]
        [MaxLength(SourceMaxLength)]
        public string Source { get; set; } = "manual";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
