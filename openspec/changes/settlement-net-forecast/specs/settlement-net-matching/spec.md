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
