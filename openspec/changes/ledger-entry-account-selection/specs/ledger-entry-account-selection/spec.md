## ADDED Requirements

### Requirement: Independent manual account selection

The manual entry editor SHALL expose active accounts for income and expense creation and editing. Selecting an account MUST NOT confirm a payment, change actual amounts or create another entry.

#### Scenario: Create an assigned forecast

- **WHEN** a user selects an account while creating a forecast
- **THEN** creation includes that account ID without actual payment fields.

#### Scenario: Assign an existing unassigned forecast

- **WHEN** a user edits an unassigned entry and chooses an account
- **THEN** the existing entry update includes the selected account ID without payment confirmation.

#### Scenario: Preserve an existing assignment

- **WHEN** a user edits other fields without changing the account
- **THEN** the current assignment remains selected and the update omits account changes, including when the account is inactive.

#### Scenario: Preserve a new unassigned entry

- **WHEN** a user intentionally leaves a new forecast without an account
- **THEN** it remains unassigned.

### Requirement: Visible account-loading failures

The editor SHALL report account-loading errors and allow retry without discarding entered values. Submission MUST be unavailable until the account list loads successfully.

#### Scenario: Retry account loading

- **WHEN** account loading fails and the user retries
- **THEN** the error is visible, entered values are retained and saving becomes available after a successful load.
