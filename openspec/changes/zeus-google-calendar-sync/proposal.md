## Why

Zeus attendance and monthly pasanaco recipients are already recorded in the app. Keeping their appointments manually in Google Calendar duplicates work and leaves outdated events after edits or deletions.

## What Changes

- Connect each user's Google account through an explicit authorization flow.
- Synchronize all past and future Zeus hour usages into that account's primary calendar.
- Create, update and delete the corresponding event when the source usage changes.
- Show untimed usages as all-day events and timed usages in the configured local time zone.
- Schedule each pasanaco recipient's delivery as an all-day event on the fifth of their assigned month.
- Synchronize reminders, including edits, completion and deletions, into a separate PersonalFinance Google Tasks list.
- Keep synchronization durable, retry failures and show connection/pending/error status without changing finance behavior.
- Show one global Google connection control, a disabled Synchronized state when current, and retry controls for failures.
- Let each user choose separate Google event colors for Zeus and pasanaco, updating existing owned events.

## Capabilities

### New Capabilities

- `zeus-google-calendar`: Secure, one-way synchronization of Zeus hour usages and monthly pasanaco deliveries to the user's primary Google Calendar.
- `reminders-google-tasks`: One-way reminder synchronization to the dedicated PersonalFinance task list.

### Modified Capabilities

None.

## Impact

New persisted authorization/connection/synchronization records, Google OAuth, Calendar and Tasks HTTP calls, a reconciliation/retry worker, an EF migration, and shared connection controls on the Zeus, Pasanaco and Reminders pages. Requires Google OAuth credentials and a stable token-encryption key supplied through environment variables. No AI calls, new financial transactions, or changes to unrelated Google events/tasks.
