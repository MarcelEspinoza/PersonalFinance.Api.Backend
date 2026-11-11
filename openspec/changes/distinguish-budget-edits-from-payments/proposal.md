## Why

The expense plan presents budget edits and independent recurring payments as similar actions. Users can mistake a monthly receipt for a total spending budget, causing the receipt to be counted alongside existing purchases.

## What Changes

- Label budget editing separately from adding or editing a fixed payment.
- Explain that a budget includes existing spending, whereas a recurring receipt is an independent payment.
- Require explicit confirmation before converting an expense budget into a recurring payment, including through the add-payment dialog.
- Preserve the existing budget replacement, receipt update and independent-payment calculations.

## Capabilities

### New Capabilities

- `planning-edit-safety`: Distinguish total-budget edits from independent fixed payments and guard category conversions.

### Modified Capabilities

None.

## Impact

- Frontend expense-plan row actions and category dialog.
- No backend runtime changes, dependencies, migrations or changes to financial calculations.
- Validation uses builds, lint and synthetic browser previews only; no tests are written or run.
