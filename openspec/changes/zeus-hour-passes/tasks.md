## 1. Backend

- [x] 1.1 Add pass and attendance persistence with an additive migration.
- [x] 1.2 Implement authenticated FIFO hour accounting, corrections and validation.
- [x] 1.3 Link or atomically create purchase expenses and protect linked ledger entries.

## 2. Frontend

- [x] 2.1 Add Zeus navigation, remaining-hours summary, pass balances and daily history.
- [x] 2.2 Add purchase and attendance forms with expense selection and correction controls.

## 3. Validation and approval

- [x] 3.1 Compile both projects, lint changed frontend files and inspect the generated migration without running tests.
- [x] 3.2 Capture desktop/mobile previews and show the result before requesting publication approval.
- [x] 3.3 Obtain explicit approval before commit, push or live migration execution.

The migration creates only ZeusHourPasses and ZeusHourUsages, their indexes and the expense foreign key. The user explicitly approved both commits, push and automatic creation of these two tables during deployment. No live purchase or attendance records are included. Desktop, mobile, empty-state and form previews use synthetic data only. Both builds and changed-file ESLint pass. Full-project TypeScript still reports existing errors in TransactionModal, shared UI aliases and ParticipantsList; no diagnostics target the changed files.

## 4. Entry and exit times

- [x] 4.1 Add nullable attendance times and server-calculated minutes, preserving duration-only records and rejecting overlapping ranges.
- [x] 4.2 Replace duration fields with date, entry and exit inputs, preview the deducted time and show the range in history.
- [x] 4.3 Build and lint without tests, inspect the additive migration and capture the revised form.
- [x] 4.4 Obtain separate approval for the follow-up commits, push and nullable time-column migration.

## 5. Prices, nights and nanny care

- [x] 5.1 Add separate care-stay persistence and authenticated correction/payment endpoints with shared-expense allocation guards.
- [x] 5.2 Preload all undiscounted daycare packages and add night/Alessia forms, pricing and independent histories.
- [x] 5.3 Support one payment for multiple stays without duplicating expenses or consuming hour passes.
- [x] 5.4 Build, lint, inspect additive migrations and show responsive previews without tests or live writes.
- [x] 5.5 Obtain approval for the complete follow-up files, commits, push and schema changes before publication.

The user explicitly approved the listed backend/frontend commits, push and deployment with both follow-up migrations: nullable attendance times and an empty ZeusCareStays table. No example data or financial transactions are included.
