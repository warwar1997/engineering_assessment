#!/usr/bin/env bash
# Fires 10 booking requests at the same time for "Strength Lab" (schedule 6, 5 slots).
# Expected: exactly 5 x 201 Booked, 5 x 409 SCHEDULE_FULL, and attendanceCount = 5.
#
# Usage: ./scripts/concurrency-test.sh [baseUrl] [scheduleId]
# Run against a freshly seeded DB (docker compose down -v && docker compose up -d --build).

set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
SCHEDULE_ID="${2:-6}"
USERS=(1 5 6 7 8 9 10 11 12 13)   # all have valid Rezerv Fitness credits and no clash at that time
OUT_DIR="$(mktemp -d)"

echo "Booking schedule $SCHEDULE_ID with ${#USERS[@]} concurrent users..."

for u in "${USERS[@]}"; do
  curl -s -o "$OUT_DIR/$u.json" -w "%{http_code}" \
    -H "Content-Type: application/json" \
    -X POST "$BASE_URL/api/bookings" \
    -d "{\"userId\": $u, \"scheduleId\": $SCHEDULE_ID}" > "$OUT_DIR/$u.status" &
done
wait

for u in "${USERS[@]}"; do
  status=$(cat "$OUT_DIR/$u.status")
  code=$(grep -o '"code":"[A-Z_]*"' "$OUT_DIR/$u.json" | cut -d'"' -f4 || true)
  printf "user %-3s -> HTTP %s %s\n" "$u" "$status" "${code:-BOOKED}"
done

echo
echo "201 Created : $(grep -l '^201$' "$OUT_DIR"/*.status | wc -l | tr -d ' ')"
echo "409 Conflict: $(grep -l '^409$' "$OUT_DIR"/*.status | wc -l | tr -d ' ')"
echo
echo "Schedule state:"
curl -s "$BASE_URL/api/timetable/$SCHEDULE_ID"
echo
rm -rf "$OUT_DIR"
