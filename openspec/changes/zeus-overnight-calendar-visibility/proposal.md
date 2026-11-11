## Why

Google Calendar currently includes Zeus hour usage but omits independently recorded daycare nights and care by Alessia. These bookings should be visible without changing their financial records.

## What Changes

- Synchronize existing and future daycare overnight bookings and Alessia day and overnight bookings through the existing authorized calendar connection.
- Preserve stable per-booking identities, private visibility, user-selected color, lifecycle reconciliation and financial independence.
- Describe the supported bookings in Google settings; do not generate appointments for cancellation fees.

## Capabilities

### New Capabilities

- `zeus-care-google-calendar`: One-way calendar visibility for independent Zeus daycare and nanny bookings.

### Modified Capabilities

None.

## Impact

The backend calendar projection and Google settings description change. No new API, permission, migration, financial mutation or automatic authorization is required.
