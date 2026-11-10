## ADDED Requirements

### Requirement: Pending sources are visible to the import assistant
The assistant SHALL receive relevant open incoming and outgoing forecasts, active financial reminders and open settlements, with real identifiers. It MUST NOT infer nonexistence from a truncated list or classify a canceled loan as open.

#### Scenario: Carlos reminder outside the forecast list
- **WHEN** the user identifies a receipt as payment of the active Carlos reminder
- **THEN** the assistant can propose a card using that reminder's identifier without requesting a duplicate forecast

### Requirement: Clear additional matches are automatic and ambiguous matches are not
The importer SHALL recognize an outstanding exact signed amount with a unique meaningful bank identifier even when its forecast account is unassigned or its expected date differs. It MUST NOT automatically select among ambiguous sources or match a generic Bizum solely by amount.

#### Scenario: Refund against an unassigned forecast
- **WHEN** a Trainline receipt uniquely identifies an open Trainline forecast for the same amount
- **THEN** the importer consumes that forecast rather than keeping it outstanding beside a new receipt

#### Scenario: Two indistinguishable sources
- **WHEN** a movement has equally plausible pending targets
- **THEN** the review requires explicit selection rather than choosing an arbitrary target

#### Scenario: Partial movement near a recurring forecast
- **WHEN** a nearby movement covers only part of an outstanding forecast
- **THEN** the importer does not automatically mark the entire forecast paid, and an explicit link is required to reduce its remaining amount

### Requirement: Confirmed obligation links are applied once
The system SHALL persist a confirmed reminder or settlement link in the draft and settle it only when the matching bank row is applied. It MUST revalidate ownership, availability, direction and total linked amount, and MUST NOT also match another pending source automatically.

#### Scenario: Confirmed reminder receipt
- **WHEN** the user confirms a 30.40 receipt against the matching 30.40 income reminder and applies the import
- **THEN** one bank movement is recorded and the reminder is completed without a second income or forecast

#### Scenario: Partial one-off payment
- **WHEN** a confirmed payment covers only part of a one-off reminder
- **THEN** the remainder stays outstanding and only the actual bank amount is booked

#### Scenario: Small shortfall is written off
- **WHEN** a confirmed 30.40 receipt is linked to a 30.42 reminder (shortfall of 0.05 or less)
- **THEN** the reminder is completed, the shortfall is treated as a loss, and only the actual bank amount is booked

#### Scenario: Changed repeating reminder
- **WHEN** the reminder's due date changed after its link was confirmed
- **THEN** the import fails visibly rather than consuming another occurrence

#### Scenario: Settlement without an automatic bank-name match
- **WHEN** the user explicitly links a receipt or payment to their own open settlement
- **THEN** applying the import records the payment against that settlement once
