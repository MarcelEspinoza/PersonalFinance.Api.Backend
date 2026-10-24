using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Enums;

namespace PersonalFinance.Api.Features.Reminders
{
    /// <summary>Validated values of a reminder, ready to be written to the entity.</summary>
    public sealed record ReminderInput(
        string Title,
        string? Notes,
        DateOnly DueDate,
        decimal? Amount,
        bool IsIncome,
        ReminderRepeat Repeat,
        int NotifyDaysBefore,
        bool EmailEnabled,
        string Source);

    /// <summary>
    /// Pure reminder rules shared by the API, the email job and the global chat:
    /// validation, the "due" rule and the next occurrence of repeating reminders.
    /// </summary>
    public static class ReminderRules
    {
        public const int MaxNotifyDaysBefore = 30;
        public const int DefaultNotifyDaysBefore = 1;
        public const int MaxSnoozeDays = 30;
        public const string SourceManual = "manual";
        public const string SourceAi = "ai";

        private const decimal MaxAmount = 999_999_999_999.99m;
        private const int MinYear = 2000;
        private const int MaxYear = 2100;

        private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
        private static readonly Lazy<TimeZoneInfo> Madrid = new(ResolveMadrid);

        public static TimeZoneInfo LocalZone => Madrid.Value;

        public static DateTime ToLocal(DateTime utcNow) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Madrid.Value);

        /// <summary>"Today" for reminders is always the calendar day in Madrid.</summary>
        public static DateOnly LocalToday(DateTime utcNow) => DateOnly.FromDateTime(ToLocal(utcNow));

        /// <summary>First day the reminder is shown: N days before the due date, or after the snooze.</summary>
        public static DateOnly VisibleFrom(Reminder reminder)
        {
            var from = reminder.DueDate.AddDays(-Math.Clamp(reminder.NotifyDaysBefore, 0, MaxNotifyDaysBefore));
            return reminder.SnoozedUntil is { } snoozed && snoozed > from ? snoozed : from;
        }

        public static bool IsDue(Reminder reminder, DateOnly today) =>
            !reminder.IsDone && today >= VisibleFrom(reminder);

        public static DateOnly NextDueDate(Reminder reminder)
        {
            var due = reminder.DueDate;
            var anchor = reminder.AnchorDay is >= 1 and <= 31 ? reminder.AnchorDay : due.Day;
            switch (reminder.Repeat)
            {
                case ReminderRepeat.Weekly:
                    return due.AddDays(7);
                case ReminderRepeat.Monthly:
                    var nextMonth = new DateOnly(due.Year, due.Month, 1).AddMonths(1);
                    return OnAnchorDay(nextMonth.Year, nextMonth.Month, anchor);
                case ReminderRepeat.Yearly:
                    return OnAnchorDay(due.Year + 1, due.Month, anchor);
                default:
                    return due;
            }
        }

        public static string ToApi(ReminderRepeat repeat) => repeat switch
        {
            ReminderRepeat.Weekly => "weekly",
            ReminderRepeat.Monthly => "monthly",
            ReminderRepeat.Yearly => "yearly",
            _ => "none"
        };

