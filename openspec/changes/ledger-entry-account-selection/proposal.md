## Why

The manual entry editor does not expose an account, forcing users to confirm an expected payment or receipt just to choose one. Forecasts must be assignable to an account without changing their payment status.

## What Changes

- Add account selection when creating and editing manual ledger entries.
- Preserve existing account assignments unless the user changes them, including accounts no longer in the active list.
- Surface account-loading failures with a retry action rather than hiding the selector.
- Keep amounts, confirmation state and existing account-update API semantics unchanged.

## Capabilities

### New Capabilities

- `ledger-entry-account-selection`: Choose an account for a manual forecast independently of payment confirmation.

### Modified Capabilities

None.

## Impact

- Frontend `EntryDialog` and its `LedgerMonthPage` submit payloads.
- No backend runtime changes, migrations, bulk financial updates or new dependencies.
- Validation uses builds, lint and synthetic previews only; no tests are written or run.
