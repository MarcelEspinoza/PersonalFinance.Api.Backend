## Context

The existing loan page reads the legacy Loans collection. Settlements have a separate, authoritative forecast that deliberately excludes linked loan balances and carried-forward duplicates. Independent ledger forecasts are not necessarily associated with loan records. The requested archive scope is every existing record in the current loan tab, including personal loans; it is not permission to erase repayments or declare debts extinguished.

## Goals / Non-Goals

**Goals:** Preserve historical loans outside active follow-up, provide a detailed loans-and-collections hub, show a truthful compact repayment outlook, and remove the duplicate dashboard action centre.

**Non-Goals:** Changing account balances, registering payments, forgiving debts, removing independent forecasts, applying imports, automatic production changes on deployment, or changing care/calendar behavior.

## Decisions

- Add nullable `ArchivedAt` to the existing Loan model rather than reusing paid/cancelled status. Preserve amounts, original status, payments, and dates. Restore clears only the archival timestamp.
- Archive an explicit set of owned identifiers atomically and idempotently. Never archive automatically from a GET, page load, or application startup. Future records remain active.
- Build a read-only overview from owned active loan records and canonical open settlements. Reuse `SettlementForecast` for settlement balances, carry-over, and known forecast dates; do not add linked loan principal to settlement amounts.
- Keep archived balances visible in a separate historical view and exclude archived records from current loan follow-up. The UI must not equate no active payments with being debt-free.
- Show recorded counterparties, dates, installments, payment history, and source links. Missing account, person, or schedule information is explicitly unknown; never infer associations from names or amounts.
- Show a latest-payment estimate only when all included active obligations have usable recorded completion dates. Unknown or overdue schedules suppress a definitive finish date. Do not extrapolate the dashboard's seven-month horizon into a debt completion date.
- Keep borrowing and repayments identifiable as financing/pass-through movements in the overview without rewriting existing cash accounting.
- Remove the dashboard's `ActionCenter` import/render and its sole `useUpcomingReminders` call. Preserve reminders and other dashboard sections.
- Surface load and mutation errors with retry. Protect detail/history loading against stale responses when changing selections.

## Risks / Trade-offs

- Independent forecasts can remain after a catalogue record is archived: preserve them and make the scope explicit rather than silently cancelling cash commitments.
- Archived loans can remain referenced by historical settlements: preserve references and snapshots; do not attach new activity to archived loans automatically.
- Loan data can lack a reliable payment account or complete schedule: display missing information and avoid invented payoff dates.
- The calendar investigation is independent and must not modify this change's files or calculations.

## Migration Plan

Generate and inspect an additive nullable-column migration, retaining existing encrypted token TEXT mappings. Build and preview locally without tests. Publication requires approval of exact messages and file lists; production archival is a separate explicit operation against the approved existing records.
