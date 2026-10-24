using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Models.Enums;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.ServiceSavings
{
    /// <summary>Stable keys that identify a recurring service across analyses.</summary>
    internal static class ServiceSavingsKeys
    {
        public const int MaxLength = 200;

        public static string Rule(Guid ruleId) => $"rule:{ruleId}";

        public static string Debt(Guid debtId) => $"debt:{debtId}";

        public static string Loan(Guid loanId) => $"loan:{loanId}";

        public static string Group(Guid conceptId, string normalizedDescription)
        {
            var key = $"grp:{conceptId}:{normalizedDescription}";
            if (key.Length <= MaxLength) return key;

            var length = MaxLength;
            if (char.IsHighSurrogate(key[length - 1])) length--;
            return key[..length].TrimEnd();
        }

        public static bool IsValid(string key) =>
            key.Length <= MaxLength &&
            (key.StartsWith("rule:", StringComparison.Ordinal) ||
             key.StartsWith("debt:", StringComparison.Ordinal) ||
             key.StartsWith("loan:", StringComparison.Ordinal) ||
             key.StartsWith("grp:", StringComparison.Ordinal));
    }

    /// <summary>A recurring payment the AI may make a suggestion about.</summary>
    internal sealed class ServiceCandidate
    {
        public ServiceCandidate(string key, string reference, string name, decimal? monthlyAmount)
        {
            Key = key;
            Reference = reference;
            Name = name;
            MonthlyAmount = monthlyAmount;
        }

        public string Key { get; }

        /// <summary>
        /// Short id ("s1", "s2"...) sent to the model instead of <see cref="Key"/>, so
        /// internal ids and unmasked descriptions never leave the server.
        /// </summary>
        public string Reference { get; }

        /// <summary>Name shown to the user when the model does not give one.</summary>
        public string Name { get; }

        public decimal? MonthlyAmount { get; }

        /// <summary>Masked data about the payment, serialized into the prompt.</summary>
        public object Prompt { get; init; } = new();
    }

    internal sealed class ServiceSavingsContext
    {
        public List<ServiceCandidate> Candidates { get; } = new();

        public HashSet<string> DismissedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string PromptJson { get; set; } = string.Empty;

        public ServiceCandidate? Find(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            var value = reference.Trim();
            return Candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Reference, value, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Collects the user's recurring payments for the service savings analysis:
    /// active outgoing recurring rules, payments that repeat in the last 12 months
    /// of the ledger, and bank debts. Loans between people, savings, transfers,
    /// pasanacos and services the user hid are left out.
    /// </summary>
    internal sealed class ServiceSavingsContextBuilder
    {
        private const int HistoryMonths = 12;
        private const int MaxRules = 80;
        private const int MaxGroups = 60;
        private const int MaxDebts = 20;

        private static readonly string[] ClosedLoanStatuses =
            { "paid", "closed", "cancelled", "canceled", "completed", "finished", "pagado", "cerrado" };

        private static readonly JsonSerializerOptions PromptJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly IAppDbContext _db;

        public ServiceSavingsContextBuilder(IAppDbContext db)
        {
            _db = db;
        }

        public async Task<ServiceSavingsContext> BuildAsync(Guid userId, DateOnly today, CancellationToken ct)
        {
            var context = new ServiceSavingsContext();

            var notes = await _db.ServiceNotes.AsNoTracking()
                .Where(note => note.UserId == userId)
                .Select(note => new { note.ServiceKey, note.Message, note.Dismissed })
                .ToListAsync(ct);
            foreach (var note in notes.Where(note => note.Dismissed))
                context.DismissedKeys.Add(note.ServiceKey);
            var comments = notes
                .Where(note => !note.Dismissed && !string.IsNullOrWhiteSpace(note.Message))
                .GroupBy(note => note.ServiceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Message!, StringComparer.OrdinalIgnoreCase);

            var concepts = await _db.Concepts.AsNoTracking()
                .Where(concept => concept.UserId == userId)
                .Select(concept => new ConceptRow(
                    concept.Id,
                    concept.Name,
                    concept.Kind,
                    concept.Nature,
                    concept.Group != null ? concept.Group.Name : null))
                .ToDictionaryAsync(concept => concept.Id, ct);

            var rules = await _db.RecurringRules.AsNoTracking()
                .Where(rule => rule.UserId == userId && rule.Direction == EntryDirection.Out)
                .Select(rule => new RuleRow(
                    rule.Id,
                    rule.ConceptId,
                    rule.Description,
                    rule.Frequency,
                    rule.DayOfMonth,
                    rule.ForecastAmount,
                    rule.EndDate,
                    rule.IsActive))
                .ToListAsync(ct);

            var includedRules = new Dictionary<Guid, RuleCandidate>();
            var blockedRuleIds = new HashSet<Guid>();
            foreach (var rule in rules)
            {
                var concept = concepts.GetValueOrDefault(rule.ConceptId);
                var excluded = concept is null || IsExcludedConcept(concept) || MentionsPasanaco(rule.Description);
                if (excluded || context.DismissedKeys.Contains(ServiceSavingsKeys.Rule(rule.Id)))
                {
                    blockedRuleIds.Add(rule.Id);
                    continue;
                }

                if (rule.IsActive && (rule.EndDate is null || rule.EndDate >= today))
                    includedRules[rule.Id] = new RuleCandidate(rule, concept!);
            }

            var since = today.AddMonths(-HistoryMonths);
            var queryFrom = since.AddDays(-45);
            var entries = await _db.LedgerEntries.AsNoTracking()
                .Where(entry =>
                    entry.UserId == userId &&
                    entry.Status == EntryStatus.Paid &&
                    entry.Direction == EntryDirection.Out &&
                    !entry.IsTransfer &&
                    entry.PersonalLoanId == null &&
                    entry.SavingsGoalId == null &&
                    entry.DebtScheduleItemId == null &&
                    entry.DueDate >= queryFrom)
                .Select(entry => new EntryRow(
                    entry.ConceptId,
                    entry.RecurringRuleId,
                    entry.Description,
                    entry.ValueDate ?? entry.DueDate,
                    entry.ActualAmount ?? entry.ForecastAmount))
                .ToListAsync(ct);

            var groups = new Dictionary<(Guid ConceptId, string Normalized), EntryGroup>();
            foreach (var entry in entries)
            {
                if (entry.Date < since || entry.Date > today) continue;
                if (entry.RecurringRuleId is Guid linkedRule && blockedRuleIds.Contains(linkedRule)) continue;
                if (!concepts.TryGetValue(entry.ConceptId, out var concept) || IsExcludedConcept(concept)) continue;
                if (MentionsPasanaco(entry.Description)) continue;

                var amount = Math.Abs(entry.Amount);
                if (amount == 0) continue;

                var normalized = RevolutMovementClassifier.Normalize(entry.Description);
                if (!groups.TryGetValue((entry.ConceptId, normalized), out var group))
                {
                    group = new EntryGroup(concept, normalized);
                    groups[(entry.ConceptId, normalized)] = group;
                }

                group.History.Add(entry.Date, amount, entry.Description);
                if (entry.RecurringRuleId is Guid ruleId)
                    group.RuleVotes[ruleId] = group.RuleVotes.GetValueOrDefault(ruleId) + 1;
            }

            var standaloneGroups = new List<EntryGroup>();
            foreach (var group in groups.Values)
            {
                var rule = FindRule(group, includedRules);
                if (rule is not null)
                {
                    rule.History.AddRange(group.History);
                    continue;
                }

                var key = ServiceSavingsKeys.Group(group.Concept.Id, group.Normalized);
                if (context.DismissedKeys.Contains(key)) continue;

                var keep = group.History.Months >= 2 ||
                           group.RuleVotes.Count > 0 ||
                           group.Concept.Nature == ConceptNature.Fixed;
                if (keep) standaloneGroups.Add(group);
            }

            foreach (var rule in includedRules.Values
                         .OrderByDescending(rule => rule.MonthlyAmount ?? 0)
                         .Take(MaxRules))
            {
                var key = ServiceSavingsKeys.Rule(rule.Row.Id);
                var name = FirstNonEmpty(rule.Row.Description, rule.Concept.Name);
                var reference = NextReference(context);
                context.Candidates.Add(new ServiceCandidate(key, reference, Shorten(name, 120), rule.MonthlyAmount)
                {
                    Prompt = new
                    {
                        key = reference,
                        type = "recurringRule",
                        concept = Mask(rule.Concept.Name, 80),
                        conceptGroup = Mask(rule.Concept.GroupName, 80),
                        description = Mask(rule.Row.Description, 120),
                        frequency = FrequencyName(rule.Row.Frequency),
                        amount = Round(rule.Row.ForecastAmount),
                        monthlyAmount = rule.MonthlyAmount,
                        dayOfMonth = rule.Row.DayOfMonth,
                        history = rule.History.Payments > 0 ? DescribeHistory(rule.History, includeSample: false) : null,
                        userComment = Comment(comments, key)
                    }
                });
            }

            foreach (var group in standaloneGroups
                         .OrderByDescending(group => group.History.Total)
                         .Take(MaxGroups))
            {
                var key = ServiceSavingsKeys.Group(group.Concept.Id, group.Normalized);
                var name = FirstNonEmpty(group.History.LastDescription, group.Concept.Name);
                var monthly = group.History.EstimatedMonthlyAmount();
                var reference = NextReference(context);
                context.Candidates.Add(new ServiceCandidate(key, reference, Shorten(name, 120), monthly)
                {
                    Prompt = new
                    {
                        key = reference,
                        type = "paymentHistory",
                        concept = Mask(group.Concept.Name, 80),
                        conceptGroup = Mask(group.Concept.GroupName, 80),
                        estimatedFrequency = group.History.EstimatedFrequency(),
                        monthlyAmount = monthly,
                        history = DescribeHistory(group.History, includeSample: true),
                        userComment = Comment(comments, key)
                    }
                });
            }

            await AddDebtsAsync(userId, context, comments, ct);
            await AddLegacyBankLoansAsync(userId, context, comments, ct);

            context.PromptJson = JsonSerializer.Serialize(new
            {
                today = today.ToString("yyyy-MM-dd"),
                country = "ES",
                currency = "EUR",
                payments = context.Candidates.Select(candidate => candidate.Prompt)
            }, PromptJsonOptions);

            return context;
        }

        private static string NextReference(ServiceSavingsContext context) => $"s{context.Candidates.Count + 1}";

        private async Task AddDebtsAsync(
            Guid userId,
            ServiceSavingsContext context,
            IReadOnlyDictionary<string, string> comments,
            CancellationToken ct)
        {
            var debts = await _db.Debts.AsNoTracking()
                .Where(debt => debt.UserId == userId && debt.Status == DebtStatus.Active)
                .Select(debt => new
                {
                    debt.Id,
                    debt.Name,
                    debt.Lender,
                    debt.Kind,
                    debt.PrincipalAmount,
                    debt.AnnualNominalRate,
                    debt.AnnualEquivalentRate,
                    debt.TermMonths,
                    debt.InstallmentAmount,
                    debt.StartDate,
                    debt.Notes
                })
                .ToListAsync(ct);
            if (debts.Count == 0) return;

            var schedule = await _db.DebtScheduleItems.AsNoTracking()
                .Where(item => item.UserId == userId && item.Debt!.Status == DebtStatus.Active)
                .Select(item => new { item.DebtId, item.InstallmentNumber, item.IsPaid, item.RemainingPrincipal, item.DueDate })
                .ToListAsync(ct);
            var scheduleByDebt = schedule.ToLookup(item => item.DebtId);

            foreach (var debt in debts.OrderByDescending(debt => debt.InstallmentAmount).Take(MaxDebts))
            {
                var key = ServiceSavingsKeys.Debt(debt.Id);
                if (context.DismissedKeys.Contains(key)) continue;
                if (MentionsPasanaco(debt.Name) || MentionsPasanaco(debt.Lender)) continue;

                var items = scheduleByDebt[debt.Id].OrderBy(item => item.InstallmentNumber).ToList();
                var lastPaid = items.LastOrDefault(item => item.IsPaid);
                var outstanding = lastPaid?.RemainingPrincipal ?? debt.PrincipalAmount;
                var remaining = items.Count > 0
                    ? items.Count(item => !item.IsPaid)
                    : (int?)null;
                var endDate = items.Count > 0
                    ? items[^1].DueDate
                    : debt.StartDate.AddMonths(Math.Max(debt.TermMonths, 0));
                var monthly = debt.InstallmentAmount > 0 ? Round(debt.InstallmentAmount) : null;

                var name = FirstNonEmpty(debt.Name, debt.Lender, "Deuda bancaria");
                var reference = NextReference(context);
                context.Candidates.Add(new ServiceCandidate(key, reference, Shorten(name, 120), monthly)
                {
                    Prompt = new
                    {
                        key = reference,
                        type = "bankDebt",
                        kind = debt.Kind switch
                        {
                            DebtKind.CreditCard => "creditCard",
                            DebtKind.Mortgage => "mortgage",
                            _ => "loan"
                        },
                        name = Mask(debt.Name, 80),
                        lender = Mask(debt.Lender, 80),
                        outstanding = Round(outstanding),
                        nominalRatePercent = Round(debt.AnnualNominalRate),
                        aprPercent = Round(debt.AnnualEquivalentRate),
                        monthlyAmount = monthly,
                        remainingInstallments = remaining,
                        endDate = endDate.ToString("yyyy-MM"),
                        notes = Mask(debt.Notes, 200),
                        userComment = Comment(comments, key)
                    }
                });
            }
        }

        private async Task AddLegacyBankLoansAsync(
            Guid userId,
            ServiceSavingsContext context,
            IReadOnlyDictionary<string, string> comments,
            CancellationToken ct)
        {
            var loans = await _db.Loans.AsNoTracking()
                .Where(loan => loan.UserId == userId && loan.Type == LoanType.Bank && loan.PasanacoId == null)
                .Select(loan => new
                {
                    loan.Id,
                    loan.Name,
                    loan.Status,
                    loan.OutstandingAmount,
                    loan.InterestRate,
                    loan.TAE,
                    loan.InstallmentsRemaining,
                    loan.NextPaymentAmount,
                    loan.DueDate
                })
                .ToListAsync(ct);

            foreach (var loan in loans
                         .Where(loan => loan.OutstandingAmount > 0 &&
                                        !ClosedLoanStatuses.Contains((loan.Status ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
                         .OrderByDescending(loan => loan.NextPaymentAmount ?? 0)
                         .Take(MaxDebts))
            {
                var key = ServiceSavingsKeys.Loan(loan.Id);
                if (context.DismissedKeys.Contains(key) || MentionsPasanaco(loan.Name)) continue;

                var monthly = loan.NextPaymentAmount is > 0 ? Round(loan.NextPaymentAmount) : null;
                var reference = NextReference(context);
                var name = Shorten(FirstNonEmpty(loan.Name, "Préstamo bancario"), 120);
                context.Candidates.Add(new ServiceCandidate(key, reference, name, monthly)
                {
                    Prompt = new
                    {
                        key = reference,
                        type = "bankDebt",
                        kind = "loan",
                        name = Mask(loan.Name, 80),
                        outstanding = Round(loan.OutstandingAmount),
                        nominalRatePercent = Round(loan.InterestRate),
                        aprPercent = Round(loan.TAE),
                        monthlyAmount = monthly,
                        remainingInstallments = loan.InstallmentsRemaining,
                        endDate = loan.DueDate?.ToString("yyyy-MM"),
                        userComment = Comment(comments, key)
                    }
                });
            }
        }

        /// <summary>
        /// The rule a ledger group belongs to: the active rule most of its entries
        /// were generated from, or one with the same concept and description.
        /// </summary>
        private static RuleCandidate? FindRule(EntryGroup group, IReadOnlyDictionary<Guid, RuleCandidate> rules)
        {
            var voted = group.RuleVotes
                .Where(vote => rules.ContainsKey(vote.Key))
                .OrderByDescending(vote => vote.Value)
                .Select(vote => rules[vote.Key])
                .FirstOrDefault();
            if (voted is not null || group.Normalized.Length == 0) return voted;

            return rules.Values.FirstOrDefault(rule =>
                rule.Row.ConceptId == group.Concept.Id &&
                string.Equals(RevolutMovementClassifier.Normalize(rule.Row.Description), group.Normalized, StringComparison.Ordinal));
        }

        private static object DescribeHistory(PaymentHistory history, bool includeSample) => new
        {
            payments = history.Payments,
            distinctMonths = history.Months,
            firstDate = history.FirstDate.ToString("yyyy-MM-dd"),
            firstAmount = Round(history.FirstAmount),
            lastDate = history.LastDate.ToString("yyyy-MM-dd"),
            lastAmount = Round(history.LastAmount),
            averageAmount = Round(history.Total / history.Payments),
            sampleDescription = includeSample ? Mask(history.LastDescription, 120) : null
        };

        private static bool IsExcludedConcept(ConceptRow concept) =>
            concept.Kind != ConceptKind.Expense ||
            MentionsPasanaco(concept.Name) ||
            MentionsPasanaco(concept.GroupName);

        private static bool MentionsPasanaco(string? text) =>
            !string.IsNullOrWhiteSpace(text) &&
            RevolutMovementClassifier.Normalize(text).Contains("PASANACO", StringComparison.Ordinal);

        private static string? Comment(IReadOnlyDictionary<string, string> comments, string key) =>
            comments.TryGetValue(key, out var message) ? Mask(message, 500) : null;

        private static string? Mask(string? text, int maxLength)
        {
            var masked = SensitiveDataMasker.ForPrompt(text, maxLength);
            return masked.Length == 0 ? null : masked;
        }

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

        private static string Shorten(string text, int maxLength) =>
            text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";

        internal static decimal? Round(decimal? value) =>
            value is null ? null : Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);

        private static string FrequencyName(RecurrenceFrequency frequency) => frequency switch
        {
            RecurrenceFrequency.Bimonthly => "bimonthly",
            RecurrenceFrequency.Quarterly => "quarterly",
            RecurrenceFrequency.Yearly => "yearly",
            _ => "monthly"
        };

        private static int MonthsPerPayment(RecurrenceFrequency frequency) => frequency switch
        {
            RecurrenceFrequency.Bimonthly => 2,
            RecurrenceFrequency.Quarterly => 3,
            RecurrenceFrequency.Yearly => 12,
            _ => 1
        };

        private sealed record ConceptRow(Guid Id, string Name, ConceptKind Kind, ConceptNature Nature, string? GroupName);

        private sealed record RuleRow(
            Guid Id,
            Guid ConceptId,
            string? Description,
            RecurrenceFrequency Frequency,
            int DayOfMonth,
            decimal ForecastAmount,
            DateOnly? EndDate,
            bool IsActive);

        private sealed record EntryRow(Guid ConceptId, Guid? RecurringRuleId, string? Description, DateOnly Date, decimal Amount);

        private sealed class RuleCandidate
        {
            public RuleCandidate(RuleRow row, ConceptRow concept)
            {
                Row = row;
                Concept = concept;
            }

            public RuleRow Row { get; }

            public ConceptRow Concept { get; }

            public PaymentHistory History { get; } = new();

            public decimal? MonthlyAmount
            {
                get
                {
                    var perPayment = Row.ForecastAmount > 0
                        ? Row.ForecastAmount
                        : History.Payments > 0 ? History.LastAmount : 0;
                    return perPayment > 0 ? Round(perPayment / MonthsPerPayment(Row.Frequency)) : null;
                }
            }
        }

        private sealed class EntryGroup
        {
            public EntryGroup(ConceptRow concept, string normalized)
            {
                Concept = concept;
                Normalized = normalized;
            }

            public ConceptRow Concept { get; }

            public string Normalized { get; }

            public PaymentHistory History { get; } = new();

            public Dictionary<Guid, int> RuleVotes { get; } = new();
        }

        private sealed class PaymentHistory
        {
            private readonly List<(DateOnly Date, decimal Amount, string? Description)> _payments = new();

            public int Payments => _payments.Count;

            public decimal Total => _payments.Sum(payment => payment.Amount);

            public int Months => _payments.Select(payment => MonthIndex(payment.Date)).Distinct().Count();

            public DateOnly FirstDate => _payments.Min(payment => payment.Date);

            public decimal FirstAmount => _payments.OrderBy(payment => payment.Date).First().Amount;

            public DateOnly LastDate => _payments.Max(payment => payment.Date);

            public decimal LastAmount => _payments.OrderBy(payment => payment.Date).Last().Amount;

            public string? LastDescription => _payments
                .Where(payment => !string.IsNullOrWhiteSpace(payment.Description))
                .OrderBy(payment => payment.Date)
                .Select(payment => payment.Description)
                .LastOrDefault();

            public void Add(DateOnly date, decimal amount, string? description) =>
                _payments.Add((date, amount, description));

            public void AddRange(PaymentHistory other) => _payments.AddRange(other._payments);

            /// <summary>Months between payments, from the spread of the months with payments.</summary>
            private int? MonthsBetweenPayments()
            {
                var months = Months;
                if (months < 2) return null;

                var span = MonthIndex(LastDate) - MonthIndex(FirstDate);
                var gap = (decimal)span / (months - 1);
                return gap switch
                {
                    <= 1.5m => 1,
                    <= 2.5m => 2,
                    <= 4.5m => 3,
                    <= 9m => 6,
                    _ => 12
                };
            }

            public string EstimatedFrequency() => MonthsBetweenPayments() switch
            {
                1 => "monthly",
                2 => "bimonthly",
                3 => "quarterly",
                6 => "semiannual",
                12 => "yearly",
                _ => "unknown"
            };

            /// <summary>Average spent per month with payments, spread over the estimated period.</summary>
            public decimal? EstimatedMonthlyAmount()
            {
                var period = MonthsBetweenPayments();
                if (period is null) return null;
                return Round(Total / Months / period.Value);
            }

            private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;
        }
    }
}
