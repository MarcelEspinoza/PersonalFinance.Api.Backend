## ADDED Requirements

### Requirement: Explain rejected movement proposals
The assistant SHALL explain which required movement field failed validation without exposing arbitrary model payloads or private identifiers.

#### Scenario: Missing concept
- **WHEN** an expense proposal has an amount and date but no valid expense concept
- **THEN** the response explains the concept problem and requests the corresponding correction instead of suggesting an unverified closed month.

#### Scenario: Invalid account or date
- **WHEN** a movement proposal identifies an unavailable account or contains an invalid date
- **THEN** the response identifies that field's problem and no confirmation card is created for the rejected proposal.

### Requirement: Preserve valid actions and financial safety
The assistant SHALL retain valid confirmation cards, explain rejected sibling proposals, and never write money while preparing a reply.

#### Scenario: Mixed valid and rejected proposals
- **WHEN** one proposed movement is valid and another fails
- **THEN** the valid card remains available and the response explains the rejected proposal without claiming either was executed.

#### Scenario: Negative December projection
- **WHEN** a valid 370-euro December driving-school expense would leave the month negative
- **THEN** the negative projection alone does not reject the confirmation card and the expense remains unapplied until explicit user confirmation.

### Requirement: Honest fallback and safe diagnostics
The assistant SHALL log safe movement rejection reasons and SHALL NOT attribute an unknown validation failure to unverified financial conditions.

#### Scenario: Unsupported or unrelated proposal failure
- **WHEN** a rejected action has no specific available validation reason
- **THEN** the fallback acknowledges that limitation and does not claim that a period is closed or a movement already paid.

### Requirement: Bounded correction of movement references
The assistant SHALL attempt at most one correction round for invalid category/account references using the current authoritative snapshot, preserve all valid sibling actions, and revalidate every resulting proposal.

#### Scenario: Repeat an existing November expense in December
- **WHEN** the user asks to keep November's expense and add the same expense in December and the model supplies an invalid category reference
- **THEN** a correction can reuse the existing movement's active category/account identifiers, preserve the original proposed amount, date and description, and return a December confirmation card without writing or moving either expense.

#### Scenario: Failed or unsafe correction
- **WHEN** the correction remains invalid, changes non-reference fields, has an unexpected action count or fails at the provider
- **THEN** no further correction is attempted, valid sibling cards survive, and unresolved proposals retain a specific rejection rather than being executed.
