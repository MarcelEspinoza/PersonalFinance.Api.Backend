## Context

The split editor stores category and amount only. Whole-row forecast and settlement links reject splits. The import application already supports explicit forecast deductions, settlement payments and duplicate bank fingerprints, which must be reused.

## Goals / Non-Goals

**Goals:** Allow explicit per-part links, retain one bank-row identity, avoid duplicate forecasts and settlement credits, show the selected links, and expire transient import confirmations.

**Non-Goals:** Creating loans, automatically applying the current import, changing actual financial records, linking reminders per part, or treating variable budgets as payable forecasts.

## Decisions

- Add nullable forecast and settlement identifiers to split allocations. A part can have at most one target. Existing unlinked allocations remain valid.
- Project linked parts into non-persisted reconciliation inputs, preserving their signed partial amounts, parent bank-row identity and actual booking identifier. Reuse existing forecast validation/deduction and settlement validation/payment helpers rather than separate accounting rules.
- Validate explicit targets on review save and again on apply. Category, direction, ownership, active status, closed periods and aggregate batch capacity remain enforced.
- Wrap application in a serializable transaction, using the existing concrete DbContext transaction pattern. All internal saves, forecast changes and settlement payments commit together.
- Load selectable forecasts and settlements on opening the split editor, with visible loading failures and no save while targets are unavailable. Search locally; changing a category clears its target.
- Preserve category-only splitting and make unlinked parts explicit. Success messages expire after three seconds; errors and review warnings do not.

## Risks / Trade-offs

- Stale pending sources or another applied batch can invalidate a saved split: fail visibly before committing; never fall back to booking an unlinked part.
- Repeat import or retry can double-credit a settlement: reuse original bank fingerprint checks and accepted batch protection.
- Account transfers must not settle third-party debts: retain existing split category restrictions and reject conflicting whole-row links.

## Migration Plan

Generate and inspect an additive nullable-column migration, retaining existing encrypted token TEXT mappings. Deploy backend before frontend only after approval. No production import is applied as part of this implementation.
