## Why

The loan catalogue and current settlement forecasts are separate, making it difficult to understand who is owed money, which payments remain, and when repayment is expected to finish. The dashboard also repeats an action list already covered by reminders.

## What Changes

- Remove the dashboard action centre and its dashboard-only reminder request, preserving reminders elsewhere.
- Add reversible archiving for existing loan records without deleting their history, changing balances, marking them paid, or cancelling scheduled cash flows.
- Replace the loan-only presentation with a detailed debts and collections view, separating amounts to pay, amounts to collect, pass-through money, and archived records.
- Show source, person or lender, outstanding amount, known payment dates, payment account, and history where recorded; identify missing information explicitly.
- Add a compact dashboard repayment outlook. Show a completion estimate only when the underlying repayment information is sufficient, never by assuming the current projection horizon is the end of a debt.
- Preserve the existing forecast and settlement calculations and avoid counting linked records twice.
- Apply the already-authorized archival operation only after the new behavior is available, using an explicit snapshot of existing owned records. New loans must not be archived automatically.

## Capabilities

### New Capabilities

- `debt-and-collection-overview`: Non-destructive loan archiving, detailed debt and collection visibility, a truthful repayment outlook, and removal of the duplicate dashboard action centre.

### Modified Capabilities

None.

## Impact

- Frontend dashboard, loan page, loan forms, and relevant API client types.
- Backend loan persistence and owned archival operations, debt overview queries, and a nullable/additive migration.
- Existing settlement and ledger references remain authoritative; incomplete source associations require explicit review rather than name-based matching.
- No new dependencies, automatic payments, financial balance corrections, tests, or publication without approval.
