## Why

Zeus needs a simple record of purchased care hours and daily attendance. Linking each purchase to a ledger expense keeps the hour balance separate from money without counting the same purchase twice.

## What Changes

- Add a Zeus page showing available hours, purchased passes and daily usage.
- Offer 60-hour, 120-hour and custom passes, with cumulative balances and no expiry.
- Record usage using a date and entry/exit times, calculating minutes automatically and consuming the oldest available pass first.
- Link an existing paid expense or explicitly create one purchase expense atomically.
- Support correcting attendance and removing unused passes without deleting their expenses.
- Preload all daytime daycare brochure tariffs without the optional discount.
- Track daycare nights and care by Zeus's nanny Alessia separately from daycare hour passes.
- Link one paid expense to one or several care stays without duplicating money.
- Plan future care, create linked ledger forecasts for agreed payment dates and distinguish forecast money from actual payments.
- Record Alessia's late-cancellation fee and show a provider-wide paid/outstanding balance.

## Capabilities

### New Capabilities

- `zeus-hour-passes`: User-owned cumulative hour passes, dated usage, expense linking and a responsive management page.

### Modified Capabilities

None.

## Impact

Backend authenticated endpoints, additive database tables and ledger edit guards. Frontend navigation, a Zeus page, care forms and API client. No new dependencies, tests, live records, migration execution, commits or pushes without approval.
