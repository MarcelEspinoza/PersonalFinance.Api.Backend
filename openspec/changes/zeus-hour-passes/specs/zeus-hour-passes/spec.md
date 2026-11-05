## ADDED Requirements

### Requirement: Cumulative hour passes
The system SHALL store user-owned passes with purchased minutes, purchase date and a unique paid expense link. Passes SHALL never expire.

#### Scenario: Another pass is purchased
- **WHEN** a 120-hour pass is added after a 60-hour pass
- **THEN** available minutes equal all purchased minutes less recorded usage.

### Requirement: Dated FIFO attendance
The system SHALL record attendance by civil date and same-day entry and exit times, computing positive integer minutes on the server and consuming the oldest eligible pass first.

#### Scenario: Automatic duration
- **WHEN** a user records 09:00 to 13:30 on one day
- **THEN** the system deducts 270 minutes and displays both the time range and duration.

#### Scenario: Invalid or overlapping hours
- **WHEN** a range is incomplete, ends at or before its start, contains sub-minute precision, or overlaps another timed record that day
- **THEN** the mutation is rejected without deducting hours.

#### Scenario: Duration-only history
- **WHEN** an existing record has no entry or exit time
- **THEN** its duration remains available, and correcting it through the new form requires an explicit time range.

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

### Requirement: Brochure daytime prices
The frontend SHALL offer daycare packages of 1/5/10/20 days at six hours per day for 13/55/100/160 euros and twelve hours per day for 21/95/180/320 euros, without the optional discount. Selecting a package SHALL prefill total hours and the price of an explicitly created expense. Existing expenses SHALL retain their actual price.

### Requirement: Independent care providers and nights
The system SHALL record daycare nights and Alessia nanny care without changing daycare hour balances.

#### Scenario: Alessia daytime price
- **WHEN** daytime care lasts less than or more than four hours
- **THEN** the form suggests 20 or 25 euros respectively, while exactly four hours requires an explicit choice.

#### Scenario: Overnight care
- **WHEN** a user records a night stay
- **THEN** its provider, first night, number of nights, agreed price and optional note are preserved separately from hour passes.

#### Scenario: Unknown night-package portability
- **WHEN** separate stays are recorded
- **THEN** no shared night balance or cross-stay package discount is inferred.

### Requirement: Shared care payments
The system SHALL allow a paid owned expense or an explicitly created expense to cover one or several pending stays for the same provider, up to its actual amount.

#### Scenario: One transfer covers several visits
- **WHEN** multiple pending Alessia stays are linked to one paid expense
- **THEN** each stay is marked paid for its agreed price, the expense is not duplicated and any unallocated amount remains explicit.

#### Scenario: Unsafe or excessive allocation
- **WHEN** a payment belongs to another user, is invalid, is already used by an hour pass, covers another provider, or has insufficient unallocated money
- **THEN** the entire operation is rejected without changing care or financial records.

#### Scenario: Correcting paid care
- **WHEN** the user unlinks a payment or removes a care record
- **THEN** the financial expense remains intact, and linked ledger entries cannot be edited until all care links are removed.

### Requirement: Agreed future care and forecast payments
The system SHALL accept future independent care dates and allow an explicit linked ledger forecast due on the agreed payment date. A forecast SHALL not be reported as paid or reduce the current bank balance.

#### Scenario: Agreed nanny visit
- **WHEN** a user records a future Alessia visit and creates its payment forecast
- **THEN** the ledger contains one planned outgoing expense with the agreed amount, account and due date, and Zeus shows it as forecast rather than paid.

#### Scenario: Confirm the forecast
- **WHEN** a linked forecast is confirmed from Zeus or Mes for an amount covering all its care allocations
- **THEN** the existing expense and care links are retained and the provider balance changes from outstanding to paid without another expense.

#### Scenario: Link an imported paid movement
- **WHEN** a paid movement is linked to care that already has a forecast
- **THEN** the operation reduces or discards the old forecast atomically and links the actual payment without double counting.

#### Scenario: Cancel or remove forecast care
- **WHEN** care is unlinked or removed from a forecast
- **THEN** the forecast is reduced by the removed allocation or discarded when no linked stays remain; actual paid expenses remain unchanged.

### Requirement: Late cancellation fee
The system SHALL offer an Alessia-only cancellation record with a suggested price of 10 euros, no attended hours or nights, and the same forecast or actual-payment options as other independent care.

### Requirement: Provider-wide payment overview
The frontend SHALL show all-time paid and outstanding care amounts and the forecast portion, irrespective of the selected history month, and distinguish future care and forecasts from completed paid care.

#### Scenario: Prior-month unpaid care
- **WHEN** a user views a different month while an older care charge remains unpaid
- **THEN** the unpaid charge remains included in the outstanding total and accessible for payment.
