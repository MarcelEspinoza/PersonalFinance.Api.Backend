## ADDED Requirements

### Requirement: Match the signed settlement net
The system SHALL automatically link a paid bank movement only to an unambiguous matching person and open settlement with the exact signed outstanding amount.

#### Scenario: Positive or negative net
- **WHEN** a bank movement matches the signed net and counterparty
- **THEN** it pays that single settlement without consuming a parallel fixed forecast.

#### Scenario: Mismatch or ambiguity
- **WHEN** the amount differs or more than one settlement matches
- **THEN** no settlement is automatically paid or given an excess allocation.

### Requirement: Settle included amounts without additional cash movements
The system SHALL reserve pending ledger amounts included in a settlement and mark their included share as settled when the settlement is paid, without adding bank movements.

#### Scenario: Partial inclusion
- **WHEN** 50 of a pending 100 is included and the settlement is paid
- **THEN** 50 is settled through the settlement and 50 remains pending.

#### Scenario: Already-paid source
- **WHEN** a settlement contains a previously paid bank movement
- **THEN** paying the settlement does not change or count that source movement again.

### Requirement: Explicit bank payment selection
The system SHALL permit selecting an owned paid bank movement as a settlement payment, prevent duplicate use and handle either direction.

#### Scenario: Manual exact payment
- **WHEN** the user links a paid movement covering the remaining signed net
- **THEN** the settlement and its included pending amounts become settled.

### Requirement: Approval-gated cleanup
The system SHALL NOT modify legacy rules or occurrences without approval of their exact inventory.

#### Scenario: Preparing cleanup
- **WHEN** the correction is implemented
- **THEN** the two legacy rules and unpaid occurrences are proposed separately from preserved paid history.

### Requirement: Repair existing manual line links without changing the net
The system SHALL allow an explicit source attachment to an existing settlement line, including a closed settlement, without changing financial line values, settlement status or bank cash.

#### Scenario: Passthrough obligation already included
- **WHEN** an owned pending passthrough outgoing entry is attached to an existing charge in a closed settlement
- **THEN** its included share is settled without adding a deduction, a bank movement or a skipped entry.

#### Scenario: Partial attachment or removal
- **WHEN** only part of an entry is attached, or an attachment is removed
- **THEN** coverage is recomputed and the remaining unallocated share stays available.

#### Scenario: Invalid source
- **WHEN** the source belongs to another user, has insufficient unallocated amount, is already used in that settlement, is skipped, belongs to a closed ledger month or is an own-account mirrored transfer
- **THEN** the attachment is rejected without persisting changes.

### Requirement: Assistant-assisted link repair
The assistant SHALL see settlement lines and ledger source identifiers and propose a dedicated, server-validated link action requiring user confirmation.

#### Scenario: Existing line instead of duplicate amounts
- **WHEN** a user asks to compensate an obligation already represented by a manual line
- **THEN** the assistant proposes attaching that source to the existing line, not creating another financial line or discarding the obligation.

### Requirement: Distinguish cash timing from variable budget reserves
The dashboard action center SHALL calculate payment shortfall warnings and transfer suggestions without variable budget reserves, while retaining those reserves in month-end free money and explaining them separately.

#### Scenario: Budget is not an immediate payment
- **WHEN** a remaining monthly budget would make an account negative if spent today
- **THEN** it does not create an immediate cash-payment shortfall warning.

#### Scenario: Account minima occur on different dates
- **WHEN** accounts have negative minima at different times but their combined daily balance remains nonnegative
- **THEN** uncovered shortfall is zero rather than the sum of those individual minima.

#### Scenario: Incorrect dates or unpaid estimates
- **WHEN** a cash-payment warning is displayed
- **THEN** it is conditional on the forecast amounts and dates and asks the user to verify payment status and timing.
