using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/imports")]
    public sealed class ImportsController : ControllerBase
    {
        private readonly IAppDbContext _db;
        private readonly IImportAiSuggestionService _aiSuggestions;
        private readonly IImportChatService _chat;
        private readonly PeriodProvisioner _periods;

        public ImportsController(
            IAppDbContext db,
            IImportAiSuggestionService aiSuggestions,
            IImportChatService chat,
            PeriodProvisioner periods)
        {
            _db = db;
            _aiSuggestions = aiSuggestions;
            _chat = chat;
            _periods = periods;
        }

        [HttpGet("{batchId:guid}")]
        public async Task<ActionResult<ImportReviewDto>> GetReview(
            Guid batchId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var batch = await _db.ImportBatches
                .AsNoTracking()
                .Include(b => b.Rows)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();

            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value && c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new ImportConceptOptionDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Kind = c.Kind.ToString()
                })
                .ToListAsync(ct);

            return Ok(new ImportReviewDto
            {
                Id = batch.Id,
                AccountId = batch.AccountId ?? Guid.Empty,
                FileName = batch.FileName,
                Status = batch.Status.ToString(),
                Rows = batch.Rows
                    .OrderBy(r => r.RowNumber)
                    .Select(MapRow)
                    .ToList(),
                Concepts = concepts
            });
        }

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/concept")]
        public async Task<ActionResult<ImportRowDto>> SelectConcept(
            Guid batchId, Guid rowId, [FromBody] SelectImportConceptDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var row = await _db.ImportRows
                .Include(r => r.Batch)
                .FirstOrDefaultAsync(
                    r => r.Id == rowId && r.BatchId == batchId && r.UserId == userId.Value, ct);
            if (row is null) return NotFound();
            if (row.Batch?.Status == ImportBatchStatus.Applied)
                return Conflict("El lote ya se ha aplicado.");

            if (dto.ConceptId is not null)
            {
                var valid = await _db.Concepts.AnyAsync(
                    c => c.Id == dto.ConceptId.Value && c.UserId == userId.Value && c.IsActive, ct);
                if (!valid) return BadRequest("El concepto no existe o no está activo.");
            }

            row.ConfirmedConceptId = dto.ConceptId;
            row.SuggestionSource = dto.ConceptId is null ? null : "manual";
            row.SuggestionConfidence = dto.ConceptId is null ? null : 1m;
            await _db.SaveChangesAsync(ct);

            return Ok(MapRow(row));
        }

        [HttpPost("{batchId:guid}/chat")]
        public async Task<ActionResult<ImportChatResponseDto>> Chat(
            Guid batchId, [FromBody] ImportChatRequestDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(dto.Message)) return BadRequest("El mensaje no puede estar vacío.");

            var batch = await _db.ImportBatches
                .Include(b => b.Rows)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();

            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value && c.IsActive)
                .Select(c => new ImportAiCandidate(c.Id, c.Name, c.Kind.ToString()))
                .ToListAsync(ct);
            var conceptNames = concepts.ToDictionary(c => c.Id, c => c.Name);

            // Sólo las filas aún pendientes de decisión: aplicadas o duplicadas
            // no tiene sentido tocarlas desde el chat.
            var rowContexts = batch.Rows
                .Where(r => r.Status == ImportRowStatus.Pending)
                .OrderBy(r => r.RowNumber)
                .Select(r => new ImportChatRowContext(
                    r.Id,
                    r.RowNumber,
                    r.ValueDate.ToString("yyyy-MM-dd"),
                    r.Amount,
                    r.RawDescription,
                    (r.ConfirmedConceptId ?? r.SuggestedConceptId) is { } cid && conceptNames.TryGetValue(cid, out var name)
                        ? name
                        : null))
                .ToList();

            var history = dto.History
                .Where(h => h.Role is "user" or "assistant")
                .Select(h => new ImportChatMessage(h.Role, h.Content))
                .ToList();

            var result = await _chat.AskAsync(dto.Message, history, rowContexts, concepts, ct);

            var applied = 0;
            foreach (var change in result.Changes)
            {
                var row = batch.Rows.FirstOrDefault(r => r.Id == change.RowId);
                if (row is null || row.Status != ImportRowStatus.Pending) continue;

                row.ConfirmedConceptId = change.ConceptId;
                row.SuggestionSource = "chat";
                row.SuggestionConfidence = change.ConceptId is null ? null : 1m;
                applied++;
            }

            if (applied > 0) await _db.SaveChangesAsync(ct);

            return Ok(new ImportChatResponseDto
            {
                Reply = result.Reply,
                AppliedChanges = applied,
                Unrecognized = result.Unrecognized
            });
        }

        [HttpPost("{batchId:guid}/apply")]
        public async Task<ActionResult<object>> Apply(
            Guid batchId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var batch = await _db.ImportBatches
                .Include(b => b.Rows)
                .FirstOrDefaultAsync(
                    b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();
            if (batch.Status == ImportBatchStatus.Applied)
                return Conflict("El lote ya se ha aplicado.");

            var unassigned = batch.Rows.Count(r =>
                r.Status == ImportRowStatus.Pending &&
                r.ConfirmedConceptId is null &&
                r.SuggestedConceptId is null);
            if (unassigned > 0)
                return BadRequest(new
                {
                    message = "Hay filas sin concepto asignado.",
                    unassignedRows = unassigned
                });

            var accountId = batch.AccountId
                ?? throw new InvalidOperationException("El lote no tiene cuenta.");
            var concepts = await _db.Concepts
                .Where(c => c.UserId == userId.Value)
                .ToDictionaryAsync(c => c.Id, ct);
            var feeConcept = concepts.Values.FirstOrDefault(
                c => string.Equals(c.Name, "Bank fees", StringComparison.OrdinalIgnoreCase));
            var existingFingerprints = (await _db.LedgerEntries
                .Where(e => e.UserId == userId.Value && e.Fingerprint != null)
                .Select(e => e.Fingerprint!)
                .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var applied = 0;
            foreach (var row in batch.Rows.Where(r => r.Status == ImportRowStatus.Pending))
            {
                var conceptId = row.ConfirmedConceptId ?? row.SuggestedConceptId;
                if (conceptId is null || !concepts.TryGetValue(conceptId.Value, out var concept))
                    continue;
                if (!existingFingerprints.Add(row.Fingerprint))
                {
                    row.Status = ImportRowStatus.Duplicate;
                    continue;
                }
                if (row.Fee != 0m && feeConcept is null)
                    return BadRequest(
                        "Falta el concepto 'Bank fees': siembra el plan de cuentas antes de aplicar.");

                var period = await _periods.GetOrOpenAsync(
                    userId.Value, row.ValueDate.Year, row.ValueDate.Month, ct);
                if (period.Status == PeriodStatus.Closed)
                    return Conflict($"El periodo {period.Year}-{period.Month:D2} está cerrado.");

                // El estado real (Paid/Pending) lo decidió el clasificador al
                // importar: un movimiento aún no liquidado no puede entrar como
                // pagado sólo porque ya se ha revisado su concepto.
                var isPaid = row.ClassifiedStatus == EntryStatus.Paid;
                var amount = Math.Abs(row.Amount);

                _db.LedgerEntries.Add(new LedgerEntry
                {
                    UserId = userId.Value,
                    PeriodId = period.Id,
                    ConceptId = concept.Id,
                    AccountId = accountId,
                    Direction = row.Amount >= 0 ? EntryDirection.In : EntryDirection.Out,
                    IsTransfer = concept.Kind == ConceptKind.Transfer,
                    Status = row.ClassifiedStatus,
                    DueDate = row.ValueDate,
                    ValueDate = isPaid ? row.ValueDate : null,
                    ForecastAmount = amount,
                    ActualAmount = isPaid ? amount : null,
                    Description = row.RawDescription,
                    ImportRowId = row.Id,
                    Fingerprint = row.Fingerprint
                });

                // La comisión es un gasto aparte: el banco resta el importe y la
                // comisión por separado del saldo, y así lo refleja el libro.
                if (row.Fee != 0m && feeConcept is not null)
                {
                    var feeFingerprint = row.Fingerprint + "#fee";
                    if (existingFingerprints.Add(feeFingerprint))
                    {
                        _db.LedgerEntries.Add(new LedgerEntry
                        {
                            UserId = userId.Value,
                            PeriodId = period.Id,
                            ConceptId = feeConcept.Id,
                            AccountId = accountId,
                            Direction = EntryDirection.Out,
                            IsTransfer = false,
                            Status = row.ClassifiedStatus,
                            DueDate = row.ValueDate,
                            ValueDate = isPaid ? row.ValueDate : null,
                            ForecastAmount = row.Fee,
                            ActualAmount = isPaid ? row.Fee : null,
                            Description = $"Comisión: {row.RawDescription}",
                            ImportRowId = row.Id,
                            Fingerprint = feeFingerprint
                        });
                    }
                }

                row.Status = ImportRowStatus.Accepted;
                applied++;
            }

            batch.AcceptedRows = batch.Rows.Count(r => r.Status == ImportRowStatus.Accepted);
            batch.DuplicateRows = batch.Rows.Count(r => r.Status == ImportRowStatus.Duplicate);
            batch.Status = ImportBatchStatus.Applied;
            batch.AppliedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(new { applied, batchId = batch.Id });
        }

        [HttpPost("mappings/seed")]
        public async Task<ActionResult<object>> SeedMappings(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var accounts = await _db.Accounts
                .Where(a => a.UserId == userId.Value && a.IsActive)
                .ToDictionaryAsync(a => a.Name, StringComparer.OrdinalIgnoreCase, ct);
            var concepts = await _db.Concepts
                .Where(c => c.UserId == userId.Value)
                .ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
            var existing = await _db.ConceptMappings
                .Where(m => m.UserId == userId.Value)
                .ToListAsync(ct);

            var created = 0;
            foreach (var template in ConceptMappingTemplates.Rules)
            {
                Guid? accountId = null;
                if (template.AccountName is not null)
                {
                    if (!accounts.TryGetValue(template.AccountName, out var account))
                        continue;
                    accountId = account.Id;
                }

                if (!concepts.TryGetValue(template.ConceptName, out var concept))
                    continue;

                var alreadyExists = existing.Any(m =>
                    m.AccountId == accountId &&
                    string.Equals(m.Pattern, template.Pattern, StringComparison.OrdinalIgnoreCase));
                if (alreadyExists) continue;

                var mapping = new ConceptMapping
                {
                    UserId = userId.Value,
                    AccountId = accountId,
                    Pattern = template.Pattern,
                    ConceptId = concept.Id,
                    Priority = template.Priority
                };
                _db.ConceptMappings.Add(mapping);
                existing.Add(mapping);
                created++;
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { created });
        }

        [HttpPost]
        [RequestSizeLimit(10_000_000)]
        public async Task<ActionResult<ImportBatchDto>> Create(
            [FromForm] Guid accountId, IFormFile file, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (file is null || file.Length == 0)
                return BadRequest("El fichero es obligatorio.");

            var account = await _db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.Id == accountId && a.UserId == userId.Value && a.IsActive, ct);
            if (account is null) return BadRequest("La cuenta no existe o no está activa.");

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var parsed = RevolutCsvParser.Parse(reader);
            if (parsed.Movements.Count == 0)
                return BadRequest(new { message = "No se ha podido importar ningún movimiento.", parsed.Problems });

            var mappings = await _db.ConceptMappings
                .AsNoTracking()
                .Include(m => m.Concept)
                .Where(m => m.UserId == userId.Value && m.IsActive)
                .ToListAsync(ct);
            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value && c.IsActive)
                .Select(c => new ImportAiCandidate(c.Id, c.Name, c.Kind.ToString()))
                .ToListAsync(ct);
            var unmappedDescriptions = parsed.Movements
                .Select(m => RevolutMovementClassifier.Normalize(m.Description))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var aiSuggestions = await _aiSuggestions.SuggestAsync(unmappedDescriptions, concepts, ct);

            var existingFingerprints = await _db.ImportRows
                .AsNoTracking()
                .Where(r => r.UserId == userId.Value)
                .Select(r => r.Fingerprint)
                .ToListAsync(ct);
            var fingerprints = existingFingerprints.ToHashSet(StringComparer.Ordinal);

            var batch = new ImportBatch
            {
                UserId = userId.Value,
                AccountId = account.Id,
                Source = ImportSource.RevolutCsv,
                FileName = file.FileName,
                TotalRows = parsed.Movements.Count
            };

            var excludedRows = 0;
            var duplicateRows = 0;
            foreach (var movement in parsed.Movements)
            {
                var classified = RevolutMovementClassifier.Classify(movement);
                if (classified.Disposition == MovementDisposition.Excluded)
                {
                    excludedRows++;
                    continue;
                }

                var fingerprint = CreateFingerprint(account.Id, movement);
                if (!fingerprints.Add(fingerprint))
                {
                    duplicateRows++;
                    continue;
                }

                var mapping = FindMapping(mappings, account.Id, classified.NormalizedDescription);
                aiSuggestions.TryGetValue(classified.NormalizedDescription, out var aiSuggestion);
                batch.Rows.Add(new ImportRow
                {
                    UserId = userId.Value,
                    RowNumber = movement.RowNumber,
                    ValueDate = DateOnly.FromDateTime(movement.StartedAt),
                    Amount = movement.Amount,
                    Fee = movement.Fee,
                    ClassifiedStatus = classified.Status,
                    Currency = movement.Currency,
                    RawDescription = movement.Description,
                    NormalizedDescription = classified.NormalizedDescription,
                    Fingerprint = fingerprint,
                    Status = ImportRowStatus.Pending,
                    SuggestedConceptId = mapping?.ConceptId ?? aiSuggestion?.ConceptId,
                    SuggestionSource = mapping is not null ? "mapping" : aiSuggestion is not null ? "ai" : null,
                    SuggestionConfidence = mapping is not null ? 1m : aiSuggestion?.Confidence
                });
            }

            batch.AcceptedRows = batch.Rows.Count;
            batch.DuplicateRows = duplicateRows;
            _db.ImportBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            return Ok(new ImportBatchDto
            {
                Id = batch.Id,
                AccountId = account.Id,
                FileName = batch.FileName,
                TotalRows = batch.TotalRows,
                AcceptedRows = batch.AcceptedRows,
                DuplicateRows = batch.DuplicateRows,
                ExcludedRows = excludedRows,
                Problems = parsed.Problems
            });
        }

        private static ConceptMapping? FindMapping(
            IReadOnlyCollection<ConceptMapping> mappings,
            Guid accountId,
            string normalizedDescription)
        {
            return mappings
                .Where(m => (m.AccountId == null || m.AccountId == accountId) &&
                            normalizedDescription.Contains(m.Pattern, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.AccountId == accountId)
                .ThenByDescending(m => m.Priority)
                .ThenByDescending(m => m.Pattern.Length)
                .FirstOrDefault();
        }

        private static ImportRowDto MapRow(ImportRow row) => new()
        {
            Id = row.Id,
            RowNumber = row.RowNumber,
            ValueDate = row.ValueDate,
            Amount = row.Amount,
            Currency = row.Currency,
            RawDescription = row.RawDescription,
            NormalizedDescription = row.NormalizedDescription,
            Status = row.Status.ToString(),
            SuggestedConceptId = row.SuggestedConceptId,
            ConfirmedConceptId = row.ConfirmedConceptId,
            SuggestionSource = row.SuggestionSource
        };

        /// <summary>
        /// Incluye el número de línea del extracto: dos movimientos distintos
        /// pueden compartir fecha, importe y descripción normalizada (por
        /// ejemplo, dos cajeros del mismo importe el mismo minuto), y sin el
        /// número de fila la huella los trataría como el mismo duplicado.
        /// </summary>
        private static string CreateFingerprint(Guid accountId, RevolutMovement movement)
        {
            var raw = string.Join(
                "|",
                accountId.ToString("N"),
                movement.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                movement.Amount.ToString(CultureInfo.InvariantCulture),
                RevolutMovementClassifier.Normalize(movement.Description),
                movement.RowNumber.ToString(CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }
    }
}
