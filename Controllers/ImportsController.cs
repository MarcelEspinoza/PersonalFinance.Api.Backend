using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Reminders;
using PersonalFinance.Api.Features.Settlements;
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
        private readonly ILogger<ImportsController> _logger;

        public ImportsController(
            IAppDbContext db,
            IImportAiSuggestionService aiSuggestions,
            IImportChatService chat,
            IImportBatchApplicationService application,
            IMediator mediator,
            ILogger<ImportsController>? logger = null)
        {
            _db = db;
            _aiSuggestions = aiSuggestions;
            _chat = chat;
            _application = application;
            _mediator = mediator;
            _logger = logger ?? NullLogger<ImportsController>.Instance;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PendingImportDto>>> GetPending(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var batches = await _db.ImportBatches.AsNoTracking()
                .Where(batch => batch.UserId == userId.Value &&
                    (batch.Status == ImportBatchStatus.Draft || batch.Status == ImportBatchStatus.Reviewing) &&
                    batch.Rows.Any(row => row.Status == ImportRowStatus.Pending))
                .OrderByDescending(batch => batch.CreatedAt).ThenByDescending(batch => batch.Id)
                .Take(50)
                .Select(batch => new PendingImportDto(
                    batch.Id, batch.FileName, batch.AccountId,
                    batch.Account != null ? batch.Account.Name : null,
                    batch.Rows.Count(row => row.Status == ImportRowStatus.Pending), batch.CreatedAt))
                .ToListAsync(ct);
            return Ok(batches);
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
                    .ThenInclude(row => row.Allocations)
                .Include(b => b.Rows)
                    .ThenInclude(row => row.PlannedAllocations)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();
            if (batch.Status == ImportBatchStatus.Discarded)
                return Conflict("El lote está descartado.");

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

            var rows = batch.Rows.OrderBy(row => row.RowNumber).Select(MapRow).ToList();
            var reminderIds = rows.Where(row => row.ReminderId.HasValue).Select(row => row.ReminderId!.Value).ToList();
            var reminderNames = await _db.Reminders.AsNoTracking()
                .Where(reminder => reminder.UserId == userId.Value && reminderIds.Contains(reminder.Id))
                .ToDictionaryAsync(reminder => reminder.Id, reminder => reminder.Title, ct);
            var settlementIds = rows.Where(row => row.SettlementId.HasValue).Select(row => row.SettlementId!.Value).ToList();
            var settlementNames = await _db.Settlements.AsNoTracking()
                .Where(settlement => settlement.UserId == userId.Value && settlementIds.Contains(settlement.Id))
                .Join(_db.Counterparties.Where(person => person.UserId == userId.Value),
                    settlement => settlement.CounterpartyId, person => person.Id,
                    (settlement, person) => new { settlement.Id, Name = person.Name + " · " + settlement.Title })
                .ToDictionaryAsync(settlement => settlement.Id, settlement => settlement.Name, ct);
            foreach (var row in rows)
            {
                row.ObligationName = row.ReminderId is { } reminderId
                    ? reminderNames.GetValueOrDefault(reminderId)
                    : row.SettlementId is { } settlementId ? settlementNames.GetValueOrDefault(settlementId) : null;
            }
            var quotaLabels = await PasanacoImportQuotas.DescribeAsync(_db, userId.Value, batch.Rows, ct);
            foreach (var row in rows)
            {
                if (row.PasanacoParticipantId is { } participantId && row.PasanacoQuotaMonth is { } quotaMonth &&
                    quotaLabels.TryGetValue((participantId, quotaMonth), out var label))
                    row.PasanacoQuotaLabel = label;
            }
            var plannedIds = rows.SelectMany(row => row.PlannedAllocations)
                .Select(link => link.PlannedEntryId).Distinct().ToList();
            if (plannedIds.Count > 0)
            {
                var planned = await _db.LedgerEntries.AsNoTracking()
                    .Where(entry => entry.UserId == userId.Value && plannedIds.Contains(entry.Id))
                    .Select(entry => new
                    {
                        entry.Id,
                        Name = entry.Concept != null ? entry.Concept.Name : entry.Description,
                        entry.DueDate
                    })
                    .ToDictionaryAsync(entry => entry.Id, ct);
                foreach (var link in rows.SelectMany(row => row.PlannedAllocations))
                {
                    if (!planned.TryGetValue(link.PlannedEntryId, out var entry)) continue;
                    link.PlannedEntryName = entry.Name;
                    link.DueDate = entry.DueDate;
                }
            }

            return Ok(new ImportReviewDto
            {
                Id = batch.Id,
                AccountId = batch.AccountId ?? Guid.Empty,
                FileName = batch.FileName,
                Status = batch.Status.ToString(),
                Rows = rows,
                Concepts = concepts,
                People = await _db.Counterparties.AsNoTracking()
                    .Where(person => person.UserId == userId.Value && person.IsActive)
                    .OrderBy(person => person.Name)
                    .Select(person => new ImportPersonOptionDto(person.Id, person.Name)).ToListAsync(ct),
                Analysis = await AnalyzeSafelyAsync(batch, ct)
            });
        }

        /// <summary>La revisión automática es una ayuda: si falla, la importación sigue funcionando.</summary>
        private async Task<ImportAnalysisDto> AnalyzeSafelyAsync(ImportBatch batch, CancellationToken ct)
        {
            try
            {
                return await ImportBatchAnalyzer.AnalyzeAsync(_db, _application, batch, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Automatic review failed for import batch {BatchId}", batch.Id);
                return new ImportAnalysisDto
                {
                    BankBalance = batch.BankBalance,
                    BankBalanceDate = batch.BankBalanceDate,
                    Findings = new[]
                    {
                        new ImportFindingDto
                        {
                            Severity = ImportBatchAnalyzer.SeverityInfo,
                            Title = "No he podido hacer la revisión automática",
                            Detail = "Algo falló al revisar este extracto. Puedes revisarlo e importarlo igual."
                        }
                    }
                };
            }
        }

        [HttpDelete("{batchId:guid}")]
        public async Task<IActionResult> Discard(Guid batchId, CancellationToken ct)
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
                return Conflict("Una importación aplicada no se puede descartar.");

            batch.Status = ImportBatchStatus.Discarded;
            _db.ImportRows.RemoveRange(batch.Rows);
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
                .Include(r => r.Allocations)
                .Include(r => r.PlannedAllocations)
                .FirstOrDefaultAsync(
                    r => r.Id == rowId && r.BatchId == batchId && r.UserId == userId.Value, ct);
            if (row is null) return NotFound();
            if (row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict("El lote ya se ha aplicado o está descartado.");

            if (row.Allocations.Any(part => part.CounterpartyId.HasValue))
                return BadRequest(new { message = "Usa Editar reparto o quita el gasto compartido antes de cambiar su categoría." });
            if (dto.ConceptId is not null)
            {
                var valid = await _db.Concepts.AnyAsync(
                    c => c.Id == dto.ConceptId.Value && c.UserId == userId.Value && c.IsActive, ct);
                if (!valid) return BadRequest("El concepto no existe o no está activo.");
            }

            row.ConfirmedConceptId = dto.ConceptId;
            row.SuggestionSource = dto.ConceptId is null ? null : "manual";
            row.SuggestionConfidence = dto.ConceptId is null ? null : 1m;
            _db.ImportRowAllocations.RemoveRange(row.Allocations);
            row.Allocations.Clear();
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

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/shared-expense")]
        public async Task<IActionResult> ShareExpense(
            Guid batchId, Guid rowId, [FromBody] ShareImportRowDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var row = await _db.ImportRows.Include(item => item.Batch)
                .Include(item => item.Allocations).Include(item => item.PlannedAllocations)
                .FirstOrDefaultAsync(item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value, ct);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending || row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict(new { message = "Solo puedes compartir un gasto pendiente de importar." });
            if (row.Amount >= 0m || !ImportBatchApplicationService.EntersAsPaid(row) ||
                dto.Amount <= 0m || dto.Amount >= Math.Abs(row.Amount) || decimal.Round(dto.Amount, 2) != dto.Amount)
                return BadRequest(new { message = "La parte que te deben debe ser positiva, tener hasta dos decimales y ser menor que el pago." });
            if (ImportPlannedLinks.HasLink(row))
                return BadRequest(new { message = "Quita el vínculo con los previstos antes de compartir este gasto." });
            if (ImportObligationLinks.HasLink(row))
                return BadRequest(new { message = "Quita el vínculo con el recordatorio o la liquidación antes de compartir este gasto." });
            if (!await _db.Counterparties.AnyAsync(person =>
                person.Id == dto.CounterpartyId && person.UserId == userId.Value && person.IsActive, ct))
                return BadRequest(new { message = "La persona no existe o no está activa. Puedes añadirla en Liquidaciones." });
            if (!await _db.Concepts.AnyAsync(concept =>
                concept.Id == dto.ConceptId && concept.UserId == userId.Value && concept.IsActive &&
                concept.Kind == ConceptKind.Expense, ct))
                return BadRequest(new { message = "Elige la categoría de tu parte del gasto." });
            var advance = await PersonalFinance.Api.Features.Settlements.SharedExpenseImport.GetOrCreateConceptAsync(_db, userId.Value, ct);
            _db.ImportRowAllocations.RemoveRange(row.Allocations);
            row.Allocations.Clear();
            _db.ImportRowAllocations.AddRange(
                new ImportRowAllocation
                {
                    UserId = userId.Value, ImportRowId = row.Id, ConceptId = dto.ConceptId,
                    Amount = Math.Abs(row.Amount) - dto.Amount, SortOrder = 0
                },
                new ImportRowAllocation
                {
                    UserId = userId.Value, ImportRowId = row.Id, ConceptId = advance.Id,
                    Amount = dto.Amount, SortOrder = 1, CounterpartyId = dto.CounterpartyId
                });
            row.ConfirmedConceptId = null;
            row.SuggestionSource = "shared";
            row.SuggestionConfidence = 1m;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpDelete("{batchId:guid}/rows/{rowId:guid}/shared-expense")]
        public async Task<IActionResult> UnshareExpense(Guid batchId, Guid rowId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var row = await _db.ImportRows.Include(item => item.Batch).Include(item => item.Allocations)
                .FirstOrDefaultAsync(item => item.UserId == userId.Value && item.BatchId == batchId && item.Id == rowId, ct);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending || row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict(new { message = "El gasto ya se ha aplicado; no puedes quitar este reparto desde la importación." });
            if (!row.Allocations.Any(part => part.CounterpartyId.HasValue)) return NoContent();
            row.ConfirmedConceptId = row.Allocations.Single(part => part.CounterpartyId is null).ConceptId;
            _db.ImportRowAllocations.RemoveRange(row.Allocations);
            row.Allocations.Clear();
            row.SuggestionSource = "manual";
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/pasanaco-quota")]
        public async Task<IActionResult> SetPasanacoQuota(
            Guid batchId, Guid rowId, [FromBody] SetImportRowPasanacoQuotaDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var row = await _db.ImportRows.Include(item => item.Batch)
                .FirstOrDefaultAsync(item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value, ct);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending || row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict(new { message = "Solo puedes asignar una cuota a un movimiento pendiente de importar." });
            if (row.Amount <= 0m)
                return BadRequest(new { message = "Solo un cobro puede pagar la cuota de un pasanaco." });
            if (ImportObligationLinks.HasLink(row))
                return BadRequest(new { message = "Quita el vínculo con el recordatorio o la liquidación antes de asignar una cuota." });
            if (string.IsNullOrWhiteSpace(dto.ParticipantId) ||
                !DateOnly.TryParseExact(dto.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                return BadRequest(new { message = "Indica el participante y el mes de la cuota." });

            var (quota, error) = await PasanacoImportQuotas.ResolveAsync(_db, userId.Value, dto.ParticipantId, month, ct);
            if (quota is null) return BadRequest(new { message = error });
            var takenBy = await _db.ImportRows.AsNoTracking()
                .Where(item => item.UserId == userId.Value && item.Id != row.Id &&
                    item.Status == ImportRowStatus.Pending &&
                    item.PasanacoParticipantId == quota.ParticipantId && item.PasanacoQuotaMonth == quota.Month &&
                    item.Batch != null &&
                    (item.Batch.Status == ImportBatchStatus.Draft || item.Batch.Status == ImportBatchStatus.Reviewing))
                .Select(item => (int?)item.RowNumber)
                .FirstOrDefaultAsync(ct);
            if (takenBy is { } takenRow)
                return Conflict(new { message = $"Esa cuota ya está asignada a otro movimiento pendiente (fila {takenRow})." });

            row.PasanacoParticipantId = quota.ParticipantId;
            row.PasanacoQuotaMonth = quota.Month;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpDelete("{batchId:guid}/rows/{rowId:guid}/pasanaco-quota")]
        public async Task<IActionResult> ClearPasanacoQuota(Guid batchId, Guid rowId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var row = await _db.ImportRows.Include(item => item.Batch)
                .FirstOrDefaultAsync(item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value, ct);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending || row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict(new { message = "El movimiento ya se ha aplicado; cambia la cuota desde Pasanaco." });
            row.PasanacoParticipantId = null;
            row.PasanacoQuotaMonth = null;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/split")]
        public async Task<ActionResult<ImportRowDto>> SplitRow(
            Guid batchId,
            Guid rowId,
            [FromBody] SplitImportRowDto dto,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var row = await _db.ImportRows
                .Include(item => item.Batch)
                .Include(item => item.Allocations)
                .Include(item => item.PlannedAllocations)
                .FirstOrDefaultAsync(
                    item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value,
                    ct);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending || row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict("Solo se pueden dividir filas pendientes de un lote sin aplicar.");
            if (row.Allocations.Any(part => part.CounterpartyId.HasValue))
                return BadRequest(new { message = "Usa Editar reparto para cambiar el gasto compartido." });
            if (ImportObligationLinks.HasLink(row))
                return BadRequest(new { message = "Quita el vínculo con el recordatorio o la liquidación antes de dividir el movimiento." });
            if (row.Amount == 0m)
                return BadRequest("No se puede dividir un movimiento de importe cero.");
            if (dto.Allocations is null || dto.Allocations.Count is < 2 or > 3)
                return BadRequest("Divide el movimiento en 2 o 3 partes.");
            if (dto.Allocations.Any(item => item.Amount <= 0m || decimal.Round(item.Amount, 2) != item.Amount))
                return BadRequest("Cada parte debe tener un importe mayor que cero.");
            if (dto.Allocations.Select(item => item.ConceptId).Distinct().Count() != dto.Allocations.Count)
                return BadRequest("Selecciona un concepto distinto para cada parte.");
            if (dto.Allocations.Sum(item => item.Amount) != Math.Abs(row.Amount))
                return BadRequest("La suma de las partes debe coincidir exactamente con el importe del movimiento.");

            var conceptIds = dto.Allocations.Select(item => item.ConceptId).ToHashSet();
            var expectedKind = row.Amount > 0m ? ConceptKind.Income : ConceptKind.Expense;
            var concepts = await _db.Concepts
                .Where(item => item.UserId == userId.Value && item.IsActive && conceptIds.Contains(item.Id))
                .ToListAsync(ct);
            if (concepts.Count != conceptIds.Count || concepts.Any(item => item.Kind != expectedKind))
                return BadRequest("Cada parte debe usar un concepto activo del mismo tipo que el movimiento.");

            _db.ImportRowAllocations.RemoveRange(row.Allocations);
            row.Allocations.Clear();
            for (var index = 0; index < dto.Allocations.Count; index++)
            {
                var allocation = dto.Allocations[index];
                _db.ImportRowAllocations.Add(new ImportRowAllocation
                {
                    UserId = userId.Value,
                    ImportRowId = row.Id,
                    ConceptId = allocation.ConceptId,
                    Amount = allocation.Amount,
                    SortOrder = index
                });
            }
            row.ConfirmedConceptId = null;
            row.SuggestionSource = "split";
            row.SuggestionConfidence = 1m;
            // A split row cannot be deducted from a planned entry.
            row.PlannedEntryId = null;
            row.PlannedAllocations.Clear();

            await _db.SaveChangesAsync(ct);
            var response = MapRow(row);
            response.Allocations = await _db.ImportRowAllocations
                .AsNoTracking()
                .Where(item => item.ImportRowId == row.Id)
                .Select(item => new ImportRowAllocationDto
                {
                    ConceptId = item.ConceptId,
                    Amount = item.Amount,
                    CounterpartyId = item.CounterpartyId
                })
                .ToListAsync(ct);
            return Ok(response);
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
                .Include(item => item.Allocations)
                .Include(item => item.PlannedAllocations)
                .FirstOrDefaultAsync(
                    item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value,
                    ct);
            if (row is null) return NotFound();
            if (row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict("El lote ya se ha aplicado o está descartado.");

            if (row.Allocations.Any(part => part.CounterpartyId.HasValue))
                return BadRequest(new { message = "Usa Editar reparto o quita el gasto compartido antes de cambiar su categoría." });
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
            _db.ImportRowAllocations.RemoveRange(row.Allocations);
            row.Allocations.Clear();

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
            if (batch.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict("El lote ya se ha aplicado o está descartado.");

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
                    row.NormalizedDescription == normalizedDescription &&
                    !row.Allocations.Any())
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
            if (batch.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict("El lote ya se ha aplicado o está descartado.");
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
            var history = await ImportHistoryClassifier.LoadAsync(_db, userId.Value, ct);
            Guid? Learned(ImportRow row) =>
                history.TryGetValue(
                    ImportHistoryClassifier.Key(row.NormalizedDescription ?? row.RawDescription, row.Amount),
                    out var conceptId)
                    ? conceptId
                    : null;
            bool AlreadySuggestedByAi(ImportRow row) =>
                row.SuggestedConceptId is not null && row.SuggestionSource == "ai";
            var descriptionsForAi = pendingRows
                .Where(row =>
                    !AlreadySuggestedByAi(row) &&
                    FindMapping(
                        mappings,
                        batch.AccountId.Value,
                        row.NormalizedDescription ?? string.Empty) is null &&
                    Learned(row) is null)
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

                if (Learned(row) is { } learnedConceptId)
                {
                    row.SuggestedConceptId = learnedConceptId;
                    row.SuggestionSource = ImportHistoryClassifier.Source;
                    row.SuggestionConfidence = 0.9m;
                    mapped++;
                    continue;
                }

                if (AlreadySuggestedByAi(row))
                {
                    suggested++;
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
                    .ThenInclude(row => row.Allocations)
                .Include(b => b.Rows)
                    .ThenInclude(row => row.PlannedAllocations)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();
            if (batch.Status == ImportBatchStatus.Discarded)
                return Conflict("El lote está descartado.");

            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value && c.IsActive)
                .Select(c => new ImportAiCandidate(c.Id, c.Name, c.Kind.ToString()))
                .ToListAsync(ct);
            var conceptNames = concepts.ToDictionary(c => c.Id, c => c.Name);
            var analysis = ImportBatchAnalyzer.ToChatLines(await AnalyzeSafelyAsync(batch, ct));

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
                        : null)
                {
                    PlannedEntryId = r.PlannedEntryId,
                    ReminderId = r.ReminderId,
                    SettlementId = r.SettlementId,
                    PlannedAllocations = r.PlannedAllocations.Select(link => new ImportPlannedAllocationDto
                    {
                        PlannedEntryId = link.PlannedEntryId,
                        Amount = link.Amount
                    }).ToList(),
                    IsSplit = r.Allocations.Count > 0,
                    SharedExpenseCounterpartyId = r.Allocations.FirstOrDefault(part => part.CounterpartyId.HasValue)?.CounterpartyId,
                    SharedExpenseAmount = r.Allocations.FirstOrDefault(part => part.CounterpartyId.HasValue)?.Amount,
                    PasanacoParticipantId = r.PasanacoParticipantId,
                    PasanacoQuotaMonth = r.PasanacoQuotaMonth
                })
                .ToList();

            var history = dto.History
                .Where(h => h.Role is "user" or "assistant")
                .Select(h => new ImportChatMessage(h.Role, h.Content))
                .ToList();

            var extra = await LoadChatContextAsync(userId.Value, ct);
            var searchTerms = MovementSearchTerms.From(
                history.Where(message => message.Role == "user")
                    .TakeLast(2)
                    .Select(message => message.Content)
                    .Append(dto.Message));
            var planned = batch.Status == ImportBatchStatus.Applied
                ? new List<ImportPlannedCandidateDto>()
                : await LoadPlannedCandidatesAsync(userId.Value, batch, ct, searchTerms);
            var reminders = await _db.Reminders.AsNoTracking()
                .Where(reminder => reminder.UserId == userId.Value && !reminder.IsDone && reminder.Amount > 0m)
                .ToListAsync(ct);
            var openSettlements = await ImportObligationLinks.LoadOpenSettlementsAsync(_db, userId.Value, ct);
            var people = await _db.Counterparties.AsNoTracking()
                .Where(person => person.UserId == userId.Value && person.IsActive)
                .Select(person => new ImportChatPersonContext(person.Id, person.Name)).ToListAsync(ct);
            var personNames = people.ToDictionary(person => person.Id, person => person.Name);
            var pendingRows = batch.Rows.Where(row => row.Status == ImportRowStatus.Pending).ToList();
            ImportChatObligationContext Obligation(Guid id, string title, string direction, DateOnly dueDate,
                decimal amount, string? repeat, bool isReminder)
            {
                var linked = pendingRows.Where(row => isReminder ? row.ReminderId == id : row.SettlementId == id).ToList();
                return new(id, title, direction, dueDate, amount, repeat,
                    linked.Sum(row => Math.Abs(row.Amount)), linked.Select(row => row.Id).ToList());
            }
            IReadOnlyList<ImportChatObligationContext> RankObligations(IEnumerable<ImportChatObligationContext> items) =>
                items.OrderByDescending(item => ImportPendingRelevance.Score(item.Title, item.Amount, searchTerms, batch.Rows))
                    .ThenByDescending(item => item.LinkedRowIds.Count > 0)
                    .ThenBy(item => batch.Rows.Count == 0 ? 0 :
                        batch.Rows.Min(row => Math.Abs(item.DueDate.DayNumber - row.ValueDate.DayNumber)))
                    .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToList();
            extra = extra with
            {
                People = people,
                Reminders = RankObligations(reminders.Select(reminder => Obligation(
                    reminder.Id, reminder.Title, reminder.IsIncome ? "in" : "out",
                    reminder.DueDate, reminder.Amount!.Value, ReminderRules.ToApi(reminder.Repeat), true))),
                Settlements = RankObligations(openSettlements.Values.Select(settlement =>
                {
                    var remaining = SettlementForecast.Remaining(settlement);
                    return Obligation(settlement.Id,
                        $"{personNames.GetValueOrDefault(settlement.CounterpartyId)} · {settlement.Title}",
                        remaining > 0m ? "in" : "out",
                        settlement.ExpectedDate ?? SettlementForecast.DefaultDate(settlement),
                        Math.Abs(remaining), null, false);
                })),
                PlannedEntries = planned
                    .Select(p => new ImportChatPlannedContext(
                        p.Id,
                        p.ConceptName,
                        p.Direction,
                        p.DueDate,
                        p.ForecastAmount,
                        p.AccountName,
                        p.Description,
                        p.LinkedAmount,
                        p.RemainingAfterLinks,
                        p.LinkedRowIds)
                    {
                        ConceptId = p.ConceptId
                    })
                    .ToList(),
                Pasanacos = batch.Status == ImportBatchStatus.Applied
                    ? Array.Empty<PasanacoImportSummary>()
                    : await PasanacoImportQuotas.LoadActiveAsync(_db, userId.Value, ct),
                Today = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            var result = await _chat.AskAsync(dto.Message, history, rowContexts, concepts, analysis, extra, ct);

            // El asistente sólo propone: nada cambia hasta que el usuario confirma
            // cada propuesta con los mismos endpoints que usa la revisión manual.
            return Ok(new ImportChatResponseDto
            {
                Reply = result.Reply,
                AppliedChanges = 0,
                Unrecognized = result.Unrecognized,
                ProposedActions = result.Proposals
                    .Select(p => new ImportChatProposedActionDto
                    {
                        Type = p.Type,
                        Summary = p.Summary,
                        RowIds = p.RowIds,
                        ConceptId = p.ConceptId,
                        ConceptName = p.ConceptName,
                        CounterpartyId = p.CounterpartyId,
                        CounterpartyName = p.CounterpartyName,
                        Name = p.Name,
                        GroupId = p.GroupId,
                        GroupName = p.GroupName,
                        Nature = p.Nature,
                        SaveForFuture = p.SaveForFuture,
                        PlannedEntryId = p.PlannedEntryId,
                        ReminderId = p.ReminderId,
                        SettlementId = p.SettlementId,
                        PlannedEntryName = p.PlannedEntryName,
                        PlannedAllocations = p.PlannedAllocations ?? Array.Empty<ImportPlannedAllocationDto>(),
                        Amount = p.Amount,
                        DueDate = p.DueDate,
                        Title = p.Title,
                        Repeat = p.Repeat,
                        NotifyDaysBefore = p.NotifyDaysBefore,
                        Notes = p.Notes,
                        IsIncome = p.IsIncome,
                        PasanacoParticipantId = p.PasanacoParticipantId,
                        PasanacoMonth = p.PasanacoMonth,
                        Pasanaco = p.Pasanaco
                    })
                    .ToList()
            });
        }

        [HttpGet("{batchId:guid}/planned-candidates")]
        public async Task<ActionResult<IReadOnlyList<ImportPlannedCandidateDto>>> GetPlannedCandidates(
            Guid batchId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var batch = await _db.ImportBatches
                .AsNoTracking()
                .Include(b => b.Rows)
                    .ThenInclude(row => row.Allocations)
                .Include(b => b.Rows)
                    .ThenInclude(row => row.PlannedAllocations)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();
            if (batch.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return BadRequest(new { message = "El lote ya se ha aplicado o está descartado." });

            return Ok(await LoadPlannedCandidatesAsync(userId.Value, batch, ct));
        }

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/planned-entry")]
        public async Task<IActionResult> SetRowPlannedEntry(
            Guid batchId, Guid rowId, [FromBody] SetImportRowPlannedEntryDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var batch = await _db.ImportBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId.Value, ct);
            if (batch is null) return NotFound();
            if (batch.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return BadRequest(new { message = "El lote ya se ha aplicado o está descartado." });

            var row = await _db.ImportRows
                .Include(item => item.Allocations)
                .Include(item => item.PlannedAllocations)
                .FirstOrDefaultAsync(
                    item => item.Id == rowId && item.BatchId == batchId && item.UserId == userId.Value, ct);
            if (row is null)
                return BadRequest(new { message = "El movimiento no pertenece a esta importación." });

            if (dto?.PlannedEntryId is not { } plannedEntryId)
            {
                row.PlannedEntryId = null;
                row.PlannedAllocations.Clear();
                await _db.SaveChangesAsync(ct);
                return NoContent();
            }

            if (row.Status != ImportRowStatus.Pending)
                return BadRequest(new { message = "Este movimiento ya no está pendiente; no se puede descontar de un previsto." });
            if (ImportObligationLinks.HasLink(row))
                return BadRequest(new { message = "Quita primero el vínculo con el recordatorio o la liquidación." });

            var planned = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.Id == plannedEntryId && entry.UserId == userId.Value)
                .Select(entry => new
                {
                    entry.ConceptId,
                    entry.Direction,
                    entry.Status,
                    entry.IsTransfer,
                    entry.ImportRowId,
                    ConceptIsActive = entry.Concept != null && entry.Concept.IsActive
                })
                .FirstOrDefaultAsync(ct);
            if (planned is null ||
                planned.Status is not (EntryStatus.Planned or EntryStatus.Pending) ||
                planned.ImportRowId is not null)
                return BadRequest(new { message = "El previsto no existe o ya no está pendiente." });
            if (planned.IsTransfer)
                return BadRequest(new { message = "Ese previsto es un traspaso; no se puede descontar de él." });

            var error = ImportPlannedLinks.LinkError(row.Amount, row.Allocations.Count > 0, planned.Direction);
            if (error is not null) return BadRequest(new { message = error });

            row.PlannedEntryId = plannedEntryId;
            row.PlannedAllocations.Clear();
            if (row.ConfirmedConceptId is null && planned.ConceptIsActive)
            {
                row.ConfirmedConceptId = planned.ConceptId;
                row.SuggestionSource = "manual";
                row.SuggestionConfidence = 1m;
            }

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/planned-allocations")]
        public async Task<IActionResult> SetRowPlannedAllocations(
            Guid batchId, Guid rowId, [FromBody] SetImportRowPlannedAllocationsDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var row = await _db.ImportRows
                .Include(item => item.Batch)
                .Include(item => item.Allocations)
                .Include(item => item.PlannedAllocations)
                .FirstOrDefaultAsync(item => item.Id == rowId && item.BatchId == batchId &&
                    item.UserId == userId.Value, ct);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending || row.Batch?.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict(new { message = "Solo se pueden repartir movimientos pendientes de una importación sin aplicar." });
            if (dto.Allocations is null) return BadRequest(new { message = "El reparto es obligatorio." });
            var links = dto.Allocations.Select(link => (link.PlannedEntryId, link.Amount)).ToList();
            if (links.Count == 0)
            {
                row.PlannedEntryId = null;
                row.PlannedAllocations.Clear();
                await _db.SaveChangesAsync(ct);
                return NoContent();
            }
            var ids = links.Select(link => link.PlannedEntryId).ToList();
            if (ImportObligationLinks.HasLink(row))
                return BadRequest(new { message = "Quita primero el vínculo con el recordatorio o la liquidación." });
            var entries = await _db.LedgerEntries
                .Include(entry => entry.Concept)
                .Include(entry => entry.Period)
                .Where(entry => entry.UserId == userId.Value && ids.Contains(entry.Id))
                .ToDictionaryAsync(entry => entry.Id, ct);
            var error = ImportPlannedLinks.AllocationError(row, links, entries);
            if (error is not null) return BadRequest(new { message = error });
            var siblings = await _db.ImportRows.AsNoTracking()
                .Include(item => item.PlannedAllocations)
                .Include(item => item.Allocations)
                .Where(item => item.BatchId == batchId && item.UserId == userId.Value &&
                    item.Id != rowId && item.Status == ImportRowStatus.Pending)
                .ToListAsync(ct);
            foreach (var link in links)
            {
                var otherAmount = siblings.Where(ImportPlannedLinks.Deducts).SelectMany(ImportPlannedLinks.Links)
                    .Where(other => other.PlannedEntryId == link.PlannedEntryId).Sum(other => other.Amount);
                if (link.Amount + otherAmount > entries[link.PlannedEntryId].ForecastAmount + ImportPlannedLinks.CoverTolerance)
                    return BadRequest(new { message = "El reparto supera lo pendiente de un previsto. Revisa los importes." });
            }
            error = await ImportPlannedLinks.CareAllocationErrorAsync(_db, userId.Value, links, entries, ct);
            if (error is not null) return BadRequest(new { message = error });
            row.PlannedEntryId = null;
            row.PlannedAllocations.Clear();
            foreach (var link in links)
                _db.ImportRowPlannedAllocations.Add(new ImportRowPlannedAllocation
                {
                    ImportRowId = row.Id,
                    PlannedEntryId = link.PlannedEntryId,
                    Amount = link.Amount
                });
            row.ConfirmedConceptId = entries[links[0].PlannedEntryId].ConceptId;
            row.SuggestionSource = "manual";
            row.SuggestionConfidence = 1m;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpPut("{batchId:guid}/rows/{rowId:guid}/obligation")]
        public async Task<IActionResult> SetRowObligation(
            Guid batchId, Guid rowId, [FromBody] SetImportRowObligationDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var batch = await _db.ImportBatches.Include(item => item.Rows).ThenInclude(row => row.Allocations)
                .Include(item => item.Rows).ThenInclude(row => row.PlannedAllocations)
                .FirstOrDefaultAsync(item => item.Id == batchId && item.UserId == userId.Value, ct);
            if (batch is null) return NotFound();
            if (batch.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded)
                return Conflict(new { message = "El lote ya se ha aplicado o está descartado." });
            var row = batch.Rows.FirstOrDefault(row => row.Id == rowId && row.UserId == userId.Value);
            if (row is null) return NotFound();
            if (row.Status != ImportRowStatus.Pending)
                return Conflict(new { message = "El movimiento ya no está pendiente de importar." });
            row.ReminderId = dto.ReminderId;
            row.ReminderDueDate = dto.ReminderId.HasValue ? dto.ReminderDueDate : null;
            row.SettlementId = dto.SettlementId;
            if (!ImportObligationLinks.HasLink(row))
            {
                await _db.SaveChangesAsync(ct);
                return NoContent();
            }
            var validation = await ImportObligationLinks.ValidateAsync(_db, userId.Value, batch.Rows, ct);
            if (validation.Error is { } error) return BadRequest(new { message = error });
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>
        /// Open planned entries around the batch dates, or from any date for chat searches,
        /// including existing links and what this batch will deduct from each entry.
        /// </summary>
        private async Task<List<ImportPlannedCandidateDto>> LoadPlannedCandidatesAsync(
            Guid userId, ImportBatch batch, CancellationToken ct, MovementSearchTerms? searchTerms = null)
        {
            if (batch.Rows.Count == 0) return new List<ImportPlannedCandidateDto>();

            var from = batch.Rows.Min(row => row.ValueDate).AddDays(-ImportPlannedLinks.CandidateWindowDays);
            var to = batch.Rows.Max(row => row.ValueDate).AddDays(ImportPlannedLinks.CandidateWindowDays);
            var linkedRows = batch.Rows.Where(ImportPlannedLinks.Deducts).ToList();
            var linkedIds = linkedRows.SelectMany(ImportPlannedLinks.Links)
                .Select(link => link.PlannedEntryId).Distinct().ToList();
            var searchAllDates = searchTerms is not null;

            var entries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(ImportPlannedLinks.IsOpenExpression)
                .Where(entry =>
                    entry.UserId == userId &&
                    (searchAllDates || (entry.DueDate >= from && entry.DueDate <= to) || linkedIds.Contains(entry.Id)))
                .Select(entry => new
                {
                    entry.Id,
                    entry.ConceptId,
                    ConceptName = entry.Concept != null ? entry.Concept.Name : null,
                    entry.Direction,
                    entry.DueDate,
                    entry.ForecastAmount,
                    entry.AccountId,
                    AccountName = entry.Account != null ? entry.Account.Name : null,
                    entry.Description
                })
                .ToListAsync(ct);

            return entries
                .Select(entry =>
                {
                    var linked = linkedRows
                        .Where(row =>
                            ImportPlannedLinks.Links(row).Any(link => link.PlannedEntryId == entry.Id) &&
                            ImportPlannedLinks.LinkError(row.Amount, false, entry.Direction) is null)
                        .OrderBy(row => row.RowNumber)
                        .ToList();
                    var forecast = Math.Abs(entry.ForecastAmount);
                    var linkedAmount = linked.Sum(row => ImportPlannedLinks.Links(row)
                        .Where(link => link.PlannedEntryId == entry.Id).Sum(link => link.Amount));
                    return new ImportPlannedCandidateDto
                    {
                        Id = entry.Id,
                        ConceptId = entry.ConceptId,
                        ConceptName = entry.ConceptName,
                        Direction = ImportPlannedLinks.DirectionText(entry.Direction),
                        DueDate = entry.DueDate,
                        ForecastAmount = forecast,
                        AccountId = entry.AccountId,
                        AccountName = entry.AccountName,
                        Description = entry.Description,
                        LinkedAmount = linkedAmount,
                        RemainingAfterLinks = forecast - linkedAmount,
                        LinkedRowIds = linked.Select(row => row.Id).ToList()
                    };
                })
                .OrderByDescending(candidate => ImportPendingRelevance.Score(
                    $"{candidate.ConceptName} {candidate.Description}", candidate.ForecastAmount, searchTerms, batch.Rows))
                .ThenByDescending(candidate => searchTerms is not null &&
                    searchTerms.Amounts.Any(range =>
                        (candidate.ForecastAmount >= range.Min && candidate.ForecastAmount <= range.Max) ||
                        (candidate.RemainingAfterLinks >= range.Min && candidate.RemainingAfterLinks <= range.Max)))
                .ThenByDescending(candidate => searchTerms?.Words.Count(word =>
                    (candidate.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (candidate.ConceptName?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (candidate.AccountName?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)) ?? 0)
                .ThenByDescending(candidate => searchAllDates && candidate.LinkedRowIds.Count > 0)
                .ThenByDescending(candidate => searchAllDates && candidate.DueDate >= from && candidate.DueDate <= to)
                .ThenBy(candidate => candidate.DueDate)
                .ThenBy(candidate => candidate.ConceptName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Contexto extra para el chat, siempre del propio usuario: grupos donde
        /// crear categorías, préstamos abiertos y apuntes recientes. Nunca se
        /// envía el teléfono de las contrapartes.
        /// </summary>
        private async Task<ImportChatExtraContext> LoadChatContextAsync(Guid userId, CancellationToken ct)
        {
            var groups = await _db.ConceptGroups
                .AsNoTracking()
                .Where(g => g.UserId == userId && g.IsActive)
                .OrderBy(g => g.SortOrder)
                .Select(g => new ImportChatGroupContext(g.Id, g.Name, g.Kind.ToString()))
                .ToListAsync(ct);

            var bankLoans = await _db.Loans
                .AsNoTracking()
                .Where(l => l.UserId == userId && l.Status.ToLower() == "active" && l.OutstandingAmount > 0m)
                .OrderBy(l => l.Name)
                .Take(50)
                .Select(l => new { l.Type, l.Name, l.OutstandingAmount, l.DueDate })
                .ToListAsync(ct);
            var personalLoans = await _db.PersonalLoans
                .AsNoTracking()
                .Where(p => p.UserId == userId && !p.IsSettled)
                .OrderBy(p => p.Id)
                .Take(50)
                .Select(p => new
                {
                    Counterparty = p.Counterparty != null ? p.Counterparty.Name : null,
                    p.Direction,
                    p.PrincipalAmount,
                    p.Description
                })
                .ToListAsync(ct);

            // SQLite no sabe ordenar DateTimeOffset, así que se ordena en memoria.
            var loans = bankLoans
                .OrderBy(l => l.DueDate ?? DateTimeOffset.MaxValue)
                .Select(l => new ImportChatLoanContext(
                    $"préstamo ({l.Type})",
                    l.Name,
                    l.OutstandingAmount,
                    l.DueDate is { } due ? "vence " + due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null))
                .Concat(personalLoans.Select(p => new ImportChatLoanContext(
                    p.Direction == PersonalLoanDirection.Given ? "dinero que prestó" : "dinero que le prestaron",
                    p.Counterparty ?? "(sin nombre)",
                    p.PrincipalAmount,
                    p.Description)))
                .ToList();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = today.AddDays(-90);
            var to = today.AddDays(31);
            var recent = await _db.LedgerEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId &&
                            e.DueDate >= from &&
                            e.DueDate <= to &&
                            e.Status != EntryStatus.Skipped)
                .OrderByDescending(e => e.DueDate)
                .Take(80)
                .Select(e => new
                {
                    e.DueDate,
                    e.Direction,
                    e.IsTransfer,
                    e.Status,
                    Amount = e.ActualAmount ?? e.ForecastAmount,
                    Concept = e.Concept != null ? e.Concept.Name : null,
                    e.Description
                })
                .ToListAsync(ct);

            return new ImportChatExtraContext(
                groups,
                loans,
                recent
                    .Select(e => new ImportChatLedgerContext(
                        e.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        e.IsTransfer ? "traspaso" : e.Direction == EntryDirection.In ? "ingreso" : "gasto",
                        e.Status switch
                        {
                            EntryStatus.Paid => "pagado",
                            EntryStatus.Pending => "pendiente",
                            _ => "previsto"
                        },
                        e.Amount,
                        e.Concept,
                        e.Description))
                    .ToList());
        }

        [HttpPost("{batchId:guid}/apply")]
        public async Task<ActionResult<object>> Apply(
            Guid batchId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var result = await _application.ApplyAsync(userId.Value, batchId, ct);
            if (result.NotFound) return NotFound();
            if (!result.Success && result.Conflict) return Conflict(new { message = result.Error });
            if (!result.Success)
                return BadRequest(new { message = result.Error, unassignedRows = result.UnassignedRows });

            return Ok(new
            {
                applied = result.Applied,
                matchedForecasts = result.MatchedForecasts,
                settlementPayments = result.SettlementPayments,
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
            var history = await ImportHistoryClassifier.LoadAsync(_db, userId.Value, ct);
            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value && c.IsActive)
                .Select(c => new ImportAiCandidate(c.Id, c.Name, c.Kind.ToString()))
                .ToListAsync(ct);
            var conceptIds = concepts.ToDictionary(
                concept => concept.Name,
                concept => concept.Id,
                StringComparer.OrdinalIgnoreCase);
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
                      importBatch.Status != ImportBatchStatus.Discarded &&
                      importBatch.AccountId == account.Id &&
                      importBatch.Source == ImportSource.RevolutCsv
                select new
                {
                    row.Id,
                    row.ValueDate,
                    row.Amount,
                    row.Fee,
                    row.Currency,
                    row.NormalizedDescription,
                    row.RawDescription,
                    row.ClassifiedStatus,
                    row.Fingerprint,
                    row.RowNumber
                })
                .ToListAsync(ct);
            var existingMovements = existingImportedRows
                .Select(row =>
                {
                    // A fee refund saved before it was moved to the amount must still count as already imported.
                    var (amount, fee) = RevolutCsvParser.MoveFeeRefundToAmount(row.Amount, row.Fee);
                    return new
                    {
                        row.Id,
                        row.ClassifiedStatus,
                        row.Fingerprint,
                        row.RowNumber,
                        NormalizedDescription = row.NormalizedDescription
                            ?? RevolutMovementClassifier.Normalize(row.RawDescription),
                        FinancialKey = CreateMovementKey(
                            row.ValueDate, amount, fee, row.Currency, string.Empty),
                        Key = CreateMovementKey(
                            row.ValueDate,
                            amount,
                            fee,
                            row.Currency,
                            row.NormalizedDescription ?? RevolutMovementClassifier.Normalize(row.RawDescription))
                    };
                })
                .ToList();
            var existingMovementsByKey = existingMovements
                .GroupBy(row => row.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var existingMovementsByFinancialKey = existingMovements
                .GroupBy(row => row.FinancialKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var matchedImportedRows = new HashSet<Guid>();
            var completedPendingRows = new List<(Guid RowId, DateOnly ValueDate)>();

            var batch = new ImportBatch
            {
                UserId = userId.Value,
                AccountId = account.Id,
                Source = ImportSource.RevolutCsv,
                FileName = file.FileName,
                TotalRows = parsed.Movements.Count
            };
            var closing = ImportBatchAnalyzer.FindBankBalance(parsed.Movements, account.Currency);
            batch.BankBalance = closing?.Balance;
            batch.BankBalanceDate = closing?.Date;

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
                var existingMovement = existingMovementsByKey.GetValueOrDefault(movementKey)?
                    .Where(row => !matchedImportedRows.Contains(row.Id))
                    .OrderBy(row => row.ClassifiedStatus == classified.Status ? 0 : 1)
                    .FirstOrDefault();
                if (existingMovement is null)
                {
                    var financialKey = CreateMovementKey(
                        DateOnly.FromDateTime(movement.StartedAt),
                        movement.Amount, movement.Fee, movement.Currency, string.Empty);
                    var renamedMatches = existingMovementsByFinancialKey.GetValueOrDefault(financialKey)?
                        .Where(row => !matchedImportedRows.Contains(row.Id) && row.Key != movementKey)
                        .Where(row => MatchesOriginalMovement(
                            account.Id, movement, row.NormalizedDescription, row.Fingerprint,
                            row.RowNumber, existingMovementsByKey[row.Key].Count))
                        .Take(2)
                        .ToList();
                    if (renamedMatches is { Count: 1 })
                        existingMovement = renamedMatches[0];
                }

                if (fingerprints.Contains(legacyFingerprint) ||
                    existingMovement is not null ||
                    !fingerprints.Add(fingerprint))
                {
                    duplicateRows++;
                    if (existingMovement is not null)
                    {
                        matchedImportedRows.Add(existingMovement.Id);
                        if (classified.Status == EntryStatus.Paid &&
                            existingMovement.ClassifiedStatus == EntryStatus.Pending)
                        {
                            completedPendingRows.Add((
                                existingMovement.Id,
                                DateOnly.FromDateTime(movement.CompletedAt ?? movement.StartedAt)));
                        }
                    }
                    continue;
                }

                var mapping = FindMapping(mappings, account.Id, classified.NormalizedDescription);
                Guid? learned = mapping is null &&
                    history.TryGetValue(
                        ImportHistoryClassifier.Key(classified.NormalizedDescription, movement.Amount),
                        out var learnedConceptId)
                    ? learnedConceptId
                    : null;
                batch.Rows.Add(new ImportRow
                {
                    UserId = userId.Value,
                    RowNumber = movement.RowNumber,
                    ValueDate = DateOnly.FromDateTime(movement.StartedAt),
                    SettledDate = classified.Status == EntryStatus.Paid && movement.CompletedAt is { } completedAt
                        ? DateOnly.FromDateTime(completedAt)
                        : null,
                    Amount = movement.Amount,
                    Fee = movement.Fee,
                    ClassifiedStatus = classified.Status,
                    Currency = movement.Currency,
                    RawDescription = movement.Description,
                    NormalizedDescription = classified.NormalizedDescription,
                    Fingerprint = fingerprint,
                    Status = ImportRowStatus.Pending,
                    SuggestedConceptId = mapping?.ConceptId ?? learned,
                    SuggestionSource = mapping is not null ? "mapping" : learned is not null ? ImportHistoryClassifier.Source : null,
                    SuggestionConfidence = mapping is not null ? 1m : learned is not null ? 0.9m : null
                });
            }

            var unmappedDescriptions = batch.Rows
                .Where(row => row.SuggestedConceptId is null)
                .Select(row => row.NormalizedDescription ?? RevolutMovementClassifier.Normalize(row.RawDescription))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var aiSuggestions = unmappedDescriptions.Count == 0
                ? new Dictionary<string, ImportAiSuggestion>()
                : await _aiSuggestions.SuggestAsync(unmappedDescriptions, concepts, ct);
            foreach (var row in batch.Rows.Where(row => row.SuggestedConceptId is null))
            {
                var normalized = row.NormalizedDescription ?? RevolutMovementClassifier.Normalize(row.RawDescription);
                aiSuggestions.TryGetValue(normalized, out var suggestion);
                var fallback = suggestion is null
                    ? ImportFallbackClassifier.FindConceptId(normalized, row.Amount, conceptIds)
                    : null;
                row.SuggestedConceptId = suggestion?.ConceptId ?? fallback;
                row.SuggestionSource = suggestion is not null ? "ai" : fallback is not null ? "regla general" : null;
                row.SuggestionConfidence = suggestion?.Confidence ?? (fallback is not null ? 0.5m : null);
            }

            var confirmedPendingRows = await ConfirmCompletedPendingEntriesAsync(
                userId.Value,
                completedPendingRows,
                ct);

            batch.AcceptedRows = batch.Rows.Count;
            batch.DuplicateRows = duplicateRows;
            batch.ExcludedRows = excludedRows;
            if (batch.AcceptedRows == 0)
            {
                if (completedPendingRows.Count > 0)
                    await _db.SaveChangesAsync(ct);

                // Aunque no haya nada nuevo, el extracto sirve para comprobar que el saldo cuadra.
                var balanceCheck = duplicateRows > 0 && closing is { } bank
                    ? await CheckBalanceSafelyAsync(account, bank.Balance, bank.Date, file.FileName, ct)
                    : null;
                return Conflict(new
                {
                    message = duplicateRows > 0
                        ? "Todos los movimientos del archivo ya están aplicados al libro contable." +
                          DescribeConfirmedPendingRows(confirmedPendingRows) +
                          (balanceCheck is null ? string.Empty : $" {balanceCheck.Title}: {balanceCheck.Detail}")
                        : "El archivo no contiene movimientos importables.",
                    duplicateRows,
                    excludedRows,
                    confirmedPendingRows,
                    balanceCheck
                });
            }

            await LinkAutomaticRemindersSafelyAsync(userId.Value, batch, ct);
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
                ConfirmedPendingRows = confirmedPendingRows,
                Problems = parsed.Problems
            });
        }

        private async Task LinkAutomaticRemindersSafelyAsync(Guid userId, ImportBatch batch, CancellationToken ct)
        {
            try
            {
                var matches = await ImportObligationLinks.FindAutomaticRemindersAsync(_db, userId, batch.Rows.ToList(), ct);
                foreach (var row in batch.Rows)
                {
                    if (!matches.TryGetValue(row.Id, out var reminder)) continue;
                    row.ReminderId = reminder.Id;
                    row.ReminderDueDate = reminder.DueDate;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Automatic reminder linking failed for import batch {BatchId}", batch.Id);
            }
        }

        private async Task<ImportFindingDto?> CheckBalanceSafelyAsync(
            Account account, decimal bankBalance, DateOnly bankDate, string? fileName, CancellationToken ct)
        {
            try
            {
                var check = await ImportBatchAnalyzer.CheckBalanceAsync(
                    _db, account, bankBalance, bankDate, 0m, applied: true, fileName, ct);
                return check.Finding;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Balance check failed for account {AccountId}", account.Id);
                return null;
            }
        }

        /// <summary>
        /// Marca como pagados los asientos que se importaron PENDIENTES y que el banco ya
        /// muestra COMPLETADOS. Devuelve cuántos movimientos del banco se han confirmado.
        /// </summary>
        private async Task<int> ConfirmCompletedPendingEntriesAsync(
            Guid userId,
            IReadOnlyCollection<(Guid RowId, DateOnly ValueDate)> completedRows,
            CancellationToken ct)
        {
            if (completedRows.Count == 0) return 0;

            var rowIds = completedRows.Select(item => (Guid?)item.RowId).ToList();
            var importedRows = await _db.ImportRows
                .Where(row => row.UserId == userId && rowIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, ct);
            var pendingEntries = await _db.LedgerEntries
                .Where(entry =>
                    entry.UserId == userId &&
                    entry.Status == EntryStatus.Pending &&
                    rowIds.Contains(entry.ImportRowId))
                .ToListAsync(ct);

            var confirmed = 0;
            foreach (var (rowId, valueDate) in completedRows)
            {
                if (importedRows.TryGetValue(rowId, out var importedRow))
                {
                    importedRow.ClassifiedStatus = EntryStatus.Paid;
                    importedRow.SettledDate = valueDate;
                }
                var rowEntries = pendingEntries.Where(entry => entry.ImportRowId == rowId).ToList();
                if (rowEntries.Count == 0) continue;

                foreach (var entry in rowEntries)
                {
                    entry.Status = EntryStatus.Paid;
                    entry.ValueDate = valueDate;
                    entry.ActualAmount = entry.ForecastAmount;
                    entry.UpdatedAt = DateTime.UtcNow;
                }
                confirmed++;
            }

            return confirmed;
        }

        private static string DescribeConfirmedPendingRows(int confirmed) => confirmed switch
        {
            0 => string.Empty,
            1 => " Se ha marcado como pagado 1 movimiento que seguía pendiente.",
            _ => $" Se han marcado como pagados {confirmed} movimientos que seguían pendientes."
        };

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
            SettledDate = row.SettledDate,
            Amount = row.Amount,
            Currency = row.Currency,
            RawDescription = row.RawDescription,
            NormalizedDescription = row.NormalizedDescription,
            Status = row.Status.ToString(),
            SuggestedConceptId = row.SuggestedConceptId,
            ConfirmedConceptId = row.ConfirmedConceptId,
            SuggestionSource = row.SuggestionSource,
            PlannedEntryId = row.PlannedEntryId,
            ReminderId = row.ReminderId,
            ReminderDueDate = row.ReminderDueDate,
            SettlementId = row.SettlementId,
            PasanacoParticipantId = row.PasanacoParticipantId,
            PasanacoQuotaMonth = row.PasanacoQuotaMonth,
            PlannedAllocations = row.PlannedAllocations
                .Select(link => new ImportPlannedAllocationDto
                {
                    PlannedEntryId = link.PlannedEntryId,
                    Amount = link.Amount
                }).ToList(),
            Allocations = row.Allocations
                .OrderBy(item => item.SortOrder)
                .Select(item => new ImportRowAllocationDto
                {
                    ConceptId = item.ConceptId,
                    Amount = item.Amount,
                    CounterpartyId = item.CounterpartyId
                })
                .ToList()
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

        private static bool MatchesOriginalMovement(
            Guid accountId,
            RevolutMovement movement,
            string originalDescription,
            string fingerprint,
            int originalRowNumber,
            int maxOccurrences)
        {
            var identity = CreateMovementIdentity(accountId, movement, originalDescription);
            for (var occurrence = 1; occurrence <= maxOccurrences; occurrence++)
            {
                if (CreateFingerprint(identity, occurrence) == fingerprint)
                    return true;
            }

            return CreateLegacyFingerprint(accountId, movement with
            {
                Description = originalDescription,
                RowNumber = originalRowNumber
            }) == fingerprint;
        }
    }
}
