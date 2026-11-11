## ADDED Requirements

### Requirement: Pending sources are visible to the import assistant
The assistant SHALL receive relevant open incoming and outgoing forecasts, active financial reminders and open settlements, with real identifiers. It MUST NOT infer nonexistence from a truncated list or classify a canceled loan as open.

#### Scenario: Carlos reminder outside the forecast list
- **WHEN** the user identifies a receipt as payment of the active Carlos reminder
- **THEN** the assistant can propose a card using that reminder's identifier without requesting a duplicate forecast

### Requirement: Clear additional matches are automatic and ambiguous matches are not
The importer SHALL recognize an outstanding exact signed amount with a unique meaningful bank identifier even when its forecast account is unassigned or its expected date differs. It MUST NOT automatically select among ambiguous sources or match a generic Bizum to a forecast solely by amount.

#### Scenario: Generic Bizum against a unique reminder
- **WHEN** an uploaded receipt has generic bank text and exactly one open reminder in the same direction is covered by its amount (shortfall of 0.05 or less), with no open forecast of the same amount
- **THEN** the upload stores that reminder link on the draft row, the review shows it with an unlink control, and the user can remove it from the review or the assistant

#### Scenario: Bank text names someone else
- **WHEN** the bank text contains a meaningful identifier that does not appear in the reminder title
- **THEN** the reminder is not linked automatically

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

### Requirement: Ordered and searchable import categories
Import category selectors SHALL display stable type/name ordering using Spanish alphabetical comparison and provide local case- and accent-insensitive search. Groups, individual rows, splits and shared expenses SHALL retain their existing category restrictions, selected values, clearing rules, required validation and disabled states.

#### Scenario: Category is found without accents
- **WHEN** the user searches for a category using a different letter case or omitting accents
- **THEN** matching categories appear in the same order and selection uses the existing category identifier without an AI request

#### Scenario: Search is canceled or has no matches
- **WHEN** the user closes the search without choosing or the search matches no category
- **THEN** the current category is unchanged and an empty result is explained visibly

#### Scenario: Required or disabled category
- **WHEN** a split or shared expense requires a category, or an import is busy or applied
- **THEN** native required validation and existing category filters remain effective, and disabled selectors cannot change their selection
