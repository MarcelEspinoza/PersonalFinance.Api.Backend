## Context

The existing create and update endpoints accept account IDs independently of payment confirmation. `EntryDialog` currently omits this field, while the confirmation dialog already loads active accounts. Updating an entry with a null account does not clear an existing assignment.

## Goals / Non-Goals

**Goals:**

- Select an account for income or expense forecasts without confirming payment.
- Preserve unchanged assignments and retain a visible option for an existing inactive account.
- Report account-loading errors with retry while retaining entered form values.

**Non-Goals:**

- Clearing previously assigned accounts, changing backend null semantics or bulk-assigning historical records.
- Automatically choosing a particular account or changing amounts, dates or payment state.

## Decisions

- Reuse `LedgerService.getAccounts` inside `EntryDialog`, loading only while open with stale-response protection.
- Keep account loading independent from form initialization so retry does not erase input.
- Offer an unassigned option for new or already unassigned entries. Preserve an existing assigned account instead of exposing unsupported clearing.
- Extend the form result with an optional account ID and forward it through `LedgerMonthPage`. Updates omit an unchanged account; creation keeps an optional account.
- Disable saving while the account list is unavailable and expose an explicit retry. Existing error presentation and post-save month refresh remain unchanged.

## Risks / Trade-offs

- Assigning an account to an already paid entry changes the account balance allocation. Explain this in that editor; never alter confirmation state.
- An assigned inactive account may be missing from the active list. Render its existing ID/name as a preservation-only option and omit it from unchanged updates.
- Loading failure blocks saving rather than silently saving an unintended unassigned entry.

## Migration Plan

Frontend-only publication after explicit approval. No backend runtime changes or data migration are required.
