#!/usr/bin/env bash
# Seeds the "Ravi Login Module" demo story through the public HTTP API (no direct SQL):
#   admin creates Ravi + Meera, a project and its Login module -> lead plans the task -> Ravi and Meera deliver it
#   -> QA reports a bug inside warranty -> Ravi logs corrective hours -> lead approves -> manager reviews (OwnDefect)
#   -> deduction lines notified -> Ravi disputes, manager upholds it with fewer hours -> dev clock passes the dispute
#   window -> lines approved -> payroll run generated, reviewed, approved, finalized and locked.
#
# Needs: curl, jq; a running stack whose demo users were seeded (Database:SeedOnStartup) and, to get past the
# dispute window, the Development-only clock control (DevTools:ClockControl=true; docker-compose.yml enables it).
# Idempotent: if ravi@reworkguard.local already exists it prints a notice and exits 0. To replay the story, start
# from a fresh database (docker compose down -v && docker compose up --build).
#
# Env: BASE_URL (default http://localhost:8080, the web container), SEED_PASSWORD (read from ../.env when unset),
#      DEMO_USER_PASSWORD (password for Ravi and Meera; default "${SEED_PASSWORD}-Demo2026").
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
if [[ -z "${SEED_PASSWORD:-}" && -f "$root/.env" ]]; then
  SEED_PASSWORD="$(grep -E '^SEED_PASSWORD=' "$root/.env" | head -n1 | cut -d= -f2-)"
fi
: "${SEED_PASSWORD:?Set SEED_PASSWORD (password of the seeded demo users) or create .env}"
BASE_URL="${BASE_URL:-http://localhost:8080}"
DEMO_USER_PASSWORD="${DEMO_USER_PASSWORD:-${SEED_PASSWORD}-Demo2026}"
command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }

step() { printf '\n== %s\n' "$*"; }

