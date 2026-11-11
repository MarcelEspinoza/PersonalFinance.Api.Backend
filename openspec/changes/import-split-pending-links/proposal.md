## Why

A single bank receipt can combine a loan receipt and repayment of a settlement. Category splits currently cannot consume either pending source, leaving duplicate forecasts after import.

## What Changes

- Allow each ordinary import split part to explicitly select an open forecast or an open settlement.
- Persist and display these links without booking money during review.
- Revalidate ownership, direction, category, available amount and closed periods before applying.
- Apply all parts atomically, recording their sum once and consuming only each linked part.
- Preserve existing category-only splits and shared-expense behavior.
- Dismiss import success messages after three seconds while retaining actionable errors.
- Add local search to the existing pending-forecast picker as requested.

## Capabilities

### New Capabilities

- `import-split-pending-links`: Independently reconcile the parts of a mixed bank movement.

### Modified Capabilities

None.

## Impact

Import allocation persistence and DTOs, review/apply endpoints, existing forecast and settlement reconciliation, and the frontend split editor. A nullable-column migration is required. No financial records are changed without explicit approval; no tests are written or run.
