## ADDED Requirements

### Requirement: Loan archiving preserves financial history
The system SHALL support reversible archival of explicitly selected owned loan records. Archival MUST preserve outstanding and principal amounts, original status, payments, dates, and references. It MUST NOT book money, forgive a balance, or cancel independent forecasts.

#### Scenario: Archive the existing catalogue
- **WHEN** the authorized existing loan identifiers are archived
- **THEN** they appear in historical records, leave active follow-up, and retain all financial values

#### Scenario: Invalid or foreign identifier
- **WHEN** an archive request contains an unavailable or foreign loan
- **THEN** it fails without partially archiving the remaining records

#### Scenario: Repeated archive or restore
- **WHEN** an already archived loan is archived again or an archived loan is restored
- **THEN** the operation is idempotent or clears only the archival state respectively

### Requirement: Current obligations retain their provenance
The overview SHALL separate amounts to pay, amounts to collect, and archived records. It SHALL expose the recorded source, party or lender, amount, dates, available payment details, and history without guessing missing information.

#### Scenario: Loan linked to a settlement
- **WHEN** a settlement also references a loan
- **THEN** the overview uses the canonical settlement balance excluding linked loan principal and counts each obligation once

#### Scenario: Missing account or association
- **WHEN** a source lacks a payment account or an independent forecast lacks an explicit loan association
- **THEN** the overview identifies the missing information without silently linking records

### Requirement: Repayment outlooks disclose uncertainty
The dashboard SHALL show a compact active-obligation outlook and distinguish archived balances from active follow-up. A completion date MUST be presented only when supported by sufficient recorded dates; incomplete or overdue schedules MUST NOT imply a debt-free date.

#### Scenario: Incomplete schedule
- **WHEN** an active obligation lacks a reliable completion date
- **THEN** the dashboard reports that the completion date is not defined

#### Scenario: Only archived loans remain
- **WHEN** no loan is active but historical loans have balances
- **THEN** the dashboard does not claim that all debts have been paid

### Requirement: Dashboard does not duplicate the reminder action list
The dashboard SHALL omit its action-centre block and its otherwise unused upcoming-reminder request while preserving reminder screens, reminder records, and unrelated financial projections.

#### Scenario: Dashboard is opened
- **WHEN** the financial dashboard renders
- **THEN** the action list is absent, its dashboard-only reminder request is not made, and the financial outlook remains visible

### Requirement: Review and publication remain explicit
All changes SHALL be validated through builds, scoped lint, and synthetic previews without writing or running tests. Publication MUST follow the user's approved review process, including any explicitly authorized one-time exception; no production financial mutation SHALL be implied by deployment.

#### Scenario: Local implementation completes
- **WHEN** the implementation and previews are ready
- **THEN** changes remain local until publication approval, and no payment or import is applied
