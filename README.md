# Rezerv Booking Engine

A simplified booking engine for fitness studios, built for the Rezerv backend assessment.
Customers buy credit packages from a studio (business), book classes with those credits,
cancel (with or without a refund), and join a FIFO waitlist when a class is full.

**Stack:** .NET 8, ASP.NET Core Web API, EF Core 8 + MySQL 8 (Pomelo), Redis (StackExchange.Redis),
Hangfire, Swagger, xUnit.

- [Setup](#setup)
- [API overview](#api-overview)
- [Architecture](#architecture)
- [Concurrency strategy](#concurrency-strategy)
- [Caching](#caching)
- [Business rules and assumptions](#business-rules-and-assumptions)
- [Tradeoffs](#tradeoffs)
- [Scaling in production](#scaling-in-production)
- [Testing](#testing)
- Database schema / ERD: [docs/DATABASE.md](docs/DATABASE.md) (DDL in [docs/schema.sql](docs/schema.sql))
- Seed data and test scenarios: [docs/SEED_DATA.md](docs/SEED_DATA.md)

---

## Setup

### Option 1: everything in Docker (recommended)

Requires Docker Desktop.

```bash
docker compose up -d --build
```

That starts MySQL, Redis and the API. On startup the API applies the EF migrations and seeds sample data.

- Swagger UI: http://localhost:8080/swagger
- Hangfire dashboard: http://localhost:8080/hangfire

Reset to a fresh seeded database (the seed is relative to "now", so do this if the seed is a few days old):

```bash
docker compose down -v && docker compose up -d --build
```

### Option 2: run the API locally

Requires the .NET 8 SDK.

```bash
docker compose up -d mysql redis      # or point appsettings.json at your own MySQL 8 / Redis
dotnet run --project src/Rezerv.Api
```

Swagger is then at http://localhost:5080/swagger. Connection strings are in `src/Rezerv.Api/appsettings.json`
and can be overridden with environment variables (`ConnectionStrings__MySql`, `Redis__ConnectionString`).

### Tests

```bash
dotnet test
./scripts/concurrency-test.sh   # against the running API: 10 parallel bookings for 5 slots
```

---

## API overview

All the required endpoints, plus a few read endpoints so the data is easy to inspect from Swagger.
There is no authentication, so the user is passed as `userId` in the request (see assumptions).

| Method | Route | Description |
|---|---|---|
| GET | `/api/packages?businessId=` | Package catalogue (what can be bought) |
| POST | `/api/packages/purchase` | Mock purchase, gives the user a package with credits |
| GET | `/api/timetable?businessId=&date=yyyy-MM-dd` | Timetable with attendance, slots and waitlist count |
| GET | `/api/timetable/{id}` | One schedule, uncached |
| POST | `/api/bookings` | Book a class (or join the waitlist with `joinWaitlistIfFull: true`) |
| POST | `/api/bookings/cancel` | Cancel, refund if more than 4h before start, promote from waitlist |
| POST | `/api/waitlist` | Join the waitlist of a full class |
| POST | `/api/waitlist/leave` | Leave a waitlist (reserved credit is released) |
| POST | `/api/waitlist/expire` | Run the "class ended" waitlist cleanup now (it also runs every minute via Hangfire) |
| GET | `/api/users`, `/api/businesses` | Seeded users / businesses |
| GET | `/api/users/{id}/packages` | User's packages with remaining / reserved / available credits |
| GET | `/api/users/{id}/bookings` | User's bookings |
| GET | `/api/users/{id}/waitlist` | User's waitlist entries with queue position |

Timetable response (matches the example in the brief, with a few extra fields):

```json
{
  "scheduleId": 1,
  "className": "Morning Yoga",
  "instructorName": "John Doe",
  "startTime": "2026-11-21T10:00:00Z",
  "endTime": "2026-11-21T11:00:00Z",
  "attendanceCount": 12,
  "availableSlots": 15,
  "remainingSlots": 3,
  "isFull": false,
  "waitlistCount": 0,
  "businessId": 1,
  "businessName": "Rezerv Fitness",
  "canBook": true,
  "canJoinWaitlist": false
}
```

`availableSlots` is the class capacity, as in the brief's example (12 attendees, 15 slots).
I added `remainingSlots` because "available" is easy to misread. `canBook` / `canJoinWaitlist` tell a
client which call to make next. That's how "proceed to booking from a timetable schedule" works: take the
`scheduleId` and call `POST /api/bookings` or `POST /api/waitlist`.

### Errors

Errors use RFC 7807 problem details with a stable `code` the client can switch on:

```json
{ "status": 422, "title": "Business rule violated", "detail": "This package expired on ...", "code": "PACKAGE_EXPIRED" }
```

| HTTP | Codes |
|---|---|
| 400 | `VALIDATION_FAILED`, model validation errors |
| 404 | `NOT_FOUND` |
| 409 | `SCHEDULE_FULL`, `SCHEDULE_NOT_FULL`, `OVERLAPPING_BOOKING`, `ALREADY_BOOKED`, `ALREADY_WAITLISTED`, `BOOKING_NOT_ACTIVE`, `RESOURCE_BUSY`, `CONCURRENT_UPDATE` |
| 422 | `PACKAGE_EXPIRED`, `INSUFFICIENT_CREDITS`, `PACKAGE_BUSINESS_MISMATCH`, `NO_ELIGIBLE_PACKAGE`, `CLASS_ALREADY_STARTED` |
| 503 | `LOCK_SERVICE_UNAVAILABLE` (Redis down, see tradeoffs) |

409 means "conflicts with the current state, could succeed later" (e.g. a slot opens up). 422 means
"this request will not succeed until the user changes something" (buy a package, pick another class).

---

## Architecture

Clean architecture with four projects. Dependencies only point inwards:

```
Rezerv.Api  ──►  Rezerv.Infrastructure  ──►  Rezerv.Application  ──►  Rezerv.Domain
 controllers       EF Core / MySQL             use cases (services)      entities, rules,
 error handling    Redis lock + cache          DTOs, abstractions        policies, errors
 Swagger, Hangfire seeding, jobs
```

**Domain** (`src/Rezerv.Domain`) has no dependencies. The entities own their rules, for example:

- `UserPackage.EnsureUsableFor(schedule, now)` checks business match and expiry
- `UserPackage.DebitCredit / ReserveCredit / ConvertReservationToDebit / ReleaseReservation / RefundCredit`
- `TimetableSchedule.TakeSlot / FreeSlot / EnsureNotStarted / OverlapsWith`
- `Booking.Cancel(schedule, now)` decides the refund using `CancellationPolicy` (strictly more than 4h)
- `WaitlistEntry.MarkPromoted / MarkExpired / MarkSkipped / MarkCancelled`

Rule violations throw a `DomainException` with an error code and kind, which the API maps to HTTP.

**Application** (`src/Rezerv.Application`) holds the use cases: `BookingService`, `WaitlistService`,
`PackageService`, `TimetableService`. It defines the ports it needs (`IAppDbContext`,
`IDistributedLockService`, `ICacheService`) and uses `TimeProvider` for the clock, so the tests can move
time forward. Checks that need the database (overlap, duplicates, choosing a package) live in
`BookingRules`, which booking, waitlist join and waitlist promotion all share.

I let the Application layer depend on EF Core's `DbSet` through `IAppDbContext` instead of adding a
repository per entity. For a codebase this size a generic repository on top of EF adds code without adding
much, and LINQ projections stay simple. If persistence had to be swappable, I'd add repositories.

**Infrastructure** (`src/Rezerv.Infrastructure`): `AppDbContext` and the entity configurations
(concurrency tokens, check constraints, indexes), migrations, the seeder, the Redis lock and cache, and
the Hangfire job.

**Api** (`src/Rezerv.Api`): thin controllers, a global `IExceptionHandler` that produces problem details,
Swagger with XML comments, Hangfire setup.

### Booking flow

```
POST /api/bookings
  └─ acquire Redis locks: schedule:{id}, then user:{id}
       ├─ load schedule; reject if it has started
       ├─ reject if already booked / already waitlisted
       ├─ pick package (explicit or auto) → business match, not expired, available credit ≥ 1
       ├─ reject overlapping confirmed bookings
       ├─ if full → 409 SCHEDULE_FULL, or waitlist (reserve 1 credit) if joinWaitlistIfFull
       ├─ schedule.TakeSlot(); package.DebitCredit(); add Booking + ledger row
       └─ SaveChanges (one transaction, concurrency tokens checked)
  └─ release locks, bump timetable cache version
```

### Cancel and waitlist promotion flow

```
POST /api/bookings/cancel
  └─ lock schedule:{id}
       ├─ booking.Cancel() → refund if > 4h before start (no cancelling once the class has started)
       ├─ schedule.FreeSlot(); refund credit if due
       ├─ promote: first Waiting entry (ORDER BY JoinedAt, Id)
       │     ├─ lock user:{promotedUserId}
       │     ├─ package expired or user now has an overlapping booking → Skipped, release reservation, try next
       │     └─ otherwise TakeSlot, reserved credit → debit, create Booking (source = WaitlistPromotion)
       └─ SaveChanges: cancellation and promotion commit together or not at all
```

### Background job

`WaitlistExpiryJob` runs every minute (Hangfire recurring job). It finds `Waiting` entries whose class has
ended, marks them `Expired` and releases the reserved credit (no deduction). It works in batches and is
idempotent. You can also trigger it with `POST /api/waitlist/expire`.

---

## Concurrency strategy

The brief's example: 5 slots, 10 users booking at the same time. Two layers protect against overbooking.

### 1. Redis distributed lock (main mechanism)

Before a booking, cancellation or waitlist change, the request takes a Redis lock:

- `lock:schedule:{scheduleId}`: serialises everyone competing for the same class, so the
  "is there a slot? → take it" check-then-act runs one request at a time per class.
- `lock:user:{userId}`: stops one user from double-spending a credit or booking two overlapping
  classes in two parallel requests (the schedule lock alone doesn't cover that because they are
  different classes).

Implementation (`RedisDistributedLockService`):

- acquire with `SET key <random token> NX PX <ttl>` (TTL 10s, so a crashed instance can't hold a lock forever)
- if taken, retry with a small jittered delay for up to 5s, then return `409 RESOURCE_BUSY`
- release with a Lua compare-and-delete, so an instance never deletes a lock that expired and was taken by someone else
- **lock ordering:** always the schedule lock first, then user locks. Every operation takes at most one
  schedule lock, so there can't be a cycle (no deadlock). During promotion the promoted user's lock is
  taken while holding the schedule lock, which follows the same order.

Locks are per class, so bookings for different classes don't wait on each other.

### 2. Database as the final guard

A Redis lock alone isn't a correctness guarantee. It can expire during a long GC pause, or be lost on a
Redis failover. So the database enforces the invariants too:

- **Optimistic concurrency tokens** on `timetable_schedules.BookedCount`, `user_packages.RemainingCredits`
  / `ReservedCredits`, and the `Status` columns. EF generates
  `UPDATE timetable_schedules SET BookedCount = 5 WHERE Id = 6 AND BookedCount = 4`. If someone else got there
  first the update matches 0 rows, EF throws, the whole transaction is rolled back and the client gets
  `409 CONCURRENT_UPDATE`.
- **Check constraints:** `BookedCount <= Capacity`, `RemainingCredits >= 0`, `ReservedCredits <= RemainingCredits`.
- **One `SaveChanges` per operation**, so slot, credits, booking row and ledger row commit atomically.

The unit tests prove both layers separately: 10 concurrent bookings with the lock give exactly 5 bookings,
and with the lock replaced by a no-op the concurrency tokens still prevent overbooking.

Result against the running stack (`scripts/concurrency-test.sh`):

```
user 1   -> HTTP 201 BOOKED
user 5   -> HTTP 201 BOOKED
user 6   -> HTTP 409 SCHEDULE_FULL
...
201 Created : 5
409 Conflict: 5
attendanceCount: 5
```

### Alternatives I considered

| Approach | Why not (as the main mechanism) |
|---|---|
| DB only: `SELECT ... FOR UPDATE` or a conditional `UPDATE ... WHERE BookedCount < Capacity` | Correct and simple, and honestly enough at this scale. But a hot class (big launch, 1000 people at 9:00) piles up row locks and connections on MySQL. The brief also asks for Redis. I kept the DB check as the safety net. |
| Optimistic concurrency only, with retries | Under heavy contention most requests fail and retry, which wastes work and gives a bad experience. |
| Redis atomic counter (Lua `DECR` on a slot counter) | Fastest, no waiting at all. But Redis then holds the "real" slot count, which has to be kept in sync with MySQL (cancellations, failed DB writes, Redis restarts). It's more moving parts than this needs, but it's where I'd go for flash-sale traffic (see scaling). |
| Redlock across several Redis nodes | Better lock availability, but more infrastructure and still not a correctness guarantee on its own. With the DB guard behind it, one Redis is enough here. |

---

## Caching

Redis is also used as a cache-aside layer (`RedisCacheService`):

- **Package catalogue:** cached 10 minutes. It only changes through deployments or admin tools in this design.
- **Timetable list:** cached 60 seconds, keyed by filters **and a version number**:
  `timetable:v{version}:b:{businessId}:d:{date}`. Every booking, cancellation, promotion, waitlist change
  and expiry runs `INCR version:timetable`, so the next read uses a new key and old entries just expire.
  That's simpler than finding and deleting every filter combination.
- `GET /api/timetable/{id}` is not cached. It's the "about to book" view and should be exact.

The cache never affects correctness. Bookings always read from MySQL under the lock. If Redis is down,
reads fall back to the database and only log a warning.

---

## Business rules and assumptions

How I handled the rules from the brief, plus the gaps I had to fill:

**Packages**

- `GET /api/packages` returns the **catalogue** (what can be bought). A user's own packages and credit
  balances are at `GET /api/users/{id}/packages`. I read "available packages" as "packages for sale".
- Purchase is mocked: it always succeeds and immediately creates a `UserPackage` with full credits and
  `ExpiresAt = now + ValidityDays`.
- A package is expired when `now >= ExpiresAt`. Expiry is checked at booking time. I don't require the
  package to still be valid on the class date. That's a business decision that could go either way.
- Users can have several packages. If `userPackageId` isn't given, the system picks a valid package for
  that business with available credit that **expires soonest**, so credits don't go to waste. Clients can
  still pass `userPackageId` explicitly.
- If a user only has packages for another business, the error is `PACKAGE_BUSINESS_MISMATCH`. If they have
  none at all, it's `NO_ELIGIBLE_PACKAGE`.

**Booking**

- Validated in this order: class not started → not already booked/waitlisted → package (business, expiry,
  credits) → no overlap → slot available.
- Overlap means `existing.Start < new.End && new.Start < existing.End`. Back-to-back classes
  (10:00-11:00 then 11:00-12:00) are allowed. Overlap is checked across all businesses, since one person
  can't be in two places at once.
- Only confirmed bookings count for overlap. Being on a waitlist doesn't block booking another class at
  the same time. It's re-checked on promotion instead.
- Classes can't be booked once they've started.

**Cancellation**

- Refund only if cancelled **strictly more than 4 hours** before the start. Exactly 4h counts as
  "within 4 hours", so no refund.
- A late cancellation still frees the slot, so the waitlist is still promoted.
- Can't cancel once the class has started.
- The refund goes back to the package that was used, even if that package has expired by then (the credit
  is then unusable, which matches how most studios handle it).
- A user can only cancel their own booking. Someone else's booking id returns 404, so ids aren't leaked.

**Waitlist**

- You can only join the waitlist when the class is full. If it has free slots you get `SCHEDULE_NOT_FULL`
  and should book directly. `POST /api/bookings` with `joinWaitlistIfFull: true` does both in one call.
- The brief says the credit is deducted on promotion and that the "reservation" is released if the class
  ends. So on joining, I **reserve** one credit (`ReservedCredits + 1`): it can't be spent on other classes,
  but it isn't deducted either. On promotion the reservation becomes a real debit. When the user leaves,
  is skipped, or the class ends, the reservation is released and nothing is deducted. This also guarantees
  a promoted user can always pay.
- FIFO order is `JoinedAt`, then `Id` as a tie breaker.
- On promotion the first user is re-validated. If their package has expired since joining, or they've since
  booked an overlapping class, they're marked `Skipped` (with a reason), their reservation is released,
  and the next person is tried.
- No promotions after the class has started (cancelling isn't possible then anyway). Remaining entries
  expire when the class ends.
- Promotion is automatic and immediate. There's no "you have 2 hours to accept" step.

**General**

- **No authentication.** `userId` in the request stands in for the authenticated user. In a real system it
  would come from the JWT, not the body.
- All times are UTC. The timetable `date` filter is a UTC date. A real system would store a time zone per
  business and filter by local date.
- Bookings and waitlist entries are never deleted, only their status changes. Every credit movement goes
  into `credit_transactions` for auditing.
- No pagination on the timetable since the dataset is small. In production I'd add a date range and paging.

---

## Tradeoffs

- **Redis is required for writes (fail closed).** If Redis is down, bookings return 503 instead of falling
  back to DB-only. The DB guard would still prevent overbooking, so falling back would be possible, but
  then we'd silently lose the user-level protection (double booking across classes) and the contention
  control. Read endpoints keep working without Redis.
- **Lock TTL vs long operations.** The lock TTL is 10s and the operation takes milliseconds. If it ever ran
  longer than the TTL the lock would be lost, but the concurrency tokens still prevent a bad write. The
  cost is one rejected request, not wrong data. A watchdog that extends the TTL would avoid even that.
- **`BookedCount` is denormalised.** It duplicates `COUNT(bookings WHERE Confirmed)`. That gives a cheap
  single-row check and something to put a concurrency token and check constraint on. The cost is keeping
  two things in sync, which only domain methods do (the seeder uses them too).
- **Concurrency failures aren't retried automatically.** `CONCURRENT_UPDATE` is returned to the client.
  With the lock in place it practically never happens, and a server-side retry loop would hide real problems.
- **Hangfire uses in-memory storage** so the project only needs MySQL + Redis. Jobs aren't persisted across
  restarts, which is fine here because the expiry job is a recurring, idempotent sweep. In production I'd
  use persistent storage (Redis or MySQL) so several API instances share one schedule.
- **Waitlist position is computed** (`COUNT` of entries ahead) rather than stored, so there's nothing to
  renumber when someone leaves.
- **Tests use SQLite**, not MySQL. The model, concurrency tokens and check constraints are the same, but
  MySQL-specific behaviour isn't covered. With more time I'd add integration tests with Testcontainers
  (MySQL + Redis) and `WebApplicationFactory`.

---

## Scaling in production

What I'd change, roughly in the order I'd need it:

1. **Stateless API, horizontal scaling.** The API keeps no state in memory (locks and cache are in Redis),
   so it can run behind a load balancer as is. Move Hangfire to persistent storage, or run jobs in a
   separate worker service.
2. **Database:** add read replicas for timetable and "my bookings" reads, keep writes on the primary.
   Partition or archive old bookings, waitlist entries and ledger rows by date. Shard by business if one
   cluster stops being enough, since everything already hangs off `BusinessId`.
3. **Redis:** use a managed, replicated Redis (Sentinel / Cluster). Lock keys are per schedule and user,
   so they spread well across cluster slots.
4. **Hot classes / flash sales:** move the slot count into Redis and claim slots with an atomic Lua script
   (no lock waiting), then write the booking to MySQL asynchronously through a queue with an outbox,
   reconciling counts periodically. Or put a short queue in front of very popular classes so requests are
   processed in order instead of competing for the lock.
5. **Events:** publish `BookingConfirmed`, `BookingCancelled`, `WaitlistPromoted` (outbox pattern) so
   notifications (email/push "you got a spot!"), analytics and studio dashboards don't sit on the booking
   path.
6. **Idempotency keys** on `POST /api/bookings` and `/purchase`, so mobile clients retrying on a bad
   network don't double book or double charge.
7. **Real payments:** create the package as `PendingPayment` and activate it from the payment provider's webhook.
8. **Observability:** metrics for lock wait time, `RESOURCE_BUSY` / `CONCURRENT_UPDATE` rates and
   booking latency, plus tracing across API → Redis → MySQL.
9. **Rate limiting** per user on booking endpoints, since one user hammering a class slows it down for everyone.

---

## Testing

`tests/Rezerv.UnitTests`: 39 xUnit tests.

- **Domain:** credit debit/reserve/convert/release/refund, expiry boundary, business mismatch, the 4h
  refund boundary (4h01m refund, exactly 4h no refund), overlap detection including back-to-back,
  full-class guard, waitlist entry state transitions.
- **Services** (against a real relational DB, SQLite, with the same EF model):
  booking success and every rejection path, auto package selection, refund and late cancellation,
  cancelling someone else's booking, FIFO promotion with credit deduction, skipping a user whose package
  expired, releasing reservations when a class ends (and idempotency of the job), reserved credits not
  being spendable elsewhere.
- **Concurrency:** 10 parallel bookings for 5 slots → exactly 5 confirmed. A stale write rejected by the
  concurrency token. Parallel bookings with **no lock at all** still never overbook.

```bash
dotnet test
```

## Project structure

```
src/
  Rezerv.Domain/           entities, enums, CancellationPolicy, DomainException + error codes
  Rezerv.Application/      Bookings/, Waitlist/, Packages/, Timetable/, Users/, Abstractions/
  Rezerv.Infrastructure/   Persistence/ (DbContext, configurations, migrations, seed), Redis/, Jobs/
  Rezerv.Api/              Controllers/, GlobalExceptionHandler, Program.cs
tests/
  Rezerv.UnitTests/        Domain/, Services/, Support/ (SQLite test DB, in-memory lock, fakes)
docs/                      DATABASE.md (ERD), schema.sql, SEED_DATA.md
scripts/                   concurrency-test.sh
```
