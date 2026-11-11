## 1. Persistent obligation links

- [x] 1.1 Add draft reminder/settlement link fields, DTOs and an additive migration.
- [x] 1.2 Implement authenticated link preparation, conflict checks and apply-time validation.
- [x] 1.3 Reuse reminder completion and settlement payment rules in the import's final save.

## 2. Pending reconciliation and assistant

- [x] 2.1 Make additional exact, identifiable forecast matches available across dates and unassigned accounts; leave ambiguity for confirmation.
- [x] 2.2 Include relevant active reminders and open settlements in ranked assistant context.
- [x] 2.3 Validate reminder/settlement proposals and report rejected or malformed actions explicitly.

## 3. Import interface

- [x] 3.1 Add confirmation cards, persistent link feedback and unlink controls for obligations.
- [x] 3.2 Display proposal validation feedback and candidate-loading errors without another AI request.

## 4. Local validation

- [x] 4.1 Generate and inspect the migration without applying it to a database.
- [x] 4.2 Build backend and frontend without writing or running tests.
- [x] 4.3 Preview affected UI locally with synthetic data, inspect focused diffs and confirm no live changes or deployment.

## Validation notes

- Backend and frontend builds passed. Changed frontend files passed ESLint and a focused TypeScript check using the existing Vite path alias.
- The repository-wide frontend type check remains blocked by pre-existing errors in `TransactionModal.tsx` and missing TypeScript alias configuration for the shared UI components. Those unrelated files were not changed.
- EF reports no pending model changes after the additive migration. No database migration was applied.
- Desktop/mobile previews used synthetic responses with every API request intercepted locally. Both obligation cards prepared the expected links; the row view displayed both links and their removal controls. No browser errors occurred.
- No tests were written or run. No production records, paid AI calls, deployment, commits or pushes were performed.

## 5. Ordered and searchable category selection

- [x] 5.1 Add a shared, Spanish-sorted import category selector with local accent-insensitive search, preserving native selection behavior.
- [x] 5.2 Reuse the selector for groups, rows, splits and shared expenses while retaining validation and category restrictions.
- [x] 5.3 Build/type-check/lint the scoped change and preview synthetic local interactions without writing or running tests.
- [x] 5.4 Present exact commit messages/files and obtain approval before publication.

### Category picker validation

- The frontend build and scoped ESLint passed. The repository-wide type check reported only the six known baseline diagnostics described above.
- Synthetic desktop/mobile previews confirmed Spanish sorting, accent-insensitive and multi-token search, cancellation with focus restoration, required/disabled behavior, unchanged-selection handling and nested shared-expense dialogs.
- No mobile horizontal overflow, browser errors or external requests occurred. No tests were written or run, and no live imports, balances, AI requests or Google connections were changed.
