## Why

The assistant rejects a December driving-school expense proposal with a generic message that incorrectly suggests checking a closed month. Movement validation does not inspect balances or month status, and its actual failure is not exposed or logged.

## What Changes

- Explain the specific invalid field when a new expense or income proposal is rejected.
- Log a safe rejection reason without exposing raw model responses or financial identifiers.
- Preserve valid confirmation cards and explain partial rejection without claiming writes occurred.
- Keep negative projected balances allowed and require explicit confirmation before financial writes.
- Repair invalid category/account identifiers with one bounded model round using current authoritative identifiers, without altering other proposed fields or valid sibling actions.
- Clarify that repeating an existing movement in a second month creates a new proposal rather than moving the source.
- Let a repeated movement reference its source entry so the server takes the category and account from it when the model garbles the copied identifiers.

## Capabilities

### New Capabilities

- `chat-action-validation-reasons`: Actionable explanations for rejected financial movement proposals.

### Modified Capabilities

None.

## Impact

Backend global chat normalization and response generation only. No dependencies, schema changes, automatic transactions or frontend contract changes. Validation uses compilation and an isolated local preview without tests.
