using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Settlements
{
    public interface ISettlementService
    {
        Task<IReadOnlyList<SettlementPersonDto>> GetPeopleAsync(Guid userId, CancellationToken ct);
        Task<SettlementPersonDto> SavePersonAsync(Guid userId, Guid? id, SavePersonDto dto, CancellationToken ct);
        Task<IReadOnlyList<SettlementSummaryDto>> ListAsync(Guid userId, CancellationToken ct);
        Task<SettlementDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct);
        Task<IReadOnlyList<SettlementLoanOptionDto>> GetLoanOptionsAsync(Guid userId, CancellationToken ct);
        Task<SettlementDetailDto> CreateAsync(Guid userId, CreateSettlementDto dto, CancellationToken ct);
        Task<SettlementDetailDto> UpdateAsync(Guid userId, Guid id, UpdateSettlementDto dto, CancellationToken ct);
        Task<SettlementDetailDto> LinkLoanAsync(Guid userId, Guid id, Guid? loanId, CancellationToken ct);
        Task<SettlementDetailDto> CloseLinkedLoanAsync(Guid userId, Guid id, string resolution, CancellationToken ct);
        Task DeleteAsync(Guid userId, Guid id, CancellationToken ct);
        Task<SettlementDetailDto> AddLineAsync(Guid userId, Guid id, SaveLineDto dto, CancellationToken ct);
        Task<SettlementDetailDto> UpdateLineAsync(Guid userId, Guid id, Guid lineId, SaveLineDto dto, CancellationToken ct);
        Task<SettlementDetailDto> DeleteLineAsync(Guid userId, Guid id, Guid lineId, CancellationToken ct);
        Task<SettlementDetailDto> AddFromEntryAsync(Guid userId, Guid id, AddFromEntryDto dto, CancellationToken ct);
        Task<SettlementDetailDto> AssignEntryAsync(Guid userId, AssignEntryDto dto, CancellationToken ct);
        Task<IReadOnlyList<SettlementCandidateDto>> GetCandidatesAsync(Guid userId, Guid id, CancellationToken ct);
        Task<SettlementDetailDto> MarkSentAsync(Guid userId, Guid id, CancellationToken ct);
        Task<SettlementDetailDto> ReopenAsync(Guid userId, Guid id, CancellationToken ct);
    }

    /// <summary>
    /// Gestiona las cuentas de gastos compartidos: qué gastos le corresponden a
    /// cada persona, cuánto ha pagado ya y el mensaje que se le manda.
    /// </summary>
    public sealed class SettlementService : ISettlementService
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        public SettlementService(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<IReadOnlyList<SettlementPersonDto>> GetPeopleAsync(Guid userId, CancellationToken ct)
        {
            var people = await _db.Counterparties
                .Where(c => c.UserId == userId && c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync(ct);

            var openByPerson = await _db.Settlements
                .Where(s => s.UserId == userId && s.Status == SettlementStatus.Draft)
                .GroupBy(s => s.CounterpartyId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(ct);

            return people
                .Select(c => new SettlementPersonDto(
                    c.Id,
                    c.Name,
                    c.PhoneNumber,
                    openByPerson.FirstOrDefault(o => o.Key == c.Id)?.Count ?? 0))
                .ToList();
        }

        public async Task<SettlementPersonDto> SavePersonAsync(
            Guid userId, Guid? id, SavePersonDto dto, CancellationToken ct)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new BusinessRuleException("La persona necesita un nombre.");

            Counterparty person;
            if (id is { } personId)
            {
                person = await _db.Counterparties
                    .FirstOrDefaultAsync(c => c.Id == personId && c.UserId == userId, ct)
                    ?? throw new NotFoundException("No encuentro esa persona.");
                person.Name = name;
            }
            else
            {
                var existing = await _db.Counterparties
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == name, ct);

                if (existing is null)
                {
                    person = new Counterparty { UserId = userId, Name = name, IsActive = true };
                    _db.Counterparties.Add(person);
                }
                else
                {
                    person = existing;
                    person.IsActive = true;
                }
            }

            person.PhoneNumber = NormalizePhone(dto.PhoneNumber);
            await _db.SaveChangesAsync(ct);

            return new SettlementPersonDto(person.Id, person.Name, person.PhoneNumber, 0);
        }

        public async Task<IReadOnlyList<SettlementSummaryDto>> ListAsync(Guid userId, CancellationToken ct)
        {
            var settlements = await _db.Settlements
                .Include(s => s.Lines)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.PeriodEnd)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            var names = await PersonNamesAsync(userId, ct);
            var loansById = await LoadLinkedLoansAsync(
                userId,
                settlements.Where(s => s.LinkedLoanId.HasValue).Select(s => s.LinkedLoanId!.Value),
                ct);

            return settlements
                .Select(s => new SettlementSummaryDto(
                    s.Id,
                    s.CounterpartyId,
                    names.TryGetValue(s.CounterpartyId, out var name) ? name : "Sin nombre",
                    s.Title,
                    s.PeriodStart,
                    s.PeriodEnd,
                    s.Status.ToString(),
                    SettlementMessageBuilder.Totals(s, EffectiveLoanBalance(s, loansById)).Pending,
                    s.Lines.Count + (EffectiveLoanBalance(s, loansById) > 0m ? 1 : 0),
                    s.SentAt))
                .ToList();
        }

        public async Task<SettlementDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct) =>
            await ToDetailAsync(userId, await LoadAsync(userId, id, ct), ct);

        public async Task<IReadOnlyList<SettlementLoanOptionDto>> GetLoanOptionsAsync(
            Guid userId, CancellationToken ct)
        {
            var loans = await _db.Loans
                .Where(loan => loan.UserId == userId
                    && loan.Type == PersonalFinance.Api.Models.Enums.LoanType.Received)
                .OrderBy(loan => loan.Name)
                .ToListAsync(ct);

            return loans
                .Select(loan => new SettlementLoanOptionDto(
                    loan.Id,
                    loan.Name,
                    GetActiveLoanBalance(loan),
                    loan.Status))
                .ToList();
        }

        public async Task<SettlementDetailDto> CreateAsync(
            Guid userId, CreateSettlementDto dto, CancellationToken ct)
        {
            var person = await _db.Counterparties
                .FirstOrDefaultAsync(c => c.Id == dto.CounterpartyId && c.UserId == userId, ct)
                ?? throw new NotFoundException("No encuentro esa persona.");

            if (dto.PeriodEnd < dto.PeriodStart)
                throw new BusinessRuleException("La fecha final no puede ser anterior a la inicial.");

            var settlement = new Settlement
            {
                UserId = userId,
                CounterpartyId = person.Id,
                Title = string.IsNullOrWhiteSpace(dto.Title)
                    ? DefaultTitle(dto.PeriodStart, dto.PeriodEnd)
                    : dto.Title.Trim(),
                PeriodStart = dto.PeriodStart,
                PeriodEnd = dto.PeriodEnd,
                CarriedOverAmount = dto.CarriedOverAmount ?? 0m,
                LinkedLoanId = await FindPreviousLinkedLoanIdAsync(userId, person.Id, ct),
                ClosingNote = string.IsNullOrWhiteSpace(dto.ClosingNote)
                    ? "Si hay algo que no se entienda o hay dudas, me avisas."
                    : dto.ClosingNote.Trim(),
                CreatedAt = _clock.UtcNow
            };

            _db.Settlements.Add(settlement);
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> UpdateAsync(
            Guid userId, Guid id, UpdateSettlementDto dto, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);

            if (dto.Title is { } title && !string.IsNullOrWhiteSpace(title))
                settlement.Title = title.Trim();
            if (dto.PeriodStart is { } start) settlement.PeriodStart = start;
            if (dto.PeriodEnd is { } end) settlement.PeriodEnd = end;
            if (dto.CarriedOverAmount is { } carried) settlement.CarriedOverAmount = carried;
            if (dto.ClosingNote is not null)
                settlement.ClosingNote = string.IsNullOrWhiteSpace(dto.ClosingNote) ? null : dto.ClosingNote.Trim();

            if (settlement.PeriodEnd < settlement.PeriodStart)
                throw new BusinessRuleException("La fecha final no puede ser anterior a la inicial.");

            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> LinkLoanAsync(
            Guid userId, Guid id, Guid? loanId, CancellationToken ct)
        {
            var settlement = await LoadEditableAsync(userId, id, ct);
            if (loanId.HasValue)
            {
                var loan = await _db.Loans.FirstOrDefaultAsync(
                    candidate => candidate.Id == loanId.Value
                        && candidate.UserId == userId
                        && candidate.Type == PersonalFinance.Api.Models.Enums.LoanType.Received,
                    ct);
                if (loan is null)
                    throw new NotFoundException("No encuentro ese préstamo recibido.");

                settlement.LinkedLoanId = loan.Id;
                settlement.LinkedLoanBalanceSnapshot = null;
            }
            else
            {
                settlement.LinkedLoanId = null;
                settlement.LinkedLoanBalanceSnapshot = null;
            }

            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> CloseLinkedLoanAsync(
            Guid userId, Guid id, string resolution, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);
            if (!settlement.LinkedLoanId.HasValue)
                throw new BusinessRuleException("Esta liquidación no tiene un préstamo asociado.");

            var loan = await _db.Loans.FirstOrDefaultAsync(
                candidate => candidate.Id == settlement.LinkedLoanId.Value && candidate.UserId == userId,
                ct);
            if (loan is null)
                throw new NotFoundException("No encuentro el préstamo asociado.");

            switch ((resolution ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "settled":
                    loan.OutstandingAmount = 0m;
                    loan.Status = "paid";
                    break;
                case "cancelled":
                    loan.OutstandingAmount = 0m;
                    loan.Status = "cancelled";
                    break;
                default:
                    throw new BusinessRuleException("Indica si la deuda se saldó o se canceló.");
            }

            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);
            _db.Settlements.Remove(settlement);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<SettlementDetailDto> AddLineAsync(
            Guid userId, Guid id, SaveLineDto dto, CancellationToken ct)
        {
            var settlement = await LoadEditableAsync(userId, id, ct);
            var line = BuildLine(settlement, dto);
            settlement.Lines.Add(line);
            _db.SettlementLines.Add(line);
            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> UpdateLineAsync(
            Guid userId, Guid id, Guid lineId, SaveLineDto dto, CancellationToken ct)
        {
            var settlement = await LoadEditableAsync(userId, id, ct);
            var line = settlement.Lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new NotFoundException("No encuentro esa línea.");

            line.Kind = ParseKind(dto.Kind);
            line.Description = CleanDescription(dto.Description);
            line.Amount = Round(Math.Abs(dto.Amount));
            line.FullAmount = dto.FullAmount is { } full ? Round(Math.Abs(full)) : null;
            line.LedgerEntryId = dto.LedgerEntryId;

            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> DeleteLineAsync(
            Guid userId, Guid id, Guid lineId, CancellationToken ct)
        {
            var settlement = await LoadEditableAsync(userId, id, ct);
            var line = settlement.Lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new NotFoundException("No encuentro esa línea.");

            settlement.Lines.Remove(line);
            _db.SettlementLines.Remove(line);
            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> AddFromEntryAsync(
            Guid userId, Guid id, AddFromEntryDto dto, CancellationToken ct)
        {
            var settlement = await LoadEditableAsync(userId, id, ct);
            await AddEntryLineAsync(userId, settlement, dto.LedgerEntryId, dto.Kind, dto.SharePercent, dto.Amount, ct);
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> AssignEntryAsync(
            Guid userId, AssignEntryDto dto, CancellationToken ct)
        {
            var person = await _db.Counterparties
                .FirstOrDefaultAsync(c => c.Id == dto.CounterpartyId && c.UserId == userId, ct)
                ?? throw new NotFoundException("No encuentro esa persona.");

            var settlement = await _db.Settlements
                .Include(s => s.Lines)
                .Where(s => s.UserId == userId
                    && s.CounterpartyId == person.Id
                    && s.Status == SettlementStatus.Draft)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (settlement is null)
            {
                var today = _clock.Today;
                var start = new DateOnly(today.Year, today.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);

                settlement = new Settlement
                {
                    UserId = userId,
                    CounterpartyId = person.Id,
                    Title = DefaultTitle(start, end),
                    PeriodStart = start,
                    PeriodEnd = end,
                    LinkedLoanId = await FindPreviousLinkedLoanIdAsync(userId, person.Id, ct),
                    ClosingNote = "Si hay algo que no se entienda o hay dudas, me avisas.",
                    CreatedAt = _clock.UtcNow
                };
                _db.Settlements.Add(settlement);
            }

            await AddEntryLineAsync(userId, settlement, dto.LedgerEntryId, dto.Kind, dto.SharePercent, dto.Amount, ct);
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<IReadOnlyList<SettlementCandidateDto>> GetCandidatesAsync(
            Guid userId, Guid id, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);

            var used = await _db.SettlementLines
                .Where(l => l.Settlement!.UserId == userId && l.LedgerEntryId != null)
                .Select(l => l.LedgerEntryId!.Value)
                .ToListAsync(ct);

            var entries = await _db.LedgerEntries
                .Include(e => e.Concept)
                .Where(e => e.UserId == userId
                    && !e.IsTransfer
                    && e.Status != EntryStatus.Skipped)
                .ToListAsync(ct);

            return entries
                .Where(e => InPeriod(e, settlement))
                .OrderBy(e => EffectiveDate(e))
                .Select(e => new SettlementCandidateDto(
                    e.Id,
                    EffectiveDate(e),
                    string.IsNullOrWhiteSpace(e.Description)
                        ? e.Concept?.Name ?? "Movimiento"
                        : e.Description!,
                    e.Concept?.Name ?? "Sin concepto",
                    Round(e.EffectiveAmount),
                    e.Direction == EntryDirection.In ? "In" : "Out",
                    used.Contains(e.Id)))
                .ToList();
        }

        public async Task<SettlementDetailDto> MarkSentAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);
            if (settlement.LinkedLoanId.HasValue)
            {
                var loan = await _db.Loans.FirstOrDefaultAsync(
                    candidate => candidate.Id == settlement.LinkedLoanId.Value
                        && candidate.UserId == userId,
                    ct);
                settlement.LinkedLoanBalanceSnapshot = loan is null
                    ? 0m
                    : GetActiveLoanBalance(loan);
            }
            settlement.Status = SettlementStatus.Sent;
            settlement.SentAt = _clock.UtcNow;
            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        public async Task<SettlementDetailDto> ReopenAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);
            settlement.Status = SettlementStatus.Draft;
            settlement.SentAt = null;
            settlement.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await ToDetailAsync(userId, settlement, ct);
        }

        private async Task AddEntryLineAsync(
            Guid userId,
            Settlement settlement,
            Guid entryId,
            string? kind,
            decimal? sharePercent,
            decimal? amount,
            CancellationToken ct)
        {
            var entry = await _db.LedgerEntries
                .Include(e => e.Concept)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, ct)
                ?? throw new NotFoundException("No encuentro ese movimiento.");

            if (settlement.Lines.Any(l => l.LedgerEntryId == entryId))
                throw new BusinessRuleException("Ese movimiento ya está en la liquidación.");

            var full = Round(Math.Abs(entry.EffectiveAmount));
            var share = ResolveShare(full, sharePercent, amount);

            var resolvedKind = kind is null
                ? (entry.Direction == EntryDirection.In
                    ? SettlementLineKind.Payment
                    : SettlementLineKind.Charge)
                : ParseKind(kind);

            var line = new SettlementLine
            {
                SettlementId = settlement.Id,
                Kind = resolvedKind,
                Description = CleanDescription(
                    string.IsNullOrWhiteSpace(entry.Description)
                        ? entry.Concept?.Name
                        : entry.Description),
                Amount = share,
                FullAmount = share == full ? null : full,
                LedgerEntryId = entry.Id,
                SortOrder = NextSortOrder(settlement, resolvedKind),
                CreatedAt = _clock.UtcNow
            };

            settlement.Lines.Add(line);
            _db.SettlementLines.Add(line);
            settlement.UpdatedAt = _clock.UtcNow;
        }

        /// <summary>
        /// Qué parte del gasto le toca: un importe fijo si se indica, si no un
        /// porcentaje, y si no el gasto entero.
        /// </summary>
        private static decimal ResolveShare(decimal full, decimal? sharePercent, decimal? amount)
        {
            if (amount is { } fixedAmount && fixedAmount > 0m)
                return Round(Math.Min(Math.Abs(fixedAmount), full));

            if (sharePercent is { } percent && percent > 0m)
                return Round(full * Math.Min(percent, 100m) / 100m);

            return full;
        }

        private SettlementLine BuildLine(Settlement settlement, SaveLineDto dto)
        {
            var kind = ParseKind(dto.Kind);
            var amount = Round(Math.Abs(dto.Amount));
            if (amount <= 0m)
                throw new BusinessRuleException("El importe tiene que ser mayor que cero.");

            return new SettlementLine
            {
                SettlementId = settlement.Id,
                Kind = kind,
                Description = CleanDescription(dto.Description),
                Amount = amount,
                FullAmount = dto.FullAmount is { } full ? Round(Math.Abs(full)) : null,
                LedgerEntryId = dto.LedgerEntryId,
                SortOrder = NextSortOrder(settlement, kind),
                CreatedAt = _clock.UtcNow
            };
        }

        /// <summary>Las líneas se agrupan por bloque: gastos, restas y pagos.</summary>
        private static int NextSortOrder(Settlement settlement, SettlementLineKind kind)
        {
            var block = (int)kind * 1000;
            var inBlock = settlement.Lines.Count(l => l.Kind == kind);
            return block + inBlock;
        }

        private static bool InPeriod(LedgerEntry entry, Settlement settlement)
        {
            var date = EffectiveDate(entry);
            return date >= settlement.PeriodStart && date <= settlement.PeriodEnd;
        }

        private static DateOnly EffectiveDate(LedgerEntry entry) => entry.ValueDate ?? entry.DueDate;

        private async Task<Settlement> LoadAsync(Guid userId, Guid id, CancellationToken ct) =>
            await _db.Settlements
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct)
            ?? throw new NotFoundException("No encuentro esa liquidación.");

        private async Task<Settlement> LoadEditableAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var settlement = await LoadAsync(userId, id, ct);
            if (settlement.Status != SettlementStatus.Draft)
                throw new BusinessRuleException(
                    "La liquidación ya se envió. Reábrela si quieres cambiarla.");

            return settlement;
        }

        private async Task<Dictionary<Guid, string>> PersonNamesAsync(Guid userId, CancellationToken ct) =>
            await _db.Counterparties
                .Where(c => c.UserId == userId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        private async Task<Guid?> FindPreviousLinkedLoanIdAsync(
            Guid userId, Guid counterpartyId, CancellationToken ct) =>
            await _db.Settlements
                .Where(s => s.UserId == userId
                    && s.CounterpartyId == counterpartyId
                    && s.LinkedLoanId.HasValue)
                .OrderByDescending(s => s.PeriodEnd)
                .ThenByDescending(s => s.CreatedAt)
                .Select(s => s.LinkedLoanId)
                .FirstOrDefaultAsync(ct);

        private async Task<Dictionary<Guid, PersonalFinance.Api.Models.Entities.Loan>> LoadLinkedLoansAsync(
            Guid userId, IEnumerable<Guid> loanIds, CancellationToken ct)
        {
            var ids = loanIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, PersonalFinance.Api.Models.Entities.Loan>();

            return await _db.Loans
                .Where(loan => loan.UserId == userId && ids.Contains(loan.Id))
                .ToDictionaryAsync(loan => loan.Id, ct);
        }

        private static decimal GetActiveLoanBalance(
            Guid? loanId,
            IReadOnlyDictionary<Guid, PersonalFinance.Api.Models.Entities.Loan> loansById) =>
            loanId.HasValue && loansById.TryGetValue(loanId.Value, out var loan)
                ? GetActiveLoanBalance(loan)
                : 0m;

        private static decimal EffectiveLoanBalance(
            Settlement settlement,
            IReadOnlyDictionary<Guid, PersonalFinance.Api.Models.Entities.Loan> loansById) =>
            settlement.Status != SettlementStatus.Draft
                && settlement.LinkedLoanBalanceSnapshot.HasValue
                    ? settlement.LinkedLoanBalanceSnapshot.Value
                    : GetActiveLoanBalance(settlement.LinkedLoanId, loansById);

        private static decimal GetActiveLoanBalance(PersonalFinance.Api.Models.Entities.Loan loan) =>
            loan.Status.Equals("paid", StringComparison.OrdinalIgnoreCase)
                || loan.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
                ? 0m
                : Math.Max(0m, loan.OutstandingAmount);

        private async Task<SettlementDetailDto> ToDetailAsync(
            Guid userId, Settlement settlement, CancellationToken ct)
        {
            var person = await _db.Counterparties
                .FirstOrDefaultAsync(c => c.Id == settlement.CounterpartyId && c.UserId == userId, ct);

            PersonalFinance.Api.Models.Entities.Loan? linkedLoan = null;
            if (settlement.LinkedLoanId.HasValue)
            {
                linkedLoan = await _db.Loans.FirstOrDefaultAsync(
                    loan => loan.Id == settlement.LinkedLoanId.Value && loan.UserId == userId,
                    ct);
            }

            var loanBalance = settlement.Status != SettlementStatus.Draft
                && settlement.LinkedLoanBalanceSnapshot.HasValue
                    ? settlement.LinkedLoanBalanceSnapshot.Value
                    : linkedLoan is null ? 0m : GetActiveLoanBalance(linkedLoan);
            var totals = SettlementMessageBuilder.Totals(settlement, loanBalance);
            var message = SettlementMessageBuilder.Build(settlement, person?.Name, loanBalance);

            return new SettlementDetailDto(
                settlement.Id,
                settlement.CounterpartyId,
                person?.Name ?? "Sin nombre",
                person?.PhoneNumber,
                settlement.Title,
                settlement.PeriodStart,
                settlement.PeriodEnd,
                settlement.Status.ToString(),
                settlement.ClosingNote,
                settlement.CarriedOverAmount,
                settlement.SentAt,
                settlement.LinkedLoanId,
                linkedLoan is null
                    ? null
                    : new SettlementLoanDto(
                        linkedLoan.Id,
                        linkedLoan.Name,
                        GetActiveLoanBalance(linkedLoan),
                        linkedLoan.Status,
                        loanBalance),
                new SettlementTotalsDto(
                    totals.Charges, totals.Deductions, totals.Payments, totals.CarriedOver, totals.Pending),
                settlement.Lines
                    .OrderBy(l => l.SortOrder)
                    .ThenBy(l => l.CreatedAt)
                    .Select(l => new SettlementLineDto(
                        l.Id, l.Kind.ToString(), l.Description, l.Amount, l.FullAmount, l.LedgerEntryId, l.SortOrder))
                    .ToList(),
                message,
                WhatsAppLink.Build(person?.PhoneNumber, message));
        }

        private static string DefaultTitle(DateOnly start, DateOnly end)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            var from = culture.DateTimeFormat.GetMonthName(start.Month).ToUpperInvariant();

            if (start.Year == end.Year && start.Month == end.Month)
                return $"CUENTAS {from} {start.Year}";

            var to = culture.DateTimeFormat.GetMonthName(end.Month).ToUpperInvariant();
            return start.Year == end.Year
                ? $"CUENTAS {from}/{to} {end.Year}"
                : $"CUENTAS {from} {start.Year}/{to} {end.Year}";
        }

        private static string CleanDescription(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return "Sin concepto";
            return text.Length > 200 ? text[..200] : text;
        }

        private static SettlementLineKind ParseKind(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "charge" or "gasto" => SettlementLineKind.Charge,
            "deduction" or "restar" => SettlementLineKind.Deduction,
            "payment" or "pago" => SettlementLineKind.Payment,
            _ => throw new BusinessRuleException("Tipo de línea desconocido.")
        };

        private static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            return digits.Length == 0 ? null : digits;
        }

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
