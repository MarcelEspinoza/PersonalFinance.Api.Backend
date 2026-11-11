## Context

The importer already matches nearby forecasts and exact settlement nets. Its assistant receives forecasts but no reminders or settlement identifiers. Forecast context is capped at 100 items, and invalid proposals are silently dropped. The frontend also ignores the response's `unrecognized` messages.

## Goals / Non-Goals

**Goals:** reconcile both cash directions once; make outstanding reminders and settlements actionable; keep bounded context relevant; expose missing-card reasons without another model request.

**Non-Goals:** reclassifying unrelated historical transactions, matching by amount alone, changing production data, deploying, adding packages, writing or running tests.

## Decisions

- Preserve existing planned-entry and settlement flows. Extend exact forecast matching to well-identified outstanding items outside the narrow date/account window, including unassigned accounts. An identifiable name and a unique exact amount/direction are required for these additional automatic matches. Overdue items remain eligible; future automatic targets are limited to the existing 31-day candidate horizon.
- Store optional reminder or settlement identifiers on an import row. A reminder link also snapshots its due date, preventing a recurring reminder's next occurrence from being consumed accidentally. Use an additive migration and the existing draft-confirm/apply lifecycle rather than completing a reminder immediately from chat.
- Expose one obligation-link endpoint shared by the action cards. Links are owned by the authenticated user, mutually exclusive with planned links, split allocations and Pasanaco quotas, and revalidated when applying.
- Apply confirmed obligation links to the same bank movement already imported. A fully paid one-off reminder is completed; a fully paid repeating reminder advances using existing reminder rules. Explicit partial payments reduce a one-off reminder's remaining amount; partial repeating reminders are rejected because their next occurrence must retain its normal amount. A shortfall of 0.05 or less completes the reminder and is written off as a loss, for both one-off and repeating reminders.
- Make active reminders and open settlements available in the import assistant context. Rank all pending sources by the user's words, bank descriptions, amounts and dates before applying prompt limits. Exclude canceled loans.
- Keep ambiguous bank descriptions manual. Automatic reminder links are stored on the draft rows at upload time, so the user sees them and can unlink them; apply does not add hidden reminder links afterwards, which keeps an unlinked row unlinked. A generic Bizum (no meaningful identifier) is linked to a reminder only when exactly one open reminder in the same direction is covered by its amount within the 0.05 tolerance and no open forecast has the same amount; a bank text naming someone else is never linked automatically.
- Require the exact remaining amount for every automatic forecast match, including recurring rules. The previous 0.5-to-2 amount ratio can mistake a partial payment for full payment. Differing amounts use the existing explicit link/allocation review instead.
- Retain server validation of model actions. Show rejected-action reasons and malformed-response errors in the existing panel. Do not retry the model automatically or fabricate executable cards from prose.
- Reuse one import category picker for groups, rows, splits and shared expenses. Preserve the native select and its required/disabled behavior; a nearby search button opens an existing accessible dialog with local, accent-insensitive filtering. Sort by category type and Spanish alphabetical name without changing category identifiers, amounts, allocations or API calls.
- Share the existing text normalization with ledger search rather than duplicating its accent-folding behavior. Category search must not call the AI or change a selection before the user chooses a result.

## Risks / Trade-offs

- Similar names or equal amounts can refer to different people → require a unique meaningful identifier and reserve ambiguous matches for confirmation.
- A reminder or settlement can change after review → validate ownership, status, due-date snapshot, direction and remaining amount again during apply.
- A bank row could satisfy several sources → enforce one explicit obligation per row and prevent automatic forecast/settlement matching from consuming it again.
- Category labels do not establish whether Carlos owed a loan or a reimbursement → do not infer a loan from a reminder alone.
- The new draft fields require backend schema compatibility → deploy the additive migration before clients use the new cards; no live migration is executed in this task.

## Migration Plan

Generate and inspect an additive EF migration. Build both projects without tests. Review the local diff and preview the cards with synthetic local responses, without importing bank data or calling the paid AI service. Deployment and production migration require separate approval.

## Open Questions

None blocking. Ambiguous source selection remains an explicit user decision in the assistant.