# call TOKEN METHOD PATH [JSON] -> prints body; fails loudly on non-2xx.
call() {
  local token="$1" method="$2" path="$3" body="${4:-}" out status
  out="$(mktemp)"
  local args=(-sS -o "$out" -w '%{http_code}' -X "$method" "$BASE_URL$path" -H 'Accept: application/json')
  [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
  [[ -n "$body" ]] && args+=(-H 'Content-Type: application/json' --data "$body")
  status="$(curl "${args[@]}")"
  if [[ "$status" != 2* ]]; then
    echo "!! $method $path -> HTTP $status" >&2
    cat "$out" >&2; echo >&2
    rm -f "$out"
    return 1
  fi
  cat "$out"; rm -f "$out"
}

# The API allows 10 sign-ins per minute per client address, so wait and retry on HTTP 429.
login() {
  local body status out attempt
  body="$(jq -nc --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"
  out="$(mktemp)"
  for attempt in 1 2 3 4 5 6 7 8; do
    status="$(curl -sS -o "$out" -w '%{http_code}' -X POST "$BASE_URL/api/v1/auth/login" -H 'Content-Type: application/json' --data "$body")"
    if [[ "$status" == 429 ]]; then
      echo "  sign-in rate limit reached; retrying $1 in 15 s" >&2
      sleep 15
      continue
    fi
    break
  done
  if [[ "$status" != 200 ]]; then
    echo "!! sign-in as $1 -> HTTP $status" >&2; cat "$out" >&2; echo >&2; rm -f "$out"; return 1
  fi
  jq -r .accessToken "$out"; rm -f "$out"
}

step "Health"
curl -fsS "$BASE_URL/health/ready" >/dev/null && echo "API ready at $BASE_URL"

step "Sign in as the seeded staff (admin, lead, QA, manager, finance)"
admin="$(login admin@reworkguard.local "$SEED_PASSWORD")"
employees="$(call "$admin" GET '/api/v1/admin/employees?pageSize=100')"
if jq -e '(.items // .) | any(.email == "ravi@reworkguard.local")' <<<"$employees" >/dev/null; then
  echo "Demo story already seeded (ravi@reworkguard.local exists). Nothing to do; use a fresh database to replay it."
  exit 0
fi
lead="$(login lead@reworkguard.local "$SEED_PASSWORD")"
qa="$(login qa@reworkguard.local "$SEED_PASSWORD")"
manager="$(login manager@reworkguard.local "$SEED_PASSWORD")"
finance="$(login finance@reworkguard.local "$SEED_PASSWORD")"
lead_id="$(jq -r '(.items // .)[] | select(.email == "lead@reworkguard.local") | .id' <<<"$employees")"

# Dates follow the server's clock (which the dev clock may have moved), in the work time zone (Asia/Kolkata).
clock="$(call "$admin" GET /api/v1/dev/clock 2>/dev/null || true)"
if [[ -n "$clock" ]]; then
  now_utc="$(jq -r .utcNow <<<"$clock")"
  clock_control=1
else
  now_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  clock_control=0
  echo "Dev clock control is not available: the story will stop before the dispute window closes."
fi
now_epoch="$(date -u -d "${now_utc%%.*}Z" +%s 2>/dev/null || date -u -d "${now_utc}" +%s)"
today="$(TZ=Asia/Kolkata date -d "@$now_epoch" +%F)"
yesterday="$(TZ=Asia/Kolkata date -d "@$((now_epoch - 86400))" +%F)"
pay_from="$(TZ=Asia/Kolkata date -d "@$((now_epoch - 40 * 86400))" +%Y-%m-01)"
echo "Server today (IST): $today"

step "Admin creates Ravi (monthly 60,000) and Meera (hourly 300), both reporting to Tara Lead"
ravi_id="$(call "$admin" POST /api/v1/admin/employees "$(jq -nc --argjson m "$lead_id" --arg d "$pay_from" --arg p "$DEMO_USER_PASSWORD" \
  '{code:"EMP101",name:"Ravi Kumar",email:"ravi@reworkguard.local",role:"Employee",managerId:$m,payMode:"Monthly",monthlyGross:60000,dailyRate:null,hourlyRate:null,payEffectiveFrom:$d,password:$p}')" | jq -r .id)"
meera_id="$(call "$admin" POST /api/v1/admin/employees "$(jq -nc --argjson m "$lead_id" --arg d "$pay_from" --arg p "$DEMO_USER_PASSWORD" \
  '{code:"EMP102",name:"Meera Iyer",email:"meera@reworkguard.local",role:"Employee",managerId:$m,payMode:"Hourly",monthlyGross:null,dailyRate:null,hourlyRate:300,payEffectiveFrom:$d,password:$p}')" | jq -r .id)"
echo "Ravi #$ravi_id, Meera #$meera_id"
ravi="$(login ravi@reworkguard.local "$DEMO_USER_PASSWORD")"
meera="$(login meera@reworkguard.local "$DEMO_USER_PASSWORD")"

step "Admin creates the project and its Login module"
project_id="$(call "$admin" POST /api/v1/admin/projects '{"name":"Net Banking Portal","warrantyDays":45}' | jq -r .projectId)"
module_id="$(call "$admin" POST "/api/v1/admin/projects/$project_id/modules" '{"name":"Login"}' | jq -r .id)"
echo "Project #$project_id, module Login #$module_id"

step "Lead plans the Login module task for Ravi and Meera"
task_id="$(call "$lead" POST /api/v1/tasks "$(jq -nc --argjson m "$module_id" --arg d "$today" --argjson r "$ravi_id" --argjson e "$meera_id" \
  '{moduleId:$m,title:"Login module: OTP sign-in",committedDate:$d,assigneeIds:[$r,$e]}')" | jq -r .id)"
call "$ravi" POST "/api/v1/tasks/$task_id/transitions" '{"targetStatus":"InProgress","reason":"Started"}' >/dev/null
echo "Task #$task_id in progress"

step "Ravi logs 6 h and Meera 2 h of regular work; the lead approves both"
ravi_log="$(call "$ravi" POST /api/v1/timelogs "$(jq -nc --argjson t "$task_id" --arg d "$yesterday" '{taskId:$t,workDate:$d,hours:6,workType:"Regular",bugId:null,note:"OTP flow and session handling"}')" | jq -r .id)"
meera_log="$(call "$meera" POST /api/v1/timelogs "$(jq -nc --argjson t "$task_id" --arg d "$yesterday" '{taskId:$t,workDate:$d,hours:2,workType:"Regular",bugId:null,note:"Login form validation"}')" | jq -r .id)"
call "$lead" POST "/api/v1/timelogs/$ravi_log/approve" >/dev/null
call "$lead" POST "/api/v1/timelogs/$meera_log/approve" >/dev/null

step "Ravi delivers the task"
call "$ravi" POST "/api/v1/tasks/$task_id/transitions" '{"targetStatus":"Completed","reason":"Delivered to QA"}' | jq -r '"Task status: \(.task.status)"'

step "QA reports a bug inside the warranty window"
bug="$(call "$qa" POST "/api/v1/tasks/$task_id/bugs" "$(jq -nc --arg d "$today" '{severity:"High",description:"Login accepts an expired OTP",foundDate:$d}')")"
bug_id="$(jq -r .bug.id <<<"$bug")"
jq -r '"Bug #\(.bug.id), warranty ends \(.warrantyEndsOn)"' <<<"$bug"

step "Ravi logs 4 h of corrective work; the lead approves it"
fix_log="$(call "$ravi" POST /api/v1/timelogs "$(jq -nc --argjson t "$task_id" --argjson b "$bug_id" --arg d "$today" '{taskId:$t,workDate:$d,hours:4,workType:"Corrective",bugId:$b,note:"Reject expired OTPs"}')" | jq -r .id)"
call "$lead" POST "/api/v1/timelogs/$fix_log/approve" >/dev/null

step "Manager reviews the bug as OwnDefect caused by the original delivery"
call "$manager" POST "/api/v1/bugs/$bug_id/review" '{"rootCause":"OwnDefect","causedByOriginalDelivery":true,"justification":"Expiry check missing in the original delivery"}' >/dev/null

step "Deduction lines (Draft) are notified to Ravi and Meera"
drafts="$(call "$manager" GET "/api/v1/deductions?pageSize=100&bugId=$bug_id&status=Draft")"
jq -r '.items[] | "  line #\(.id) \(.employeeName): \(.hours) h x \(.hourlyRateUsed) x \(.sharePercent)% = \(.amount) (apply \(.applyMonth))"' <<<"$drafts"
ids="$(jq -c '[.items[].id]' <<<"$drafts")"
call "$manager" POST /api/v1/deductions/notify "{\"deductionIds\":$ids}" >/dev/null
ravi_line="$(jq -r --argjson r "$ravi_id" '.items[] | select(.employeeId == $r) | .id' <<<"$drafts")"
apply_month="$(jq -r '.items[0].applyMonth' <<<"$drafts")"

step "Ravi disputes his line; the manager upholds it with 3 h instead of 4 h"
call "$ravi" POST "/api/v1/deductions/$ravi_line/dispute" '{"reason":"One hour was spent on a shared config issue, not my defect"}' >/dev/null
call "$manager" POST "/api/v1/deductions/$ravi_line/dispute/resolve" '{"outcome":"Upheld","justification":"Config hour confirmed in the build log","newHours":3,"newSharePercent":null}' >/dev/null

lines="$(call "$manager" GET "/api/v1/deductions?pageSize=100&bugId=$bug_id")"
jq -r '.items[] | "  line #\(.id) \(.employeeName): \(.status) \(.amount)"' <<<"$lines"
window_end="$(jq -r '[.items[] | select(.status == "Notified") | .disputeWindowEndsAtUtc] | max // empty' <<<"$lines")"

if [[ -n "$window_end" ]]; then
  if [[ "$clock_control" != 1 ]]; then
    echo "Meera's line stays Notified until its dispute window ends at $window_end UTC; approve it and run payroll after that." >&2
    exit 2
  fi
  step "Dev clock moves past the dispute window ($window_end UTC)"
  current="$(call "$admin" GET /api/v1/dev/clock | jq -r .utcNow)"
  end_epoch="$(date -u -d "${window_end%%.*}Z" +%s)"
  cur_epoch="$(date -u -d "${current%%.*}Z" +%s)"
  hours=$(( (end_epoch - cur_epoch) / 3600 + 1 ))
  (( hours < 1 )) && hours=1
  call "$admin" POST /api/v1/dev/clock/advance "{\"hours\":$hours}" | jq -r '"Server clock now \(.utcNow) (offset \(.offsetHours) h)"'
fi

step "Manager approves the lines"
approvable="$(call "$manager" GET "/api/v1/deductions?pageSize=100&bugId=$bug_id" | jq -c '[.items[] | select(.status == "Notified" or .status == "Resolved") | .id]')"
call "$manager" POST /api/v1/deductions/approve "{\"deductionIds\":$approvable}" | jq -r '.[] | "  line #\(.id) \(.employeeName): \(.status) \(.amount)"'

step "Payroll for $apply_month: generate -> review -> approve -> finalize -> lock"
run="$(call "$finance" POST /api/v1/payroll/runs "{\"periodMonth\":\"$apply_month\"}")"
run_id="$(jq -r .run.id <<<"$run")"
for pair in "$finance:review" "$manager:approve" "$finance:finalize" "$finance:lock"; do
  who="${pair%%:*}"; action="${pair##*:}"
  version="$(jq -r .run.version <<<"$run")"
  run="$(call "$who" POST "/api/v1/payroll/runs/$run_id/$action" "{\"expectedVersion\":$version}")"
  echo "  $action -> $(jq -r .run.status <<<"$run")"
done

step "Done"
jq -r '"Payroll run #\(.run.id) for \(.run.periodMonth): \(.run.status)"' <<<"$run"
echo "Sign in as ravi@reworkguard.local (password from DEMO_USER_PASSWORD) to see the story from Ravi's side."
