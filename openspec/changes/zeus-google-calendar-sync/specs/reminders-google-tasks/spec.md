## ADDED Requirements

### Requirement: Dedicated reminder task list
The system SHALL request Google Tasks authorization explicitly and synchronize reminders only in the PersonalFinance task list. Existing unrelated tasks MUST NOT be modified.

#### Scenario: First reminder synchronization
- **WHEN** the authorized user has reminders and no saved task list
- **THEN** the app finds or creates the PersonalFinance list and persists its identity

### Requirement: Reminder task lifecycle
The system SHALL create, update, complete and delete an app-owned task according to its source reminder. Effective reminder dates SHALL be represented without an exact time, and app notifications SHALL remain unchanged.

#### Scenario: Reminder is completed or postponed
- **WHEN** a non-recurring reminder is completed or its effective date changes
- **THEN** its task reflects the completed state or the new date without a duplicate task

#### Scenario: Recurring reminder advances
- **WHEN** completing a recurring reminder advances its due date in the app
- **THEN** the same task represents the next due date as an active task

#### Scenario: Reminder is deleted
- **WHEN** the reminder is removed
- **THEN** only the task bearing that source's app-owned marker is removed

### Requirement: Recover uncertain task creation
The system SHALL persist an app-owned source marker before creating a task and search for that marker, including completed and hidden tasks, before retrying creation after an uncertain response.

#### Scenario: Create response is lost
- **WHEN** Google has created a task but the app has not persisted its generated task ID
- **THEN** the retry adopts the task with its source marker rather than creating another task

### Requirement: One-way synchronization
The system SHALL treat the app as the source of truth; Google-side edits or completion MUST NOT modify reminders or financial data in the app.

#### Scenario: Task is edited in Google
- **WHEN** a user changes a task directly in Google
- **THEN** no reminder or financial record is changed in the app
