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
