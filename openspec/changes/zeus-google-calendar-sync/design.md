## Context

The backend uses .NET 9, EF Core/MySQL and JWT authentication. Zeus hour usages include a date and optional start/end times; pasanaco recipients have an assigned round relative to the starting month. Google authorization and persistent credential storage do not exist yet. Both repositories prohibit writing or running tests; validation uses compilation, frontend builds and lint.

## Goals / Non-Goals

**Goals:** Connect the user's primary Google calendar and a PersonalFinance Tasks list; synchronize past and future Zeus hours, pasanaco deliveries and reminders; propagate app-side creates, edits, completion and deletions reliably; expose setup, pending and reconnect errors.

**Non-Goals:** Google-to-app updates, synchronizing purchases or Zeus care stays, creating financial payments, AI use, managing unrelated events, or sending invitations.

## Decisions

- Use server-side Google OAuth authorization code flow with PKCE, expiring single-use hashed state and explicit offline consent. Frontend receives only the authorization URL and connection status, never Google credentials.
- Encrypt access/refresh tokens and PKCE verifiers with AES-GCM using a separate stable 32-byte environment key and user-bound associated data. In-memory Data Protection keys are unsuitable for credentials that must survive Railway restarts.
- Poll connected users' source records every 15 seconds and reconcile a durable per-source event record. This detects additions, edits and deletions across all existing mutation paths without coupling calendar HTTP failures to financial transactions.
- Persist a stable Google event ID before sending a create request. Retry the same ID after uncertain outcomes; verify private ownership metadata before changing an existing event. Keep deletion work until Google confirms removal; absent/deleted remote events are successful deletions.
- Lease each connection in the database to prevent concurrent workers. Reconnection changes a generation identifier; stale workers cannot overwrite new credentials or acknowledge a new connection's pending work.
- Use timed events in Europe/Madrid by default, respecting daylight-saving rules. Untimed Zeus records and all pasanaco deliveries are all-day events with an exclusive next-day end date.
- Pasanaco event date is start month plus assigned round minus one, on day 5; title names the recipient. Completed pasanacos retain past/current-round deliveries but omit later rounds.
- Request Calendar events and Google Tasks scopes together. Tasks use a separate PersonalFinance list and an app-owned source marker in their notes. Since Tasks does not accept caller-generated IDs, search that list (including completed/hidden tasks) for the persisted marker before retrying an uncertain creation.
- A reminder maps to one task. Completion marks non-recurring reminders completed; when the app advances a recurring reminder, the same task moves to its next date. Snoozing adjusts the task's effective date. Task API dates have no exact time; existing app email/notification rules remain unchanged.
- Disconnect stops future synchronization and clears local credentials; existing Google events remain. Reconnect repopulates current source records and reconciles stale mappings.
- Share connection state through a layout-level provider and place the only normal connect/disconnect control in the global header. Zeus, Pasanaco and Reminders show only compact synchronization status/retry; explanations, last synchronization, color preferences and required reauthorization belong to Settings > Google.
- Allow the fixed `/settings` OAuth return path and restore its Google tab after authorization. Continue rejecting arbitrary return destinations.
- Show Synchronized only after a completed synchronization with zero pending items and no reported error. Disable that status button while synchronized or awaiting automatic synchronization; enable retry for failures.
- Persist separate per-user Zeus/pasanaco Google event color IDs (1-11), defaulting to purple (3) and green (10). Add only two small integer columns; keep encrypted credentials mapped to TEXT.
- Saving changed colors invalidates the connection generation/lease, queues owned calendar events and clears the previous completion timestamp. Reconciliation updates the existing event identities with their desired color; Tasks and unrelated Google resources are not recolored.

## Risks / Trade-offs

- Google consent or refresh credentials can expire or be revoked -> show reconnect status and retain pending work.
- Network errors or quotas -> exponential retry and explicit error status; never report failed synchronization as successful.
- Source changes arrive during HTTP work -> next reconciliation compares desired content and applies the latest version.
- Deletions/recreations or manually deleted Google events -> preserve ownership checks and allocate a new event ID only after remote absence is confirmed.
- Google OAuth external apps in testing can issue short-lived refresh tokens -> document test users, publication and reconnection requirements.
- Synchronization is eventually consistent -> show pending counts and provide a manual retry action.

## Migration Plan

Deploy additive tables and disabled-by-missing-credentials integration. Configure Google Calendar API, a web OAuth client, an exact callback URL, frontend URL and stable encryption key. Authorize the Google account from the app. Roll back application code without dropping credential/event tables; remove Google consent separately if desired.

## Open Questions

The user must create/provide the Google OAuth application credentials and authorize their account. No Google account can be linked or verified end-to-end before that step.

## Google and Railway Setup

1. Enable Google Calendar API and Google Tasks API in a Google Cloud project.
2. Configure Google Auth Platform consent and add the intended account as a test user while the OAuth application is in testing.
3. Create a Web application OAuth client. Register the exact backend callback URL, for example `https://personalfinanceapibackend-production.up.railway.app/api/calendar/google/callback`.
4. Configure backend environment variables `GoogleCalendar__ClientId`, `GoogleCalendar__ClientSecret`, `GoogleCalendar__RedirectUri`, `GoogleCalendar__FrontendUrl` and `GoogleCalendar__TokenEncryptionKey`. Use the frontend origin for FrontendUrl.
5. Generate TokenEncryptionKey as a cryptographically random 32-byte value encoded in Base64, store it only in the environment/secret store, keep it stable across redeployments and back it up securely. Changing or losing it requires reconnecting Google.
6. Deploy the additive migration and both applications. Open Zeus, Pasanaco or Reminders, choose Connect Google and authorize both scopes.
7. Verify an app-side create/edit/delete and reminder completion against the authorized account before treating live synchronization as verified.

An external Google OAuth app left in testing commonly receives refresh tokens that expire after seven days for these scopes. Publishing the consent application, or reconnecting when prompted, is necessary for ongoing personal use. Google may require verification for broader distribution. Disconnect clears local tokens but leaves Google resources; Google account settings can additionally revoke the application's grant.
