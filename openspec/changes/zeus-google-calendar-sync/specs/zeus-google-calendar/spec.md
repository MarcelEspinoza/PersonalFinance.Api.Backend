## ADDED Requirements

### Requirement: Explicit secure calendar connection
The system SHALL connect only the authenticated user's explicitly authorized Google account, target its primary calendar, and store encrypted credentials. State MUST expire and be usable once; callbacks MUST use a configured redirect destination.

#### Scenario: Authorization succeeds
- **WHEN** the user grants calendar event access through a valid callback
- **THEN** the app stores user-bound encrypted tokens and schedules all existing source records, including past dates

#### Scenario: Invalid or replayed authorization
- **WHEN** the callback has missing, expired or previously consumed state
- **THEN** no Google account is connected and an explicit connection failure is returned

### Requirement: Zeus event lifecycle
The system SHALL eventually create, update and delete one app-owned calendar event for each Zeus hour usage as the usage is created, modified or deleted. Durations without a start/end time SHALL appear as all-day events.

#### Scenario: Timed usage changes
- **WHEN** a usage's day, hours, duration or note changes
- **THEN** its existing event is updated without creating a duplicate and local daylight-saving offsets are respected

#### Scenario: Untimed usage is deleted
- **WHEN** an all-day usage is deleted
- **THEN** its app-owned event is removed, with already absent events treated as successfully removed

### Requirement: Pasanaco delivery events
The system SHALL create one all-day delivery event on day 5 of each assigned participant's month, name the recipient, and update or remove it when its source schedule changes. Calendar operations MUST NOT mark a payout or quota as paid.

#### Scenario: Assigned month or recipient changes
- **WHEN** the pasanaco start date, participant or relevant schedule data changes
- **THEN** the corresponding app-owned event reflects the current recipient and month

#### Scenario: Source is removed
- **WHEN** the participant or pasanaco is removed
- **THEN** the app removes only the delivery events it owns

### Requirement: Durable and observable synchronization
The system SHALL persist event identities and pending work, retry transient failures without duplicate creates, isolate users, and display connection/error/pending status.

#### Scenario: Interrupted event creation
- **WHEN** Google creates an event but the response is lost
- **THEN** a retry uses the same event identity and reconciles the owned event instead of inserting a duplicate

#### Scenario: Permission expires
- **WHEN** Google rejects revoked or expired authorization
- **THEN** synchronization requests reconnection, retains pending source work and does not change financial records

### Requirement: Explicit disconnection
The system SHALL stop synchronization and clear locally stored Google credentials when the user disconnects, preserving existing Google events.

#### Scenario: Connection is removed
- **WHEN** the user confirms disconnection
- **THEN** new work is not sent to Google and the UI explains that existing events remain

### Requirement: Global connection and explicit synchronization state
The system SHALL expose one normal connection/disconnection control in the global application header. Page panels SHALL display Synchronized only after completed work with no pending items or errors, disable that status control when current, and allow retry after failures.

#### Scenario: Connected account is current
- **WHEN** synchronization has completed without pending work or reported errors
- **THEN** the panel shows a disabled Synchronized control and disconnection is available only in the global header

#### Scenario: Work is pending or failed
- **WHEN** synchronization is pending or an error is reported
- **THEN** the panel shows synchronization in progress or an enabled retry control, never a successful synchronized state

### Requirement: Per-user event colors
The system SHALL let each authenticated user choose valid Google Calendar colors separately for Zeus and pasanaco and persist these preferences across reloads, disconnection and reconnection.

#### Scenario: Existing events are recolored
- **WHEN** a user saves different event colors
- **THEN** existing and future owned events acquire the selected source-specific colors without duplicate creation or changes to Google Tasks

#### Scenario: Invalid color is submitted
- **WHEN** a color outside the supported Google event palette is submitted
- **THEN** the request fails explicitly without changing preferences or existing events
