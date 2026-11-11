## ADDED Requirements

### Requirement: Distinct budget and payment actions

The expense plan SHALL identify total-budget editing separately from adding or editing a fixed payment. The budget editor SHALL explain that a new limit replaces the previous limit and includes recorded purchases.

#### Scenario: Edit an expense budget

- **WHEN** a user opens an existing variable expense category
- **THEN** the action and form identify budget editing, and saving updates the total limit without creating a payment.

#### Scenario: Edit an existing recurring receipt

- **WHEN** a user changes the amount or account of an existing fixed receipt
- **THEN** the action identifies receipt editing and continues to update the existing rule without requiring new-payment acknowledgement.

#### Scenario: Add an independent fixed payment

- **WHEN** a user starts adding a fixed expense
- **THEN** the action identifies an additional fixed payment rather than a replacement budget.

### Requirement: Explicit budget-to-receipt conversion

The interface SHALL require explicit acknowledgement before an expense budget becomes a recurring payment. It SHALL explain that the budget is replaced and the receipt counts alongside existing purchases. Both the edit and add-payment entry paths MUST enforce the same confirmation, including in the submit handler.

#### Scenario: Unacknowledged conversion

- **WHEN** a user selects monthly or bimonthly recurrence for an existing expense budget without acknowledging the conversion
- **THEN** submission is disabled and a submit attempt cannot send an update.

#### Scenario: Confirmed conversion

- **WHEN** a user explicitly acknowledges conversion
- **THEN** the form can submit the existing fixed-payment payload using an add-payment action.

#### Scenario: Change category or recurrence

- **WHEN** a user changes the selected category or recurrence after acknowledging conversion
- **THEN** the acknowledgement is reset.

#### Scenario: Preserve income behavior

- **WHEN** a user edits an income category
- **THEN** expense-specific conversion confirmation is not introduced.
