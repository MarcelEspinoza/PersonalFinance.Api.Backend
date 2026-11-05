## Why

Zeus needs a simple record of purchased care hours and daily attendance. Linking each purchase to a ledger expense keeps the hour balance separate from money without counting the same purchase twice.

## What Changes

- Add a Zeus page showing available hours, purchased passes and daily usage.
- Offer 60-hour, 120-hour and custom passes, with cumulative balances and no expiry.
- Record usage in hours and minutes, consuming the oldest available pass first.
- Link an existing paid expense or explicitly create one purchase expense atomically.
- Support correcting attendance and removing unused passes without deleting their expenses.

## Capabilities

### New Capabilities

- `zeus-hour-passes`: User-owned cumulative hour passes, dated usage, expense linking and a responsive management page.

### Modified Capabilities

None.

## Impact

Backend authenticated endpoints, two additive database tables and ledger edit guards. Frontend navigation, a Zeus page and API client. No new dependencies, tests, live records, migration execution, commits or pushes without approval.
