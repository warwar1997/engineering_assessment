# Seed data

Seeded on first start by `DbSeeder` (only if the DB is empty). Times are relative to the moment
the app first starts, so classes are always in the future on a fresh DB.
To reseed: `docker compose down -v && docker compose up -d --build`.

`D+1` = tomorrow (UTC), etc. Ids below are what you get on a fresh database.

## Businesses

| Id | Name |
|---|---|
| 1 | Rezerv Fitness |
| 2 | Urban Flow Yoga |

## Package catalogue (`GET /api/packages`)

| Id | Business | Name | Credits | Validity |
|---|---|---|---|---|
| 1 | Rezerv Fitness | Rezerv 5 Class Pack | 5 | 30 days |
| 2 | Rezerv Fitness | Rezerv 10 Class Pack | 10 | 60 days |
| 3 | Urban Flow Yoga | Urban Flow 10 Class Pack | 10 | 60 days |
| 4 | Urban Flow Yoga | Urban Flow Drop-in | 1 | 7 days |

## Users and their packages

| User | Name | Packages | Used for testing |
|---|---|---|---|
| 1 | Alice | Rezerv 10-pack | happy path, overlap, late cancellation |
| 2 | Bob | Rezerv 10-pack, **expired** 10 days ago | expired package |
| 3 | Chloe | Rezerv 5-pack, **0 credits** left | insufficient credits |
| 4 | David | **Urban Flow only** | wrong business package |
| 5 | Emma | Rezerv 10-pack + Urban Flow 10-pack | multi-business, cancellation that triggers promotion |
| 6-12 | Frank ... Leo | Rezerv 10-pack | various bookings / waitlists |
| 13 | Mia | Rezerv 10-pack + Urban Flow drop-in (used) | |
| 14 | Noah | Rezerv 10-pack + Urban Flow drop-in (reserved on a waitlist) | |
| 15 | Olivia | Rezerv 10-pack | |

Check balances with `GET /api/users/{id}/packages`.

## Timetable

| Id | Business | Class | When | Booked / Slots | Waitlist | Scenario |
|---|---|---|---|---|---|---|
| 1 | Rezerv | Morning Yoga | D+1 10:00-11:00 | 12 / 15 | | mixed availability (same as spec example) |
| 2 | Rezerv | HIIT Blast | D+1 10:30-11:30 | 0 / 10 | | overlaps #1 |
| 3 | Rezerv | Spin Express | D+1 12:00-12:45 | **5 / 5** | Jack (10), Karen (11) | full + waitlist promotion |
| 4 | Rezerv | Pilates Core | now + ~2.5h | 2 / 8 | | late cancellation (within 4h) |
| 5 | Rezerv | Evening Boxing | **ended** ~2h ago | 2 / 2 | Leo (12) | waitlist after class ends |
| 6 | Rezerv | Strength Lab | D+2 18:00-19:00 | 0 / 5 | | concurrency test |
| 7 | Urban Flow | Vinyasa Flow | D+1 09:00-10:00 | 2 / 12 | | |
| 8 | Urban Flow | Power Yoga | D+1 18:00-19:00 | **3 / 3** | Noah (14) | full + waitlist |
| 9 | Urban Flow | Sunrise Barre | D+2 07:00-08:00 | 0 / 20 | | |
| 10 | Urban Flow | Yin & Meditation | D+3 19:00-20:00 | **1 / 1** | David (4) | full + waitlist |
| 11 | Rezerv | Core & Stretch | D+3 08:00-08:45 | 2 / 10 | | |

## Scenarios to try in Swagger

| Scenario | Request | Expected |
|---|---|---|
| Happy path | `POST /api/bookings` `{"userId":1,"scheduleId":11}` | 201, credits 8 -> 7 |
| Overlapping class | `{"userId":1,"scheduleId":2}` | 409 `OVERLAPPING_BOOKING` |
| Expired package | `{"userId":2,"scheduleId":6}` | 422 `PACKAGE_EXPIRED` |
| Insufficient credits | `{"userId":3,"scheduleId":6}` | 422 `INSUFFICIENT_CREDITS` |
| Wrong business | `{"userId":4,"scheduleId":6}` | 422 `PACKAGE_BUSINESS_MISMATCH` |
| Wrong business, explicit package | `{"userId":5,"scheduleId":6,"userPackageId":6}` | 422 `PACKAGE_BUSINESS_MISMATCH` |
| Full class | `{"userId":1,"scheduleId":3}` | 409 `SCHEDULE_FULL` |
| Full class, join waitlist | `{"userId":1,"scheduleId":3,"joinWaitlistIfFull":true}` or `POST /api/waitlist` | 202 / 201, 1 credit reserved |
| Waitlist promotion | find Emma's Spin booking via `GET /api/users/5/bookings`, then `POST /api/bookings/cancel` `{"bookingId":<id>,"userId":5}` | Emma refunded, Jack (10) promoted and charged 1 credit |
| Late cancellation | Alice's Pilates booking (`GET /api/users/1/bookings`), cancel it | 200, `creditRefunded: false` |
| Waitlist after class ends | `POST /api/waitlist/expire` (or wait up to 1 min for the Hangfire job) | Leo's entry `Expired`, reserved credit released, remaining unchanged |
| Concurrency | `./scripts/concurrency-test.sh` | exactly 5 x 201 and 5 x 409 on schedule 6 |