        public static bool TryParseRepeat(string? value, out ReminderRepeat repeat)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "none": repeat = ReminderRepeat.None; return true;
                case "weekly": repeat = ReminderRepeat.Weekly; return true;
                case "monthly": repeat = ReminderRepeat.Monthly; return true;
                case "yearly": repeat = ReminderRepeat.Yearly; return true;
                default: repeat = ReminderRepeat.None; return false;
            }
        }

        public static string DescribeRepeat(ReminderRepeat repeat) => repeat switch
        {
            ReminderRepeat.Weekly => "Cada semana",
            ReminderRepeat.Monthly => "Cada mes",
            ReminderRepeat.Yearly => "Cada año",
            _ => "Una vez"
        };

        public static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public static string FormatAmount(decimal amount) => amount.ToString("N2", Spanish) + " €";

        /// <summary>"cobro de 20,00 €" or "pago de 20,00 €": a reminder amount always says which way the money goes.</summary>
        public static string DescribeAmount(decimal amount, bool isIncome) =>
            $"{(isIncome ? "cobro" : "pago")} de {FormatAmount(amount)}";

        /// <summary>Spanish summary shown on the chat confirmation card.</summary>
        public static string DescribeProposal(ReminderInput input)
        {
            var notice = input.NotifyDaysBefore switch
            {
                0 => "avisa el mismo día",
                1 => "avisa 1 día antes",
                var days => $"avisa {days} días antes"
            };
            var summary = $"Crear recordatorio «{input.Title}» para el {FormatDate(input.DueDate)} " +
                          $"({DescribeRepeat(input.Repeat)}, {notice})";
            return input.Amount is { } amount ? $"{summary} · {DescribeAmount(amount, input.IsIncome)}" : summary;
        }

        public static ReminderDto ToDto(Reminder reminder) => new(
            reminder.Id,
            reminder.Title,
            reminder.Notes,
            reminder.DueDate,
            reminder.Amount,
            reminder.IsIncome,
            ToApi(reminder.Repeat),
            reminder.NotifyDaysBefore,
            reminder.EmailEnabled,
            reminder.IsDone,
            reminder.CompletedAt,
            reminder.SnoozedUntil,
            reminder.Source,
            reminder.CreatedAt);

        /// <summary>
        /// Validates a create (existing == null) or update body. On update, omitted
        /// repeat/notifyDaysBefore/emailEnabled keep their value and source never changes.
        /// </summary>
        public static bool TryNormalize(
            SaveReminderDto? dto,
            Reminder? existing,
            [NotNullWhen(true)] out ReminderInput? input,
            [NotNullWhen(false)] out string? error)
        {
            input = null;
            if (dto is null)
                return Fail("Faltan los datos del recordatorio.", out error);

            var title = dto.Title?.Trim();
            if (string.IsNullOrEmpty(title))
                return Fail("El título del recordatorio es obligatorio.", out error);
            if (title.Length > Reminder.TitleMaxLength)
                return Fail($"El título no puede superar los {Reminder.TitleMaxLength} caracteres.", out error);

            var notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            if (notes?.Length > Reminder.NotesMaxLength)
                return Fail($"Las notas no pueden superar los {Reminder.NotesMaxLength} caracteres.", out error);

            if (string.IsNullOrWhiteSpace(dto.DueDate))
                return Fail("La fecha del recordatorio es obligatoria.", out error);
            if (!DateOnly.TryParseExact(dto.DueDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dueDate))
                return Fail("La fecha del recordatorio no es válida (usa el formato aaaa-mm-dd).", out error);
            if (dueDate.Year is < MinYear or > MaxYear)
                return Fail($"La fecha del recordatorio debe estar entre los años {MinYear} y {MaxYear}.", out error);

            decimal? amount = dto.Amount is { } rawAmount && rawAmount != 0
                ? Math.Round(rawAmount, 2, MidpointRounding.AwayFromZero)
                : null;
            if (amount < 0)
                return Fail("El importe no puede ser negativo.", out error);
            if (amount > MaxAmount)
                return Fail("El importe es demasiado alto.", out error);
            if (amount == 0) amount = null;

            var isIncome = dto.IsIncome ?? existing?.IsIncome ?? false;

            ReminderRepeat repeat;
            if (string.IsNullOrWhiteSpace(dto.Repeat))
                repeat = existing?.Repeat ?? ReminderRepeat.None;
            else if (!TryParseRepeat(dto.Repeat, out repeat))
                return Fail("La repetición no es válida (usa none, weekly, monthly o yearly).", out error);

            var notifyDaysBefore = dto.NotifyDaysBefore ?? existing?.NotifyDaysBefore ?? DefaultNotifyDaysBefore;
            if (notifyDaysBefore is < 0 or > MaxNotifyDaysBefore)
                return Fail($"El aviso debe ser de 0 a {MaxNotifyDaysBefore} días antes.", out error);

            var emailEnabled = dto.EmailEnabled ?? existing?.EmailEnabled ?? true;

            string source;
            if (existing is not null)
                source = existing.Source;
            else if (string.IsNullOrWhiteSpace(dto.Source))
                source = SourceManual;
            else
            {
                source = dto.Source.Trim().ToLowerInvariant();
                if (source is not (SourceManual or SourceAi))
                    return Fail("El origen del recordatorio debe ser manual o ai.", out error);
            }

            input = new ReminderInput(title, notes, dueDate, amount, isIncome, repeat, notifyDaysBefore, emailEnabled, source);
            error = null;
            return true;
        }

        /// <summary>
        /// Validation for reminders proposed by the AI: always source "ai" with email
        /// enabled, and the date cannot be in the past.
        /// </summary>
        public static bool TryNormalizeFromAssistant(
            SaveReminderDto dto,
            DateOnly today,
            [NotNullWhen(true)] out ReminderInput? input,
            [NotNullWhen(false)] out string? error)
        {
            var proposal = new SaveReminderDto
            {
                Title = dto.Title,
                Notes = dto.Notes,
                DueDate = dto.DueDate,
                Amount = dto.Amount,
                IsIncome = dto.IsIncome,
                Repeat = dto.Repeat,
                NotifyDaysBefore = dto.NotifyDaysBefore,
                EmailEnabled = true,
                Source = SourceAi
            };
            if (!TryNormalize(proposal, null, out input, out error)) return false;
            if (input.DueDate < today)
            {
                input = null;
                return Fail("La fecha del recordatorio no puede ser anterior a hoy.", out error);
            }

            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static DateOnly OnAnchorDay(int year, int month, int anchorDay) =>
            new(year, month, Math.Min(anchorDay, DateTime.DaysInMonth(year, month)));

        private static TimeZoneInfo ResolveMadrid()
        {
            foreach (var id in new[] { "Europe/Madrid", "Romance Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.Utc;
        }
    }
}
