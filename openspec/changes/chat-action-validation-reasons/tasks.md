## 1. Validation explanations

- [x] 1.1 Return safe, specific rejection reasons from movement normalization and log them.
- [x] 1.2 Preserve valid cards and explain rejected proposals without unsupported month-status claims.

## 2. Validation and publication

- [x] 2.1 Compile and preview controlled rejected and valid December proposals locally without tests or real financial writes.
- [x] 2.2 Show the preview, exact files and commit message and obtain separate commit/push approval.

## 3. Invalid reference repair follow-up

- [x] 3.1 Add one bounded correction round for invalid movement concept/account references, followed by existing validation; preserve valid siblings and all non-reference fields.
- [x] 3.2 Clarify repeat-versus-move intent and reuse source movement identifiers.
- [x] 3.3 Compile and manually preview repaired, persistently invalid, mixed and failed-provider replies without tests or real financial writes; show the actual frontend screenshot.
- [x] 3.4 Obtain separate approval for the follow-up commit and push.

The local preview exercised the real chat service with a disposable MySQL database and controlled model replies: missing/unavailable concept, unavailable account, invalid date, missing amount, mixed valid/rejected proposals and a valid December 370-euro driving-school proposal. With a synthetic 100-euro account balance, the valid proposal retained its confirmation card. No ledger entries were created. The actual frontend displayed the precise rejection and subsequent valid card; its screenshot uses synthetic data. Compilation and whitespace checks passed.

The user explicitly approved commit and push of the six listed files and the presented commit message after reviewing the synthetic preview.

The follow-up manual preview used the real service with controlled model transport and a synthetic November source expense. Missing/unavailable concept and unavailable account references recovered in one correction round; persistent invalid references, malformed responses, HTTP/network failures, unexpected action counts, and attempts to change amount/date were rejected. A valid sibling survived provider failure, already valid account references were preserved, and valid/date-invalid/amount-invalid replies did not trigger a correction round. Every request used at most two model calls. The sole seeded November entry and its 370-euro amount/date remained unchanged, with no December entry written. The actual frontend displayed the December 370-euro confirmation card with Vehiculo/Principal using synthetic data. Compilation and whitespace-diff checks passed; the full-file formatter reported only pre-existing compact anonymous-object formatting outside the changed lines.

After viewing the follow-up screenshot, the user explicitly approved commit and push of the five listed backend files with message `fix(chat): repair invalid movement references before proposing actions`.
