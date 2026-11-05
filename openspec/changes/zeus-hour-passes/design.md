## Context

The app uses ASP.NET Core, EF Core/MySQL and a React/Tailwind frontend. The ledger is the source of financial truth. The user confirmed that passes accumulate without expiry and purchases must be linked to expenses. PersonalFinance must not write or run tests.

## Goals / Non-Goals

**Goals:** A user-owned Zeus section with cumulative hours, minute-accurate attendance, FIFO pass consumption, expense links and clear correction controls.

**Non-Goals:** Pet profiles, external bookings, expiry, automatic payments, AI actions, recurring attendance or changes to existing live financial data.

## Decisions

- Store passes and attendance as two tables. Use integer minutes instead of decimal hours to avoid rounding errors. Derive FIFO consumption by purchase date, creation time and ID; usage on a date cannot consume a later purchase.
- Use serializable transactions for mutations and validate the entire dated timeline to prevent concurrent or backdated overdraw. Derive allocations rather than persist a third allocation table.
- A pass requires exactly one existing paid, outgoing, non-transfer, unallocated expense or explicit creation of one. Reuse the ledger creation command inside the transaction so a failure cannot leave an orphan expense. Purchase date follows the expense value date.
- Keep each expense linked to at most one pass. Protect linked ledger entries from editing, unconfirmation and deletion until an unused pass is removed. Deleting a pass preserves its financial expense. Consumed passes cannot be deleted; attendance can be edited or removed first.
- Return linked expense details and allocation details to the UI. Reuse existing error, dialog, button, civil-date and currency helpers. Place Zeus in desktop/mobile navigation.
- Record attendance through same-day entry and exit times, computing minute precision on the server and previewing the duration in the form. Reject overlapping time ranges on the same day. Nullable time columns preserve existing duration-only records and legacy clients; no attendance is fabricated during migration.
- Offer all eight daytime brochure packages with their undiscounted prices. The user confirmed that actual attended hours are deducted and unused hours accumulate, regardless of the package's advertised daily hours.
- Store independent care stays for daycare nights and Alessia, Zeus's nanny. These never consume hour passes. Daycare night prices are 28/52/75/96/115 euros for one through five nights and 21 euros per night from six nights. Alessia costs 20 euros below four hours, 25 above four hours, and 30 per night. Exactly four hours requires an explicit 20/25 choice. Store the agreed price; allow explicit adjustment for actual charges.
- Do not assume that night packages accumulate across separate visits. Record each stay and its number of nights. Night-package portability remains unknown.
- Allow pending care stays to share a paid expense for the same provider, allocating each stay's agreed price. Reject allocations above the actual expense, reuse of pass expenses and mixing providers. Show allocated and remaining payment amounts; do not treat unallocated money as paid care. A new expense is explicit and atomic. Unlinking or deleting care preserves the expense. Linked care must be unlinked before editing.
- Allow future independent care records, while pass attendance still requires today or earlier. Add an Alessia-only cancellation kind with a suggested 10-euro fee, no hours or nights, and no attendance-overlap impact.
- Persist the domain/API kind `cancellation` as `cancel` through a bidirectional EF conversion so cancellation records fit the existing ten-character database column without changing the public contract or schema.
- Reuse the care expense link for either a paid expense or an explicitly created forecast. Create forecasts through the existing ledger command without ActualAmount or ValueDate, due on the selected payment date, for exactly the selected stays' agreed total. No schema change or separate reminder duplicates the forecast.
- Derive care payment state from the linked ledger status, never from link existence. Confirming a linked forecast through the normal ledger command preserves its ID and care links and requires an actual payment covering all linked stays. Prevent all other direct edits while linked.
- When replacing a forecast with a paid bank expense, unlinking it or deleting a care record, atomically reduce its remaining care allocation and discard the forecast if no care stays remain. Paid expenses are never changed. Closed-period or settlement-linked forecasts must not be changed silently.
- Show provider-wide paid, outstanding and forecast amounts independent of month filtering. Outstanding includes forecasts but does not count them twice; upcoming care is visible separately. A zero outstanding balance shows "Todo pagado" only when care records exist.

## Risks / Trade-offs

- Duplicate expenses: existing-expense selection is the default; creation is explicit and atomic.
- Historical hour changes: validate all dated usage after each write and reject insufficient balances without persisting.
- Concurrent updates: serializable database transactions and a unique expense index; no silent fallback.
- Incorrect purchase entry: unused passes can be removed without deleting bank history and then registered correctly.

## Migration Plan

Generate and inspect an additive migration for the two tables and foreign keys. Build and preview locally without starting the API against production. Present screenshots, migration and exact files before requesting commit/push approval. Do not apply a migration or create live pass data without approval.
