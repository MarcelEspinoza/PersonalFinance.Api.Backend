using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts;
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
        private readonly IImportBatchApplicationService _application;
        private readonly IMediator _mediator;

        public ImportsController(
            IAppDbContext db,
            IImportAiSuggestionService aiSuggestions,
            IImportChatService chat,
            IImportBatchApplicationService application,
            IMediator mediator)
        {
            _db = db;
            _aiSuggestions = aiSuggestions;
            _chat = chat;
            _application = application;
            _mediator = mediator;
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

        [HttpDelete("{batchId:guid}")]
        public async Task<IActionResult> Discard(Guid batchId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var batch = await _db.ImportBatches
                .FirstOrDefaultAsync(
                    item => item.Id == batchId && item.UserId == userId.Value,
                    ct);
            if (batch is null) return NotFound();
            if (batch.Status == ImportBatchStatus.Applied)
                return Conflict("Una importación aplicada no se puede descartar.");

            _db.ImportBatches.Remove(batch);
            await _db.SaveChangesAsync(ct);
            return NoContent();
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
            if (dto.SaveForFuture && dto.ConceptId is Guid conceptId && row.Batch?.AccountId is Guid accountId)
            {
                var pattern = row.NormalizedDescription?.Trim();
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    var mapping = await _db.ConceptMappings.FirstOrDefaultAsync(
                        item => item.UserId == userId.Value &&
                                item.AccountId == accountId &&
                                item.Pattern == pattern,
                        ct);
                    if (mapping is null)
                    {
                        mapping = new ConceptMapping
                        {
                            UserId = userId.Value,
                            AccountId = accountId,
                            Pattern = pattern,
                            ConceptId = conceptId,
                            Priority = 100
                        };
                        _db.ConceptMappings.Add(mapping);
                    }
                    else
                    {
                        mapping.ConceptId = conceptId;
                        mapping.Priority = 100;
                        mapping.IsActive = true;
                    }
                }
            }
            await _db.SaveChangesAsync(ct);

            return Ok(MapRow(row));
        }

        [HttpPost("{batchId:guid}/rows/{rowId:guid}/concept")]
        public async Task<ActionResult<ImportRowDto>> CreateConceptForRow(
            Guid batchId,
            Guid rowId,
            [FromBody] CreateImportConceptDto dto,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var row = await _db.ImportRows
                .Include(item => item.Batch)
                .FirstOrDefaultAsync(
                    item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value,
                    ct);
            if (row is null) return NotFound();
            if (row.Batch?.Status == ImportBatchStatus.Applied)
                return Conflict("El lote ya se ha aplicado.");

            var name = dto.Name.Trim();
            if (name.Length is 0 or > 120)
                return BadRequest("El nombre debe tener entre 1 y 120 caracteres.");

            var group = await _db.ConceptGroups.FirstOrDefaultAsync(
                item => item.Id == dto.GroupId && item.UserId == userId.Value && item.IsActive,
                ct);
            if (group is null) return BadRequest("El grupo no existe o no está activo.");

            var concept = await _db.Concepts.FirstOrDefaultAsync(
                item => item.UserId == userId.Value &&
                        item.GroupId == group.Id &&
                        item.Name == name &&
                        item.IsActive,
                ct);
            if (concept is null)
            {
                var duplicate = await _db.Concepts.AnyAsync(
                    item => item.UserId == userId.Value && item.GroupId == group.Id && item.Name == name,
                    ct);
                if (duplicate) return Conflict("Ya existe una categoría inactiva con ese nombre en el grupo.");

                var sortOrder = (await _db.Concepts
                    .Where(item => item.UserId == userId.Value && item.GroupId == group.Id)
                    .Select(item => (int?)item.SortOrder)
                    .MaxAsync(ct) ?? 0) + 10;
                concept = new Concept
                {
                    UserId = userId.Value,
                    GroupId = group.Id,
                    Name = name,
                    Kind = group.Kind,
                    Nature = dto.Nature,
                    SortOrder = sortOrder
                };
                _db.Concepts.Add(concept);
            }

            row.ConfirmedConceptId = concept.Id;
            row.SuggestionSource = "manual";
            row.SuggestionConfidence = 1m;

            if (dto.SaveForFuture && row.Batch?.AccountId is Guid accountId)
            {
                var pattern = row.NormalizedDescription?.Trim();
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    var mapping = await _db.ConceptMappings.FirstOrDefaultAsync(
                        item => item.UserId == userId.Value &&
                                item.AccountId == accountId &&
                                item.Pattern == pattern,
                        ct);
                    if (mapping is null)
                    {
                        _db.ConceptMappings.Add(new ConceptMapping
                        {
                            UserId = userId.Value,
                            AccountId = accountId,
                            Pattern = pattern,
                            ConceptId = concept.Id,
                            Priority = 100
                        });
                    }
                    else
                    {
                        mapping.ConceptId = concept.Id;
                        mapping.Priority = 100;
                        mapping.IsActive = true;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
            return Ok(MapRow(row));
        }

        [HttpPut("{batchId:guid}/groups/concept")]
        public async Task<ActionResult<object>> SelectGroupConcept(
            Guid batchId,
            [FromBody] SelectImportGroupConceptDto dto,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var normalizedDescription = dto.NormalizedDescription.Trim();
            if (normalizedDescription.Length == 0)
                return BadRequest("La descripción normalizada es obligatoria.");

            var batch = await _db.ImportBatches
                .FirstOrDefaultAsync(
                    item => item.Id == batchId && item.UserId == userId.Value,
                    ct);
            if (batch is null) return NotFound();
            if (batch.Status == ImportBatchStatus.Applied)
                return Conflict("El lote ya se ha aplicado.");

            if (dto.ConceptId is not null)
            {
                var valid = await _db.Concepts.AnyAsync(
                    concept =>
                        concept.Id == dto.ConceptId.Value &&
                        concept.UserId == userId.Value &&
                        concept.IsActive,
                    ct);
                if (!valid) return BadRequest("El concepto no existe o no está activo.");
            }

            var rows = await _db.ImportRows
                .Where(row =>
                    row.BatchId == batchId &&
                    row.UserId == userId.Value &&
                    row.Status == ImportRowStatus.Pending &&
                    row.NormalizedDescription == normalizedDescription)
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                row.ConfirmedConceptId = dto.ConceptId;
                row.SuggestionSource = dto.ConceptId is null ? null : "group";
                row.SuggestionConfidence = dto.ConceptId is null ? null : 1m;
            }

            if (dto.ConceptId is not null && batch.AccountId is not null)
            {
                var mapping = await _db.ConceptMappings.FirstOrDefaultAsync(
                    item =>
                        item.UserId == userId.Value &&
                        item.AccountId == batch.AccountId &&
                        item.Pattern == normalizedDescription,
                    ct);
                if (mapping is null)
                {
                    _db.ConceptMappings.Add(new ConceptMapping
                    {
                        UserId = userId.Value,
                        AccountId = batch.AccountId,
                        Pattern = normalizedDescription,
                        ConceptId = dto.ConceptId.Value,
                        Priority = 120,
                        IsActive = true
                    });
                }
                else
                {
                    mapping.ConceptId = dto.ConceptId.Value;
                    mapping.Priority = Math.Max(mapping.Priority, 120);
                    mapping.IsActive = true;
                }
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { updated = rows.Count });
        }

        [HttpPost("{batchId:guid}/suggest")]
        public async Task<ActionResult<object>> SuggestPending(
            Guid batchId,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var batch = await _db.ImportBatches
                .Include(item => item.Rows)
                .FirstOrDefaultAsync(
                    item => item.Id == batchId && item.UserId == userId.Value,
                    ct);
            if (batch is null) return NotFound();
            if (batch.Status == ImportBatchStatus.Applied)
                return Conflict("El lote ya se ha aplicado.");
            if (batch.AccountId is null)
                return BadRequest("El lote no tiene una cuenta asociada.");

            await _mediator.Send(new SeedChartOfAccountsCommand(userId.Value), ct);
            await SeedMappingsAsync(userId.Value, ct);

            var mappings = await _db.ConceptMappings
                .AsNoTracking()
                .Include(mapping => mapping.Concept)
                .Where(mapping => mapping.UserId == userId.Value && mapping.IsActive)
                .ToListAsync(ct);
            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(concept => concept.UserId == userId.Value && concept.IsActive)
                .Select(concept => new ImportAiCandidate(
                    concept.Id,
                    concept.Name,
                    concept.Kind.ToString()))
                .ToListAsync(ct);
            var conceptIds = concepts.ToDictionary(
                concept => concept.Name,
                concept => concept.Id,
                StringComparer.OrdinalIgnoreCase);

            var pendingRows = batch.Rows
                .Where(row =>
                    row.Status == ImportRowStatus.Pending &&
                    row.ConfirmedConceptId is null)
                .ToList();
            var descriptionsForAi = pendingRows
                .Where(row =>
                    FindMapping(
                        mappings,
                        batch.AccountId.Value,
                        row.NormalizedDescription ?? string.Empty) is null)
                .Select(row => row.NormalizedDescription ?? string.Empty)
                .Where(description => !string.IsNullOrWhiteSpace(description))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var aiSuggestions = await _aiSuggestions.SuggestAsync(
                descriptionsForAi,
                concepts,
                ct);

            var mapped = 0;
            var suggested = 0;
            var fallback = 0;
            foreach (var row in pendingRows)
            {
                var mapping = FindMapping(
                    mappings,
                    batch.AccountId.Value,
                    row.NormalizedDescription ?? string.Empty);
                if (mapping is not null)
                {
                    row.SuggestedConceptId = mapping.ConceptId;
                    row.SuggestionSource = "mapping";
                    row.SuggestionConfidence = 1m;
                    mapped++;
                    continue;
                }

                if (aiSuggestions.TryGetValue(row.NormalizedDescription ?? string.Empty, out var suggestion))
                {
                    row.SuggestedConceptId = suggestion.ConceptId;
                    row.SuggestionSource = "ai";
                    row.SuggestionConfidence = suggestion.Confidence;
                    suggested++;
                    continue;
                }

                var fallbackConceptId = ImportFallbackClassifier.FindConceptId(
                    row.NormalizedDescription ?? string.Empty,
                    row.Amount,
                    conceptIds);
                if (fallbackConceptId is not null)
                {
                    row.SuggestedConceptId = fallbackConceptId;
                    row.SuggestionSource = "regla general";
                    row.SuggestionConfidence = 0.5m;
                    fallback++;
                }
            }

            await _db.SaveChangesAsync(ct);
            var remaining = pendingRows.Count(row => row.SuggestedConceptId is null);
            return Ok(new
            {
                mapped,
                suggested,
                fallback,
                remaining,
                aiWarning = _aiSuggestions.FailureReason
            });
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
            var result = await _application.ApplyAsync(userId.Value, batchId, ct);
            if (result.NotFound) return NotFound();
            if (!result.Success && result.Conflict) return Conflict(result.Error);
            if (!result.Success)
                return BadRequest(new { message = result.Error, unassignedRows = result.UnassignedRows });

            return Ok(new
            {
                applied = result.Applied,
                matchedForecasts = result.MatchedForecasts,
                batchId = result.BatchId
            });
        }

        [HttpPost("mappings/seed")]
        public async Task<ActionResult<object>> SeedMappings(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var result = await SeedMappingsAsync(userId.Value, ct);
            return Ok(new { created = result.Created, updated = result.Updated });
        }

        private async Task<(int Created, int Updated)> SeedMappingsAsync(
            Guid userId,
            CancellationToken ct)
        {
            var accounts = await _db.Accounts
                .Where(a => a.UserId == userId && a.IsActive)
                .ToDictionaryAsync(a => a.Name, StringComparer.OrdinalIgnoreCase, ct);
            var concepts = await _db.Concepts
                .Where(c => c.UserId == userId && c.IsActive)
                .ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
            var existing = await _db.ConceptMappings
                .Where(m => m.UserId == userId)
                .ToListAsync(ct);

            var created = 0;
            var updated = 0;
            foreach (var template in ConceptMappingTemplates.Rules)
            {
                Guid? accountId = null;
                if (template.AccountName is not null)
                {
                    var found = accounts.TryGetValue(template.AccountName, out var account);
                    if (!found &&
                        string.Equals(template.AccountName, "Personal", StringComparison.OrdinalIgnoreCase))
                    {
                        found = accounts.TryGetValue("Revolut 6931", out account);
                    }

                    if (!found)
                        continue;
                    accountId = account.Id;
                }

                if (!concepts.TryGetValue(template.ConceptName, out var concept))
                    continue;

                var current = existing.FirstOrDefault(m =>
                    m.AccountId == accountId &&
                    string.Equals(m.Pattern, template.Pattern, StringComparison.OrdinalIgnoreCase));
                if (current is not null)
                {
                    if (current.ConceptId != concept.Id ||
                        current.Priority != template.Priority ||
                        !current.IsActive)
                    {
                        updated++;
                    }
                    current.ConceptId = concept.Id;
                    current.Priority = template.Priority;
                    current.IsActive = true;
                    continue;
                }

                var mapping = new ConceptMapping
                {
                    UserId = userId,
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
            return (created, updated);
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

            await _mediator.Send(new SeedChartOfAccountsCommand(userId.Value), ct);
            await SeedMappingsAsync(userId.Value, ct);

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
            var conceptIds = concepts.ToDictionary(
                concept => concept.Name,
                concept => concept.Id,
                StringComparer.OrdinalIgnoreCase);
            var unmappedDescriptions = parsed.Movements
                .Select(m => new
                {
                    Movement = m,
                    Classification = RevolutMovementClassifier.Classify(m),
                    Normalized = RevolutMovementClassifier.Normalize(m.Description)
                })
                .Where(item =>
                    item.Classification.Disposition != MovementDisposition.Excluded &&
                    FindMapping(mappings, account.Id, item.Normalized) is null)
                .Select(item => item.Normalized)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var aiSuggestions = await _aiSuggestions.SuggestAsync(unmappedDescriptions, concepts, ct);

            var existingFingerprints = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId.Value && entry.Fingerprint != null)
                .Select(entry => entry.Fingerprint!)
                .ToListAsync(ct);
            var fingerprints = existingFingerprints.ToHashSet(StringComparer.Ordinal);
            var movementOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var existingImportedRows = await (
                from row in _db.ImportRows.AsNoTracking()
                join importBatch in _db.ImportBatches.AsNoTracking()
                    on row.BatchId equals importBatch.Id
                where row.UserId == userId.Value &&
                      row.Status == ImportRowStatus.Accepted &&
                      importBatch.AccountId == account.Id &&
                      importBatch.Source == ImportSource.RevolutCsv
                select new
                {
                    row.ValueDate,
                    row.Amount,
                    row.Fee,
                    row.Currency,
                    row.NormalizedDescription,
                    row.RawDescription
                })
                .ToListAsync(ct);
            var existingMovementCounts = existingImportedRows
                .GroupBy(row => CreateMovementKey(
                    row.ValueDate,
                    row.Amount,
                    row.Fee,
                    row.Currency,
                    row.NormalizedDescription ?? RevolutMovementClassifier.Normalize(row.RawDescription)))
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var movementImportOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

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

                var normalizedDescription = RevolutMovementClassifier.Normalize(movement.Description);
                var movementIdentity = CreateMovementIdentity(account.Id, movement, normalizedDescription);
                movementOccurrences.TryGetValue(movementIdentity, out var occurrence);
                occurrence++;
                movementOccurrences[movementIdentity] = occurrence;

                var fingerprint = CreateFingerprint(movementIdentity, occurrence);
                var legacyFingerprint = CreateLegacyFingerprint(account.Id, movement);
                var movementKey = CreateMovementKey(
                    DateOnly.FromDateTime(movement.StartedAt),
                    movement.Amount,
                    movement.Fee,
                    movement.Currency,
                    normalizedDescription);
                movementImportOccurrences.TryGetValue(movementKey, out var importedOccurrence);
                importedOccurrence++;
                movementImportOccurrences[movementKey] = importedOccurrence;

                var wasImportedWithPreviousFingerprint =
                    existingMovementCounts.TryGetValue(movementKey, out var importedCount) &&
                    importedOccurrence <= importedCount;
                if (fingerprints.Contains(legacyFingerprint) ||
                    wasImportedWithPreviousFingerprint ||
                    !fingerprints.Add(fingerprint))
                {
                    duplicateRows++;
                    continue;
                }

                var mapping = FindMapping(mappings, account.Id, classified.NormalizedDescription);
                aiSuggestions.TryGetValue(classified.NormalizedDescription, out var aiSuggestion);
                var fallbackConceptId = mapping is null && aiSuggestion is null
                    ? ImportFallbackClassifier.FindConceptId(
                        classified.NormalizedDescription,
                        movement.Amount,
                        conceptIds)
                    : null;
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
                    SuggestedConceptId = mapping?.ConceptId ?? aiSuggestion?.ConceptId ?? fallbackConceptId,
                    SuggestionSource = mapping is not null
                        ? "mapping"
                        : aiSuggestion is not null
                            ? "ai"
                            : fallbackConceptId is not null
                                ? "regla general"
                                : null,
                    SuggestionConfidence = mapping is not null
                        ? 1m
                        : aiSuggestion?.Confidence ?? (fallbackConceptId is not null ? 0.5m : null)
                });
            }

            batch.AcceptedRows = batch.Rows.Count;
            batch.DuplicateRows = duplicateRows;
            if (batch.AcceptedRows == 0)
            {
                return Conflict(new
                {
                    message = duplicateRows > 0
                        ? "Todos los movimientos del archivo ya están aplicados al libro contable."
                        : "El archivo no contiene movimientos importables.",
                    duplicateRows,
                    excludedRows
                });
            }

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

        private static string CreateMovementIdentity(
            Guid accountId,
            RevolutMovement movement,
            string normalizedDescription)
        {
            return string.Join(
                "|",
                accountId.ToString("N"),
                movement.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                movement.Amount.ToString(CultureInfo.InvariantCulture),
                movement.Fee.ToString(CultureInfo.InvariantCulture),
                movement.Currency.Trim().ToUpperInvariant(),
                normalizedDescription);
        }

        private static string CreateLegacyFingerprint(Guid accountId, RevolutMovement movement)
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

        private static string CreateMovementKey(
            DateOnly valueDate,
            decimal amount,
            decimal fee,
            string? currency,
            string normalizedDescription)
        {
            return string.Join(
                "|",
                valueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount.ToString("G29", CultureInfo.InvariantCulture),
                fee.ToString("G29", CultureInfo.InvariantCulture),
                currency?.Trim().ToUpperInvariant() ?? string.Empty,
                normalizedDescription);
        }

        private static string CreateFingerprint(string movementIdentity, int occurrence)
        {
            var raw = $"{movementIdentity}|{occurrence.ToString(CultureInfo.InvariantCulture)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }
    }
}
