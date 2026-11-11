## Context

The connected-user worker reconciles source records into privately owned events. It reads hour usages but not independent care stays. Care records contain the first night and count or a daytime time range; overnight records do not store individual times. The published daycare form states 21:00–09:00 as the daycare overnight rule.

The existing `zeus-google-calendar-sync` proposal and authorization specification explicitly include past and future source records. Care uses that established history policy rather than introducing a separate date window.

## Goals / Non-Goals

**Goals:** Show daycare nights and Alessia bookings, including existing records, using the existing authorization and lifecycle.

**Non-Goals:** Synchronize cancellation charges, make financial changes, infer nanny overnight hours, or change authorization settings.

## Decisions

- Give care stays a separate `zeus-care:` source key and use the existing event reconciliation, stable remote IDs and ownership checks. Reusing the hour-usage key could collide with another source.
- Represent daycare nights from 21:00 on the first night through 09:00 after the last night in the configured time zone, as stated by the daycare form. Represent nanny nights as all-day events spanning their recorded night dates, with an exclusive end; no overnight hours are stored.
- Represent nanny daytime bookings with their stored start and end times. Exclude cancellation records because they are fees rather than bookings.
- Use private events and the Zeus color, without exposing financial amounts or care notes. Existing user authorization remains required.

## Risks / Trade-offs

- Nanny overnight bookings have no checkout time → all-day display identifies recorded nights without inventing hours.
- Existing bookings will be reconciled after a connected installation is deployed → no live synchronization is triggered by this local change.

## Migration Plan

Deploy the backend and updated settings description. Existing connected users are reconciled by the normal worker; no schema migration is required. Rollback of the projection removes its owned care events during subsequent reconciliation.
