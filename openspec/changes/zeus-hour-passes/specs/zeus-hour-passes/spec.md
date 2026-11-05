## ADDED Requirements

### Requirement: Cumulative hour passes
The system SHALL store user-owned passes with purchased minutes, purchase date and a unique paid expense link. Passes SHALL never expire.

#### Scenario: Another pass is purchased
- **WHEN** a 120-hour pass is added after a 60-hour pass
- **THEN** available minutes equal all purchased minutes less recorded usage.

### Requirement: Dated FIFO attendance
The system SHALL record attendance by civil date and positive integer minutes, consuming the oldest eligible pass first.

#### Scenario: Attendance spans passes
- **WHEN** attendance exceeds the oldest pass's remaining hours
- **THEN** the remainder is deducted from the next pass purchased on or before that date.

#### Scenario: Insufficient historical balance
- **WHEN** new or edited attendance exceeds the hours available on any attendance date
- **THEN** the entire mutation is rejected without persisting changes.

### Requirement: Financial integrity
The system SHALL link an existing owned paid outgoing expense or atomically create and link an explicitly requested purchase expense.

#### Scenario: Existing imported purchase
- **WHEN** a paid expense is selected
- **THEN** no new financial entry is created and its actual amount and value date define the pass price and purchase date.

#### Scenario: Invalid financial source
- **WHEN** a source belongs to another user, is unpaid, is a transfer, is settlement-allocated, or is already linked to a pass
- **THEN** the request is rejected.

#### Scenario: Linked ledger edits
- **WHEN** a linked expense is edited, unconfirmed, skipped or deleted
- **THEN** the request is rejected until the unused pass is removed.

### Requirement: Corrections and isolation
The system SHALL permit editing or removing attendance and removing unused passes, enforce user ownership on every request and preserve purchase expenses.

#### Scenario: Remove an unused pass
- **WHEN** the user explicitly confirms removal of a pass with no consumed minutes
- **THEN** the pass is removed but its ledger expense remains unchanged.

#### Scenario: Consumed pass removal
- **WHEN** a pass with consumed minutes is removed
- **THEN** the request is rejected with a correction instruction.

### Requirement: Simple responsive Zeus page
The frontend SHALL show available hours, pass balances, daily history and purchase expense links with accessible forms and explicit errors.

#### Scenario: Quick purchase and usage
- **WHEN** a user opens Zeus
- **THEN** they can choose 60, 120 or custom hours, link a purchase expense and record hours and minutes without financial duplication.
