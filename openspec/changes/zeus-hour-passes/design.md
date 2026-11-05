## Context

The app uses ASP.NET Core, EF Core/MySQL and a React/Tailwind frontend. The ledger is the source of financial truth. The user confirmed that passes accumulate without expiry and purchases must be linked to expenses. PersonalFinance must not write or run tests.

## Goals / Non-Goals

**Goals:** A user-owned Zeus section with cumulative hours, minute-accurate attendance, FIFO pass consumption, expense links and clear correction controls.

**Non-Goals:** Pet profiles, bookings, expiry, automatic payments, AI actions, recurring attendance or changes to existing live financial data.

## Decisions

- Store passes and attendance as two tables. Use integer minutes instead of decimal hours to avoid rounding errors. Derive FIFO consumption by purchase date, creation time and ID; usage on a date cannot consume a later purchase.
- Use serializable transactions for mutations and validate the entire dated timeline to prevent concurrent or backdated overdraw. Derive allocations rather than persist a third allocation table.
- A pass requires exactly one existing paid, outgoing, non-transfer, unallocated expense or explicit creation of one. Reuse the ledger creation command inside the transaction so a failure cannot leave an orphan expense. Purchase date follows the expense value date.
- Keep each expense linked to at most one pass. Protect linked ledger entries from editing, unconfirmation and deletion until an unused pass is removed. Deleting a pass preserves its financial expense. Consumed passes cannot be deleted; attendance can be edited or removed first.
- Return linked expense details and allocation details to the UI. Reuse existing error, dialog, button, civil-date and currency helpers. Place Zeus in desktop/mobile navigation.

## Risks / Trade-offs

- Duplicate expenses: existing-expense selection is the default; creation is explicit and atomic.
- Historical hour changes: validate all dated usage after each write and reject insufficient balances without persisting.
- Concurrent updates: serializable database transactions and a unique expense index; no silent fallback.
- Incorrect purchase entry: unused passes can be removed without deleting bank history and then registered correctly.

## Migration Plan

Generate and inspect an additive migration for the two tables and foreign keys. Build and preview locally without starting the API against production. Present screenshots, migration and exact files before requesting commit/push approval. Do not apply a migration or create live pass data without approval.
