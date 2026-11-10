## Why

Imported payments and receipts can leave their existing forecasts or reminders outstanding and count the same money twice. The import assistant cannot see reminders, can omit relevant forecasts from its limited context, and silently discards invalid proposals, leaving users without actionable cards.

## What Changes

- Review incoming and outgoing movements against open forecasts, reminders and settlements before creating unrelated entries.
- Automatically match only unambiguous, well-identified payments; require an explicit card for ambiguous or partial matches.
- Expose active reminders to the import assistant and persist confirmed reminder links until the import is applied.
- Complete or reduce linked reminders with the bank movement, without creating a second cash transaction.
- Prioritize relevant pending items rather than the oldest items when constructing bounded AI context.
- Surface rejected proposals and malformed responses in the existing assistant panel instead of silently losing action cards.
- Preserve existing settlement matching, recurring forecasts, closed periods and already-paid entries.

## Capabilities

### New Capabilities

- `import-pending-reconciliation`: Safe reconciliation of imported bank movements with outstanding forecasts, reminders and settlements.
- `reliable-import-action-cards`: Validated, visible import assistant proposals and actionable explanations when cards cannot be generated.

### Modified Capabilities

None.

## Impact

Backend import matching, reminder completion rules, import review/chat endpoints and DTOs; frontend import action cards and linked-row feedback. An additive migration persists reminder links. No new packages, production data changes, automatic deployment, commits or pushes. Validation uses builds and local previews only; no tests are written or run.
