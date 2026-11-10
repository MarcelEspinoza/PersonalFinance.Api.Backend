## ADDED Requirements

### Requirement: Rejected model actions are visible
The system SHALL retain valid action cards and display a readable explanation for rejected or unsupported proposed actions. It MUST NOT silently discard all actions while implying that executable cards are present.

#### Scenario: Invalid target identifier
- **WHEN** the model proposes a link to an unavailable pending target
- **THEN** the response explains why its card could not be prepared and retains other valid cards

### Requirement: Users can act without another AI request
Reminder and settlement proposals SHALL render confirmation cards using the existing assistant workflow. A failed confirmation SHALL retain the card with its error and retry control. Displaying validation feedback MUST NOT require another model request.

#### Scenario: Carlos reminder card
- **WHEN** a validated proposal links a bank receipt to Carlos's reminder
- **THEN** the card shows the reminder, amount and pending effect, and confirmation prepares the link without completing the reminder early

### Requirement: Malformed responses are not success-shaped
An invalid structured model response SHALL produce a readable error explaining that no executable cards were prepared, rather than exposing raw JSON as a successful answer. A plain-text answer without any JSON object SHALL be shown to the user together with a notice that no cards were prepared. Previous assistant turns SHALL be sent back to the model in the expected JSON shape so the conversation keeps the structured format.

#### Scenario: Truncated model response
- **WHEN** the response cannot be parsed as the expected object
- **THEN** the assistant displays an explicit preparation error and does not execute or invent actions

#### Scenario: Plain-text model response
- **WHEN** the model answers with plain text and no JSON object
- **THEN** the assistant shows that text, adds a notice that no card was prepared, and does not execute or invent actions
