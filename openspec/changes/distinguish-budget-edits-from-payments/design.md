## Context

The existing plan correctly replaces variable budget limits, updates existing recurring rules and sums independent payments. Its generic change/save labels and similar recurrence choices obscure those differences. Creating a fixed rule for a variable expense also removes its default budget, so a warning must cover both category editing and the add-payment dialog.

## Goals / Non-Goals

**Goals:**

- Identify budget replacement, existing-receipt editing and new fixed-payment creation explicitly.
- Explain that recorded purchases count inside a total budget.
- Require a deliberate acknowledgement before replacing an expense budget with a recurring payment.

**Non-Goals:**

- Changing financial calculations, automatically merging purchases with receipts or modifying historical transactions.
- Changing income handling, backend APIs, dependencies or unrelated import-picker work.

## Decisions

- Reuse `PlanPage` row actions and `PlanConceptDialog`; no new dialog or component abstraction is needed.
- Keep existing service methods and payloads. A budget edit remains a budget update; editing a recurring receipt continues to reuse its existing rule.
- Use explicit expense labels for monthly budgets and independent receipts, and contextual submit buttons.
- Derive conversion risk from the original category and selected recurrence. Require a checkbox in either entry path, disable submission until acknowledged and guard the submit handler. Reset acknowledgement when the selected category or recurrence changes.
- Retain existing warnings about manually recorded amounts and preserve error reporting.

## Risks / Trade-offs

- Fixed receipts legitimately coexist with separate purchases. Preserve their addition rather than hiding charges with a global maximum.
- Conversion still replaces the category budget. Explain both the loss of the budget and the independent receipt before allowing it.
- The safeguard is an interactive UI protection, not an API authorization rule. Existing integrations and API contracts remain unchanged.

## Migration Plan

Deploy frontend-only changes after explicit commit/push approval. No schema or data migration is required. Rollback restores the prior interface without changing stored financial data.
