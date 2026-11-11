## ADDED Requirements

### Requirement: Explicit split targets persist without booking money
An ordinary split part SHALL optionally reference one open forecast or one open settlement. Saving, reopening and canceling review MUST NOT book money, close a pending source or discard a selected link silently.

#### Scenario: Mixed receipt
- **WHEN** a bank receipt is divided into a loan-receipt part linked to a forecast and a reimbursement part linked to a settlement
- **THEN** both independent links persist, and review clearly distinguishes linked and category-only parts

#### Scenario: Target loading fails
- **WHEN** selectable pending sources cannot be loaded
- **THEN** an actionable error is visible and saving is blocked until sources load successfully

### Requirement: Apply reconciles only each linked part atomically
Applying a linked split SHALL book the sum of its parts once, deduct only each forecast-linked amount and record only each settlement-linked amount as payment. A failed operation MUST roll back all changes, and duplicate or reapplied bank rows MUST NOT settle sources again.

#### Scenario: Loan and settlement paid together
- **WHEN** a receipt contains 100 for an open loan-receipt forecast and 42 for a settlement
- **THEN** the account receives 142, the 100 forecast no longer remains pending, and the settlement receives only 42

#### Scenario: Invalidated target
- **WHEN** a target is foreign, unavailable, closed, direction-incompatible, category-incompatible or overallocated across batch rows
- **THEN** the operation fails visibly without booking the other part

### Requirement: Existing split behavior remains available
Category-only and shared-expense splits SHALL retain their current booking behavior and restrictions. Variable budgets MUST NOT become fake pending entries to enable selection.

#### Scenario: Unlinked split
- **WHEN** a user leaves both parts without a target
- **THEN** only their categories and amounts are booked, with no pending-source payment

### Requirement: Pending-source selection is searchable
The split editor and existing forecast picker SHALL provide local case- and accent-insensitive search without changing existing selections or reconciliation restrictions.

#### Scenario: Search while splitting a forecast payment
- **WHEN** a user searches and then clears the search after selecting a forecast allocation
- **THEN** the allocation and its amount remain unchanged

### Requirement: Import confirmations expire
Transient import success messages SHALL disappear after three seconds and clean up their timers on replacement or unmount. Errors and actionable warnings MUST remain visible.

#### Scenario: Saved split confirmation
- **WHEN** a split is saved successfully
- **THEN** its confirmation disappears after three seconds without dismissing an unrelated error
