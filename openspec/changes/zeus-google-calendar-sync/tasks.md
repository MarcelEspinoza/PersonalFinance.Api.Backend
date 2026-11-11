## 1. Secure connection and persistence

- [x] 1.1 Add persisted Google authorization, connection and event synchronization models with an additive EF migration.
- [x] 1.2 Implement configured OAuth PKCE, one-use state, encrypted tokens and authenticated connection/status/disconnection APIs.

## 2. Source synchronization

- [x] 2.1 Project all Zeus usages and pasanaco recipients into calendar events, using day 5 for deliveries.
- [x] 2.2 Implement durable reconciliation, ownership checks, leased background processing and retry/reconnect status.
- [x] 2.3 Synchronize reminder create/edit/complete/delete to the dedicated PersonalFinance Tasks list with uncertain-create recovery.

## 3. User interface and setup

- [x] 3.1 Add shared Google connection/status/retry/disconnection controls to Zeus, Pasanaco and Reminders.
- [x] 3.2 Document Google OAuth and Railway configuration without committing credentials.

## 4. Validation and delivery

- [x] 4.1 Compile the backend and dependent project without running or writing tests.
- [x] 4.2 Build/type-check the frontend and lint touched files, then review the scoped diff.
- [x] 4.3 Obtain explicit commit/push approval and identify the remaining Google-account setup required for live verification.

Backend/dependent compilation and the frontend production build passed. The generated migration matches the EF model and only adds the three Google integration tables. New Google integration files and the other touched pages passed focused lint.

Global frontend type-checking is blocked by six existing errors: TransactionModal account nullability and missing `@/lib/utils` alias declarations. PasanacoPage has two existing explicit-any lint errors and two existing warnings outside this change. The same diagnostics were reproduced against committed HEAD sources without modifying the worktree.

Live Google synchronization remains unverified until the user supplies OAuth configuration and authorizes an account. No tests were written or run.

## 5. Global controls, synchronization state and event colors

- [x] 5.1 Persist validated per-user Zeus/pasanaco color preferences with an additive migration and reconcile existing event colors.
- [x] 5.2 Share Google state across the layout, move normal connection controls to the global header, and show disabled Synchronized/in-progress states with explicit error retry.
- [x] 5.3 Add source-specific color controls, compile/build/lint the changed code without tests, and inspect generated migration SQL.
- [x] 5.4 Present exact commit messages and files and obtain publication approval.

The user reports that Google is now connected and events synchronize. The new status/color behavior still requires deployment verification after explicit publication approval.

Backend/dependent compilation, frontend production build, focused lint, EF model consistency and OpenSpec validation passed. Generated SQL only adds two integer color columns with defaults 3 and 10; the token TEXT mapping is unchanged. Frontend type-checking still reports the same six pre-existing errors documented above, with no new diagnostics.

## 6. Settings-only Google preferences

- [x] 6.1 Move Google explanations and event color preferences to Settings > Google, keeping only compact sync status/action on source pages.
- [x] 6.2 Restore the Google settings tab after OAuth using a fixed allowlisted return path and preserve the global account control.
- [x] 6.3 Compile/build/lint the scoped change without tests and review OAuth query preservation.
- [x] 6.4 Obtain approval for exact commit messages/files before publication.

Backend/dependent compilation, the frontend production build and focused lint passed for the relocation. The existing type-check and Pasanaco lint diagnostics above remain unchanged. No migration was added and no tests were written or run.
