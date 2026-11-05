## Why

Independent recurring income and expense forecasts duplicate the single net amount of a family settlement. Bank imports can consume an unrelated fixed forecast instead of settling the actual amount owed.

## What Changes

- Prefer the outstanding signed settlement net when importing a matching bank transfer.
- Keep settlement forecasting as the single source of the amount and direction.
- Match an exact signed net automatically; allow an explicit bank-payment link.
- Settle included pending amounts without creating additional bank transactions; retain any unincluded share as pending.
- Preserve unrelated recurring forecasts and already-paid movements.
- Prepare an explicit, read-only inventory for retiring the two legacy rules and their unpaid occurrences; execute no data cleanup without approval.

## Capabilities

### New Capabilities

- `settlement-net-matching`: Settlement-aware bank matching that takes precedence over fixed forecasts.

### Modified Capabilities

None.

## Impact

Backend settlement payment linking and bank import forecast matching. No new dependencies, automatic data migrations, tests, commits, or pushes. Validate through compilation and review.
