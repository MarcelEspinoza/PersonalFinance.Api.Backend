## ADDED Requirements

### Requirement: Independent care booking visibility
The system SHALL project each recorded daycare night booking and Alessia day or night booking to a private Zeus-colored event on an explicitly connected user's primary Google Calendar. Cancellation fees SHALL NOT create booking events.

#### Scenario: Daycare overnight
- **WHEN** a daycare stay records one or more nights
- **THEN** its event starts at 21:00 on the first night and ends at 09:00 after the last night in the configured time zone

#### Scenario: Nanny overnight
- **WHEN** an Alessia stay records one or more nights
- **THEN** its all-day event covers the recorded night dates with an exclusive end after the last night, without an invented time

#### Scenario: Nanny day booking
- **WHEN** an Alessia daytime stay records a start and end time
- **THEN** its event uses that day and time range in the configured time zone

### Requirement: Safe care event lifecycle
The system SHALL reconcile one stable app-owned event per booking, including existing records, and propagate changes and deletions only while the user is connected. It SHALL not include prices or notes or change financial records.

#### Scenario: Booking is changed or removed
- **WHEN** a booking is changed or removed in the app
- **THEN** its existing owned event is updated or removed without creating a duplicate or modifying unrelated events

#### Scenario: Google is disconnected
- **WHEN** the user disconnects Google
- **THEN** synchronization stops and the existing remote events remain untouched
