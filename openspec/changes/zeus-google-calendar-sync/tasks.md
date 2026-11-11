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
