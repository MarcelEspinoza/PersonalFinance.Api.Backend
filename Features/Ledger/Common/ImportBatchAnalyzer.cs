using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Revisión automática de un extracto, como la haría una persona con el banco delante:
    /// ¿cuadra el saldo?, ¿algún cobro no casa con lo previsto, se pagó por adelantado o
    /// parece repetido? No cambia nada; sólo avisa. El chat del importador la recibe como
    /// contexto para poder explicarla.
    /// </summary>
    public static class ImportBatchAnalyzer
    {
        public const string SeverityOk = "ok";
        public const string SeverityInfo = "info";
        public const string SeverityWarning = "warning";

        /// <summary>Un gasto de los últimos días del mes puede ser el del mes siguiente pagado antes.</summary>
        private const int MonthEndDays = 7;
        private const int DuplicateWindowDays = 3;
        private const decimal AdvanceAmountTolerance = 0.15m;
        private const decimal UnplannedIncomeThreshold = 100m;
        private const decimal Cent = 0.01m;

        private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
        private static readonly Regex StatementRange = new(
            @"(\d{4}-\d{2}-\d{2})_(\d{4}-\d{2}-\d{2})",
            RegexOptions.Compiled);

        public sealed record BalanceCheck(ImportFindingDto Finding, decimal? AppBalance, decimal? Difference);

        private sealed record Forecast(DateOnly DueDate, decimal Amount, Guid? AccountId, Guid? EntryId, Guid? RuleId)
        {
            public string Key => ForecastKey(EntryId, RuleId, DueDate);
        }

        /// <summary>
        /// Saldo con el que el banco cierra el extracto: el de la cuenta corriente tras el
        /// último movimiento completado. Si varios se completaron en el mismo instante, el
        /// último es aquel cuyo saldo no sirve de punto de partida a ningún otro.
        /// </summary>
        public static (decimal Balance, DateOnly Date)? FindClosingBalance(
            IEnumerable<RevolutMovement> movements, string? currency)
        {
            ArgumentNullException.ThrowIfNull(movements);

            var completed = movements
                .Where(movement => movement.Balance is not null && movement.CompletedAt is not null)
                .Where(movement =>
                    string.IsNullOrWhiteSpace(currency) ||
                    string.Equals(movement.Currency.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(movement => RevolutMovementClassifier.Classify(movement) is
                {
                    Disposition: MovementDisposition.Import,
                    Status: EntryStatus.Paid
                })
                .ToList();
            if (completed.Count == 0) return null;

            var lastAt = completed.Max(movement => movement.CompletedAt!.Value);
            var tied = completed.Where(movement => movement.CompletedAt == lastAt).ToList();
            var last = tied.FirstOrDefault(candidate => !tied.Any(other =>
                           !ReferenceEquals(other, candidate) &&
                           other.Balance == candidate.Balance + other.Amount - other.Fee))
                       ?? tied[^1];
            return (last.Balance!.Value, DateOnly.FromDateTime(lastAt));
        }

        /// <summary>
        /// Saldo con el que debe cuadrar la app: el de cierre del extracto más los pagos que el banco aún
        /// tiene pendientes. La app ya los cuenta como pagados (el dinero está apartado), pero el «Saldo»
        /// del extracto todavía no los incluye. La fecha llega hasta el último de esos pagos.
        /// </summary>
        public static (decimal Balance, DateOnly Date)? FindBankBalance(
            IEnumerable<RevolutMovement> movements, string? currency)
        {
            ArgumentNullException.ThrowIfNull(movements);

            var list = movements.ToList();
            if (FindClosingBalance(list, currency) is not { } closing) return null;

            var pending = list
                .Where(movement =>
                    string.IsNullOrWhiteSpace(currency) ||
                    string.Equals(movement.Currency.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(movement => RevolutMovementClassifier.Classify(movement) is
                {
                    Disposition: MovementDisposition.Import,
                    Status: EntryStatus.Pending
                })
                .ToList();
            if (pending.Count == 0) return closing;

            var lastPending = pending.Max(movement => DateOnly.FromDateTime(movement.StartedAt));
            return (
                closing.Balance + pending.Sum(movement => movement.Amount - movement.Fee),
                lastPending > closing.Date ? lastPending : closing.Date);
        }

        public static async Task<ImportAnalysisDto> AnalyzeAsync(
            IAppDbContext db,
            IImportBatchApplicationService application,
            ImportBatch batch,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(db);
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(batch);

            var analysis = new ImportAnalysisDto
            {
                BankBalance = batch.BankBalance,
                BankBalanceDate = batch.BankBalanceDate
            };
            var account = batch.AccountId is null
                ? null
                : await db.Accounts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == batch.AccountId && item.UserId == batch.UserId, ct);
            if (account is null) return analysis;

            var applied = batch.Status == ImportBatchStatus.Applied;
            var pendingRows = applied
                ? new List<ImportRow>()
                : batch.Rows
                    .Where(row => row.Status == ImportRowStatus.Pending)
                    .OrderBy(row => row.RowNumber)
                    .ToList();

            var findings = new List<ImportFindingDto>();
            if (batch.BankBalance is { } bankBalance && batch.BankBalanceDate is { } bankDate)
            {
                // Lo que este extracto aún va a sumar al saldo cuando se aplique.
                var pendingEffect = pendingRows
                    .Where(ImportBatchApplicationService.EntersAsPaid)
                    .Where(row => SettledOn(row) >= account.OpeningDate && SettledOn(row) <= bankDate)
                    .Sum(row => row.Amount - row.Fee);
                var check = await CheckBalanceAsync(
                    db, account, bankBalance, bankDate, pendingEffect, applied, batch.FileName, ct);
                analysis.AppBalance = check.AppBalance;
                analysis.Difference = check.Difference;
                findings.Add(check.Finding);
            }
            else
            {
                findings.Add(Finding(
                    SeverityInfo,
                    "No puedo comprobar el saldo",
                    "Este extracto no trae el saldo final del banco (o se subió antes de que existiera esta revisión), " +
                    "así que no puedo decirte si cuadra."));
            }

            var others = new List<ImportFindingDto>();
            if (pendingRows.Count > 0)
                others.AddRange(await AnalyzeRowsAsync(db, application, batch, account, pendingRows, ct));
            if (!applied && batch.DuplicateRows > 0)
            {
                others.Add(Finding(
                    SeverityInfo,
                    "Ya estaban en la app",
                    $"{Plural(batch.DuplicateRows, "movimiento del archivo ya estaba importado", "movimientos del archivo ya estaban importados")} " +
                    "y no se vuelven a meter. Es normal si este extracto se solapa con el anterior."));
            }
            if (batch.ExcludedRows > 0)
            {
                others.Add(Finding(
                    SeverityInfo,
                    "Movimientos que no se importan",
                    $"{Plural(batch.ExcludedRows, "movimiento no se importa", "movimientos no se importan")}: " +
                    "los que el banco devolvió y los de otros productos de Revolut (como la hucha), " +
                    "que ya se cuentan en la cuenta principal."));
            }

            // Primero lo que pide hacer algo; el orden original se mantiene dentro de cada tipo.
            findings.AddRange(others.OrderBy(finding => finding.Severity == SeverityWarning ? 0 : 1));
            analysis.Findings = findings;
            return analysis;
        }

        /// <summary>
        /// Compara el saldo del banco con el de la app en la misma fecha. <paramref name="pendingEffect"/>
        /// es lo que aún sumará el extracto si no está aplicado.
        /// </summary>
        public static async Task<BalanceCheck> CheckBalanceAsync(
            IAppDbContext db,
            Account account,
            decimal bankBalance,
            DateOnly bankDate,
            decimal pendingEffect,
            bool applied,
            string? fileName,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(db);
            ArgumentNullException.ThrowIfNull(account);

            if (bankDate < account.OpeningDate)
            {
                return new(Finding(
                    SeverityInfo,
                    "Extracto anterior al inicio de la cuenta",
                    $"El extracto termina el {Day(bankDate)}, antes del {Day(account.OpeningDate)}, que es cuando " +
                    $"empieza a contar «{account.Name}» en la app. No comparo saldos de antes de esa fecha."),
                    null,
                    null);
            }

            var entries = await db.LedgerEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.UserId == account.UserId &&
                    entry.AccountId == account.Id &&
                    entry.Status == EntryStatus.Paid &&
                    entry.ValueDate != null &&
                    entry.ValueDate <= bankDate)
                .ToListAsync(ct);
            var appBalance = AccountBalanceCalculator.ComputeBalance(account, entries, bankDate) + pendingEffect;
            var difference = bankBalance - appBalance;
            var appSays = applied ? "la app tiene" : "la app tendrá, cuando apliques este extracto,";

            if (Math.Abs(difference) < Cent)
            {
                return new(Finding(
                    SeverityOk,
                    "El saldo cuadra con el banco",
                    $"El {Day(bankDate)} el banco dice {Money(bankBalance)} en «{account.Name}» y {appSays} lo mismo."),
                    appBalance,
                    0m);
            }

            var gap = difference > 0
                ? $"La app marca {Money(difference)} menos que el banco."
                : $"La app marca {Money(-difference)} más que el banco.";
            var start = StatementStart(fileName);
            var startHint = start is { Day: > 1 } statementStart
                ? $" Este extracto empieza el {Day(statementStart)}: expórtalo desde el día 1 del mes."
                : " Exporta siempre desde el día 1 del mes.";
            var detail =
                $"El {Day(bankDate)} el banco dice {Money(bankBalance)} en «{account.Name}», pero {appSays} " +
                $"{Money(appBalance)}. {gap} Causas típicas: 1) falta algún movimiento porque el extracto no empieza " +
                $"justo donde terminó el anterior.{startHint} 2) El saldo inicial de la cuenta no es el correcto. " +
                "3) Hay apuntes hechos a mano en esta cuenta que el banco no tiene, o con otra fecha.";

            // Un apunte manual que explica justo la diferencia es la pista más útil.
            var suspect = entries
                .Where(entry =>
                    entry.ImportRowId is null &&
                    entry.ValueDate >= account.OpeningDate &&
                    Math.Abs(entry.SignedEffectiveAmount + difference) < Cent)
                .OrderByDescending(entry => entry.ValueDate)
                .FirstOrDefault();
            if (suspect is not null)
            {
                detail +=
                    $" Pista: el apunte «{suspect.Description ?? "sin descripción"}» del {Day(suspect.ValueDate!.Value)} " +
                    $"({Money(suspect.SignedEffectiveAmount)}) es justo la diferencia. ¿Lo apuntaste a mano y no pasó por este banco?";
            }

            return new(Finding(SeverityWarning, "El saldo no cuadra con el banco", detail), appBalance, difference);
        }

        /// <summary>Texto compacto de la revisión para dárselo al asistente del importador.</summary>
        public static IReadOnlyList<string> ToChatLines(ImportAnalysisDto analysis)
        {
            ArgumentNullException.ThrowIfNull(analysis);
            return analysis.Findings
                .Select(finding => $"[{finding.Severity}] {finding.Title}: {finding.Detail}")
                .ToList();
        }

        private static async Task<List<ImportFindingDto>> AnalyzeRowsAsync(
            IAppDbContext db,
            IImportBatchApplicationService application,
            ImportBatch batch,
            Account account,
            IReadOnlyList<ImportRow> rows,
            CancellationToken ct)
        {
            var findings = new List<ImportFindingDto>();
            var userId = batch.UserId;

            var unassigned = rows.Where(row => row.Allocations.Count == 0 && ConceptOf(row) is null).ToList();
            if (unassigned.Count > 0)
            {
                findings.Add(Finding(
                    SeverityWarning,
                    "Movimientos sin concepto",
                    $"{Plural(unassigned.Count, "movimiento no tiene", "movimientos no tienen")} concepto. " +
                    "Elige uno en la lista (o pídeselo al asistente) antes de aplicar.",
                    unassigned));
            }

            var bankPending = rows.Where(row => row.ClassifiedStatus == EntryStatus.Pending).ToList();
            if (bankPending.Count > 0)
            {
                findings.Add(Finding(
                    SeverityInfo,
                    "Pendientes en el banco",
                    $"{Plural(bankPending.Count, "movimiento sigue pendiente", "movimientos siguen pendientes")} en el banco. " +
                    "El dinero ya está apartado, así que entran como pagados y el saldo de arriba ya los tiene en cuenta. " +
                    "Si el banco anula alguno, el saldo dejará de cuadrar y te avisaré.",
                    bankPending));
            }

            var guessed = rows
                .Where(row =>
                    row.Allocations.Count == 0 &&
                    row.ConfirmedConceptId is null &&
                    row.SuggestedConceptId is not null &&
                    row.SuggestionSource is "ai" or "regla general")
                .ToList();
            if (guessed.Count > 0)
            {
                findings.Add(Finding(
                    SeverityInfo,
                    "Conceptos adivinados",
                    $"{Plural(guessed.Count, "movimiento tiene", "movimientos tienen")} un concepto que adivinó la IA o una regla general. " +
                    "Échales un vistazo antes de aplicar; si alguno está mal, cámbialo o díselo al asistente.",
                    guessed));
            }

            var concepts = await db.Concepts
                .AsNoTracking()
                .Where(concept => concept.UserId == userId)
                .ToDictionaryAsync(concept => concept.Id, ct);
            var accountNames = await db.Accounts
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .ToDictionaryAsync(item => item.Id, item => item.Name, ct);

            var firstDay = rows.Min(row => row.ValueDate);
            var lastDay = rows.Max(row => SettledOn(row) > row.ValueDate ? SettledOn(row) : row.ValueDate);
            // Desde el mes anterior: la nómina de este mes pudo cobrarse a finales del anterior.
            var from = new DateOnly(firstDay.Year, firstDay.Month, 1).AddMonths(-1);
            var to = new DateOnly(lastDay.Year, lastDay.Month, 1).AddMonths(2);
            var entries = await db.LedgerEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.UserId == userId &&
                    ((entry.DueDate >= from && entry.DueDate < to) ||
                     (entry.ValueDate != null && entry.ValueDate >= from && entry.ValueDate < to)))
                .ToListAsync(ct);
            var rules = await db.RecurringRules
                .AsNoTracking()
                .Where(rule => rule.UserId == userId && rule.IsActive)
                .ToListAsync(ct);
            var previews = (await application.PreviewForecastMatchesAsync(userId, batch, ct))
                .ToDictionary(preview => preview.RowId);
            var taken = previews.Values
                .Select(preview => ForecastKey(preview.EntryId, preview.RuleId, preview.DueDate))
                .ToHashSet(StringComparer.Ordinal);
            var flagged = new HashSet<string>(StringComparer.Ordinal);
            // Quién te paga la nómina (u otro ingreso fijo): un ingreso suyo fuera de lo previsto
            // suele ser un adelanto o una paga extra.
            var fixedIncomePayers = entries
                .Where(entry =>
                    entry.Direction == EntryDirection.In &&
                    entry.Status == EntryStatus.Paid &&
                    !string.IsNullOrWhiteSpace(entry.Description) &&
                    concepts.TryGetValue(entry.ConceptId, out var paidConcept) &&
                    paidConcept.Kind == ConceptKind.Income &&
                    paidConcept.Nature == ConceptNature.Fixed)
                .Select(entry => RevolutMovementClassifier.Normalize(entry.Description!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<Forecast> OpenForecasts(Guid conceptId, EntryDirection direction, DateOnly monthStart)
            {
                var monthEnd = monthStart.AddMonths(1);
                var open = entries
                    .Where(entry =>
                        entry.ConceptId == conceptId &&
                        entry.Direction == direction &&
                        entry.DueDate >= monthStart &&
                        entry.DueDate < monthEnd &&
                        entry.ImportRowId == null &&
                        entry.Status != EntryStatus.Paid &&
                        entry.Status != EntryStatus.Skipped)
                    .Select(entry => new Forecast(entry.DueDate, entry.ForecastAmount, entry.AccountId, entry.Id, null))
                    .ToList();
                foreach (var rule in rules.Where(rule =>
                             rule.ConceptId == conceptId &&
                             rule.Direction == direction &&
                             RecurrenceCalculator.OccursIn(rule, monthStart.Year, monthStart.Month)))
                {
                    var generated = entries.Any(entry =>
                        entry.RecurringRuleId == rule.Id &&
                        entry.DueDate >= monthStart &&
                        entry.DueDate < monthEnd);
                    if (!generated)
                    {
                        open.Add(new Forecast(
                            RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, monthStart.Year, monthStart.Month),
                            rule.ForecastAmount,
                            rule.AccountId,
                            null,
                            rule.Id));
                    }
                }

                return open
                    .Where(forecast => !taken.Contains(forecast.Key) && !flagged.Contains(forecast.Key))
                    .OrderBy(forecast => forecast.DueDate)
                    .ToList();
            }

            foreach (var row in rows.Where(row => ImportBatchApplicationService.EntersAsPaid(row) && row.Allocations.Count == 0))
            {
                if (ConceptOf(row) is not { } conceptId || !concepts.TryGetValue(conceptId, out var concept))
                    continue;

                var direction = row.Amount >= 0 ? EntryDirection.In : EntryDirection.Out;
                var amount = Math.Abs(row.Amount);
                var monthStart = new DateOnly(row.ValueDate.Year, row.ValueDate.Month, 1);
                var label = Label(row);

                if (previews.TryGetValue(row.Id, out var match))
                {
                    if (match.DueDate >= monthStart.AddMonths(1))
                    {
                        findings.Add(Finding(
                            SeverityInfo,
                            "Cuenta para el mes siguiente",
                            $"{label}: se apuntará como «{concept.Name}» de {MonthName(match.DueDate)}, porque así lo " +
                            "tienes previsto (por ejemplo, la nómina que llega antes de fin de mes).",
                            row));
                    }
                    else if (IsNoticeablyDifferent(amount, match.ForecastAmount))
                    {
                        findings.Add(Finding(
                            SeverityInfo,
                            "Importe distinto de lo previsto",
                            $"{label}: para «{concept.Name}» tenías previsto {Money(match.ForecastAmount)}. " +
                            $"Se guardará lo que dice el banco ({Money(amount)}).",
                            row));
                    }
                    continue;
                }

                var thisMonth = OpenForecasts(conceptId, direction, monthStart);
                var sameAccount = thisMonth.FirstOrDefault(forecast => forecast.AccountId == account.Id);
                if (sameAccount is not null)
                {
                    flagged.Add(sameAccount.Key);
                    findings.Add(Finding(
                        SeverityWarning,
                        "No casa con lo previsto",
                        $"{label}: tenías «{concept.Name}» previsto el {Day(sameAccount.DueDate)} por {Money(sameAccount.Amount)}, " +
                        "pero la fecha o el importe están demasiado lejos para emparejarlos solos. La app lo apuntará aparte " +
                        "y el previsto seguirá pendiente, así que contaría dos veces. Si es el mismo pago, después de aplicar " +
                        "pulsa «Descartar este mes» en el previsto.",
                        row));
                    continue;
                }

                var otherAccount = thisMonth.FirstOrDefault();
                if (otherAccount is not null)
                {
                    flagged.Add(otherAccount.Key);
                    var where = otherAccount.AccountId is { } otherId && accountNames.TryGetValue(otherId, out var otherName)
                        ? $"en «{otherName}»"
                        : "sin cuenta";
                    findings.Add(Finding(
                        SeverityWarning,
                        "Previsto en otra cuenta",
                        $"{label}: «{concept.Name}» está previsto {where}, pero el banco lo cobró en «{account.Name}». " +
                        "La app lo apuntará aparte y el previsto seguirá pendiente. Si es el mismo pago: después de aplicar " +
                        $"pulsa «Descartar este mes» en el previsto y, en Planificación, cambia la cuenta de «{concept.Name}» " +
                        $"a «{account.Name}» para que la próxima vez se emparejen solos.",
                        row));
                    continue;
                }

                if (direction == EntryDirection.Out && IsMonthEnd(row.ValueDate))
                {
                    var advance = OpenForecasts(conceptId, direction, monthStart.AddMonths(1))
                        .FirstOrDefault(forecast => IsSimilar(amount, forecast.Amount));
                    if (advance is not null)
                    {
                        flagged.Add(advance.Key);
                        findings.Add(Finding(
                            SeverityWarning,
                            "¿Pago adelantado?",
                            $"{label}: también tienes «{concept.Name}» previsto el {Day(advance.DueDate)} por {Money(advance.Amount)}. " +
                            $"La app lo contará como gasto de {MonthName(row.ValueDate)}. Si en realidad era el pago de " +
                            $"{MonthName(advance.DueDate)}, ve a {MonthName(advance.DueDate)} y pulsa «Descartar este mes» en ese " +
                            "previsto para no contarlo dos veces.",
                            row));
                        continue;
                    }
                }

                if (direction == EntryDirection.In)
                {
                    var payer = row.NormalizedDescription ?? RevolutMovementClassifier.Normalize(row.RawDescription);
                    if (concept.Kind == ConceptKind.Income &&
                        amount >= UnplannedIncomeThreshold &&
                        (concept.Nature == ConceptNature.Fixed || fixedIncomePayers.Contains(payer)))
                    {
                        findings.Add(Finding(
                            SeverityInfo,
                            "Ingreso que no estaba previsto",
                            $"{label}: no había nada previsto así en «{concept.Name}». Si es un adelanto (por ejemplo, de una " +
                            "paga extra), después de aplicar escríbelo en la nota del movimiento: así recordarás que esa paga " +
                            "llegará más baja.",
                            row));
                    }
                    continue;
                }

                if (concept.Nature != ConceptNature.Fixed) continue;

                // Otro cobro del banco por lo mismo y el mismo importe en el mes: posible cargo doble.
                // Los apuntes hechos a mano los revisa FindPossibleDuplicatesAsync.
                var monthEnd = monthStart.AddMonths(1);
                var alreadyPaid = entries.FirstOrDefault(entry =>
                    entry.ConceptId == conceptId &&
                    entry.Direction == direction &&
                    entry.Status == EntryStatus.Paid &&
                    entry.ImportRowId != null &&
                    entry.DueDate >= monthStart &&
                    entry.DueDate < monthEnd &&
                    Math.Abs(entry.EffectiveAmount - amount) < Cent);
                var twinInBatch = rows.FirstOrDefault(other =>
                    other.Id != row.Id &&
                    previews.TryGetValue(other.Id, out var otherMatch) &&
                    ConceptOf(other) == conceptId &&
                    otherMatch.DueDate >= monthStart &&
                    otherMatch.DueDate < monthEnd &&
                    Math.Abs(Math.Abs(other.Amount) - amount) < Cent);
                if (alreadyPaid is not null || twinInBatch is not null)
                {
                    var when = alreadyPaid?.ValueDate ?? alreadyPaid?.DueDate ?? twinInBatch!.ValueDate;
                    findings.Add(Finding(
                        SeverityWarning,
                        "¿Cobrado dos veces?",
                        $"{label}: «{concept.Name}» ya figura como pagado este mes por el mismo importe (el {Day(when)}). " +
                        "Si el banco te lo cobró dos veces, reclámalo; si es otra cosa, cámbiale el concepto.",
                        row));
                }
            }

            findings.AddRange(await FindPossibleDuplicatesAsync(db, account, rows, entries, concepts, ct));
            return findings;
        }

        /// <summary>
        /// Un apunte ya pagado en esta cuenta que no viene de un extracto suyo (hecho a mano,
        /// un previsto confirmado a mano o el reflejo de un traspaso) y que coincide en importe
        /// y fecha con una fila: si es el mismo movimiento, contaría dos veces.
        /// </summary>
        private static async Task<List<ImportFindingDto>> FindPossibleDuplicatesAsync(
            IAppDbContext db,
            Account account,
            IReadOnlyList<ImportRow> rows,
            IReadOnlyList<LedgerEntry> entries,
            IReadOnlyDictionary<Guid, Concept> concepts,
            CancellationToken ct)
        {
            var findings = new List<ImportFindingDto>();
            var paid = entries
                .Where(entry =>
                    entry.AccountId == account.Id &&
                    entry.Status == EntryStatus.Paid &&
                    entry.ValueDate != null)
                .ToList();
            var importRowIds = paid
                .Where(entry => entry.ImportRowId != null)
                .Select(entry => entry.ImportRowId!.Value)
                .Distinct()
                .ToList();
            var importAccounts = importRowIds.Count == 0
                ? new Dictionary<Guid, Guid?>()
                : await db.ImportRows
                    .AsNoTracking()
                    .Where(row => importRowIds.Contains(row.Id))
                    .Select(row => new { row.Id, row.Batch!.AccountId })
                    .ToDictionaryAsync(item => item.Id, item => item.AccountId, ct);
            var foreign = paid
                .Where(entry =>
                    entry.ImportRowId is null ||
                    (importAccounts.TryGetValue(entry.ImportRowId.Value, out var sourceAccount) &&
                     sourceAccount != account.Id))
                .ToList();

            var used = new HashSet<Guid>();
            foreach (var row in rows)
            {
                var direction = row.Amount >= 0 ? EntryDirection.In : EntryDirection.Out;
                var amount = Math.Abs(row.Amount);
                var twin = foreign
                    .Where(entry =>
                        !used.Contains(entry.Id) &&
                        entry.Direction == direction &&
                        Math.Abs(entry.EffectiveAmount - amount) < Cent &&
                        Math.Abs(entry.ValueDate!.Value.DayNumber - row.ValueDate.DayNumber) <= DuplicateWindowDays)
                    .OrderBy(entry => Math.Abs(entry.ValueDate!.Value.DayNumber - row.ValueDate.DayNumber))
                    .FirstOrDefault();
                if (twin is null) continue;

                used.Add(twin.Id);
                var conceptName = concepts.TryGetValue(twin.ConceptId, out var concept) ? concept.Name : "sin concepto";
                var (origin, advice) = twin.ImportRowId is not null
                    ? ("copiado desde otra cuenta al importar",
                        "Si es el mismo traspaso, después de aplicar borra uno de los dos.")
                    : twin.RecurringRuleId is not null
                        ? ("un previsto que marcaste como pagado",
                            "Si es el mismo pago, antes de aplicar pulsa «Deshacer confirmación» en ese apunte y deja este " +
                            $"movimiento con el concepto «{conceptName}»: así la importación los junta y no cuenta dos veces.")
                        : ("apuntado a mano",
                            "Si es el mismo movimiento, después de aplicar borra el que hiciste a mano.");
                findings.Add(Finding(
                    SeverityWarning,
                    "¿Repetido?",
                    $"{Label(row)}: en «{account.Name}» ya hay un apunte igual del {Day(twin.ValueDate!.Value)} " +
                    $"({Money(twin.SignedEffectiveAmount)}, «{conceptName}», {origin}). {advice}",
                    row));
            }

            return findings;
        }

        private static Guid? ConceptOf(ImportRow row) => row.ConfirmedConceptId ?? row.SuggestedConceptId;

        private static DateOnly SettledOn(ImportRow row) => row.SettledDate ?? row.ValueDate;

        private static string ForecastKey(Guid? entryId, Guid? ruleId, DateOnly dueDate) =>
            entryId is { } id
                ? $"entry:{id}"
                : $"rule:{ruleId}:{dueDate.Year:D4}-{dueDate.Month:D2}";

        private static bool IsMonthEnd(DateOnly date) =>
            date.Day > DateTime.DaysInMonth(date.Year, date.Month) - MonthEndDays;

        private static bool IsSimilar(decimal actual, decimal forecast) =>
            forecast > 0m && Math.Abs(actual - forecast) <= forecast * AdvanceAmountTolerance;

        private static bool IsNoticeablyDifferent(decimal actual, decimal forecast) =>
            forecast > 0m && Math.Abs(actual - forecast) > Math.Max(1m, forecast * 0.10m);

        private static DateOnly? StatementStart(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var match = StatementRange.Match(fileName);
            return match.Success &&
                   DateOnly.TryParseExact(
                       match.Groups[1].Value,
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out var start)
                ? start
                : null;
        }

        private static string Label(ImportRow row)
        {
            var description = row.RawDescription.Trim();
            if (description.Length > 40) description = description[..40].TrimEnd() + "…";
            return $"{Day(row.ValueDate)} · {description} · {Money(row.Amount)}";
        }

        private static string Day(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);

        private static string MonthName(DateOnly date) => Spanish.DateTimeFormat.GetMonthName(date.Month);

        private static string Money(decimal amount) => amount.ToString("N2", Spanish) + " €";

        private static string Plural(int count, string one, string many) =>
            $"{count} {(count == 1 ? one : many)}";

        private static ImportFindingDto Finding(
            string severity, string title, string detail, IEnumerable<ImportRow>? rows = null) => new()
        {
            Severity = severity,
            Title = title,
            Detail = detail,
            RowNumbers = rows?.Select(row => row.RowNumber).ToList() ?? new List<int>()
        };

        private static ImportFindingDto Finding(string severity, string title, string detail, ImportRow row) =>
            Finding(severity, title, detail, new[] { row });
    }
}
