#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Seeds the "Ravi Login Module" demo story through the public HTTP API (PowerShell 7 port of seed-demo.sh).
.DESCRIPTION
  Admin creates Ravi + Meera, a project and its Login module; the lead plans the task; Ravi and Meera deliver it;
  QA reports a bug inside warranty; Ravi logs corrective hours; the lead approves; the manager reviews (OwnDefect);
  lines are notified; Ravi disputes and the manager upholds it with fewer hours; the Development-only clock control
  moves past the dispute window; lines are approved; payroll is generated, reviewed, approved, finalized and locked.
  Idempotent: exits 0 without changes when ravi@reworkguard.local already exists. Replaying needs a fresh database.
.EXAMPLE
  ./scripts/seed-demo.ps1 -BaseUrl http://localhost:8080
#>
param(
  [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:8080' }),
  [string]$SeedPassword = $env:SEED_PASSWORD,
  [string]$DemoUserPassword = $env:DEMO_USER_PASSWORD
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if (-not $SeedPassword -and (Test-Path "$root/.env")) {
  $line = Get-Content "$root/.env" | Where-Object { $_ -match '^SEED_PASSWORD=' } | Select-Object -First 1
  if ($line) { $SeedPassword = $line.Substring('SEED_PASSWORD='.Length) }
}
if (-not $SeedPassword) { throw 'Set SEED_PASSWORD (password of the seeded demo users) or create .env' }
if (-not $DemoUserPassword) { $DemoUserPassword = "$SeedPassword-Demo2026" }

function Step([string]$text) { Write-Host "`n== $text" }

function Call([string]$token, [string]$method, [string]$path, $body = $null) {
  $headers = @{ Accept = 'application/json' }
  if ($token) { $headers.Authorization = "Bearer $token" }
  $params = @{ Method = $method; Uri = "$BaseUrl$path"; Headers = $headers }
  if ($null -ne $body) { $params.Body = ($body | ConvertTo-Json -Depth 10 -Compress); $params.ContentType = 'application/json' }
  try { Invoke-RestMethod @params }
  catch {
    $detail = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }
    throw "$method $path failed: $detail"
  }
}

# The API allows 10 sign-ins per minute per client address, so wait and retry on HTTP 429.
function Login([string]$email, [string]$password) {
  for ($attempt = 1; ; $attempt++) {
    try {
      $response = Invoke-RestMethod -Method POST -Uri "$BaseUrl/api/v1/auth/login" -ContentType 'application/json' -Body (@{ email = $email; password = $password } | ConvertTo-Json -Compress)
      return $response.accessToken
    }
    catch {
      if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 429 -and $attempt -lt 8) {
        Write-Host "  sign-in rate limit reached; retrying $email in 15 s"
        Start-Sleep -Seconds 15
        continue
      }
      throw "Sign-in as $email failed: $($_.ErrorDetails.Message ?? $_.Exception.Message)"
    }
  }
}

Step 'Health'
Invoke-WebRequest "$BaseUrl/health/ready" -UseBasicParsing | Out-Null
Write-Host "API ready at $BaseUrl"

Step 'Sign in as the seeded staff (admin, lead, QA, manager, finance)'
$admin = Login 'admin@reworkguard.local' $SeedPassword
$employees = (Call $admin GET '/api/v1/admin/employees?pageSize=100').items
if ($employees | Where-Object { $_.email -eq 'ravi@reworkguard.local' }) {
  Write-Host 'Demo story already seeded (ravi@reworkguard.local exists). Nothing to do; use a fresh database to replay it.'
  exit 0
}
$lead = Login 'lead@reworkguard.local' $SeedPassword
$qa = Login 'qa@reworkguard.local' $SeedPassword
$manager = Login 'manager@reworkguard.local' $SeedPassword
$finance = Login 'finance@reworkguard.local' $SeedPassword
$leadId = ($employees | Where-Object { $_.email -eq 'lead@reworkguard.local' }).id

# Dates follow the server's clock (the dev clock may have moved it), in the work time zone (Asia/Kolkata).
$clockControl = $true
try { $nowUtc = ([datetime](Call $admin GET '/api/v1/dev/clock').utcNow).ToUniversalTime() }
catch { $clockControl = $false; $nowUtc = [datetime]::UtcNow; Write-Host 'Dev clock control is not available: the story will stop before the dispute window closes.' }
# India Standard Time is a fixed UTC+05:30 (no daylight saving), so no tz database is needed.
$todayIst = $nowUtc.AddMinutes(330).Date
$today = $todayIst.ToString('yyyy-MM-dd')
$yesterday = $todayIst.AddDays(-1).ToString('yyyy-MM-dd')
$payFrom = $todayIst.AddDays(-40).ToString('yyyy-MM-01')
Write-Host "Server today (IST): $today"

Step 'Admin creates Ravi (monthly 60,000) and Meera (hourly 300), both reporting to Tara Lead'
$ravi = Call $admin POST '/api/v1/admin/employees' @{ code = 'EMP101'; name = 'Ravi Kumar'; email = 'ravi@reworkguard.local'; role = 'Employee'; managerId = $leadId; payMode = 'Monthly'; monthlyGross = 60000; dailyRate = $null; hourlyRate = $null; payEffectiveFrom = $payFrom; password = $DemoUserPassword }
$meera = Call $admin POST '/api/v1/admin/employees' @{ code = 'EMP102'; name = 'Meera Iyer'; email = 'meera@reworkguard.local'; role = 'Employee'; managerId = $leadId; payMode = 'Hourly'; monthlyGross = $null; dailyRate = $null; hourlyRate = 300; payEffectiveFrom = $payFrom; password = $DemoUserPassword }
Write-Host "Ravi #$($ravi.id), Meera #$($meera.id)"
$raviToken = Login 'ravi@reworkguard.local' $DemoUserPassword
$meeraToken = Login 'meera@reworkguard.local' $DemoUserPassword

Step 'Admin creates the project and its Login module'
$project = Call $admin POST '/api/v1/admin/projects' @{ name = 'Net Banking Portal'; warrantyDays = 45 }
$module = Call $admin POST "/api/v1/admin/projects/$($project.projectId)/modules" @{ name = 'Login' }
Write-Host "Project #$($project.projectId), module Login #$($module.id)"

Step 'Lead plans the Login module task for Ravi and Meera'
$task = Call $lead POST '/api/v1/tasks' @{ moduleId = $module.id; title = 'Login module: OTP sign-in'; committedDate = $today; assigneeIds = @($ravi.id, $meera.id) }
$taskId = $task.id
Call $raviToken POST "/api/v1/tasks/$taskId/transitions" @{ targetStatus = 'InProgress'; reason = 'Started' } | Out-Null
Write-Host "Task #$taskId in progress"

Step 'Ravi logs 6 h and Meera 2 h of regular work; the lead approves both'
$raviLog = Call $raviToken POST '/api/v1/timelogs' @{ taskId = $taskId; workDate = $yesterday; hours = 6; workType = 'Regular'; bugId = $null; note = 'OTP flow and session handling' }
$meeraLog = Call $meeraToken POST '/api/v1/timelogs' @{ taskId = $taskId; workDate = $yesterday; hours = 2; workType = 'Regular'; bugId = $null; note = 'Login form validation' }
Call $lead POST "/api/v1/timelogs/$($raviLog.id)/approve" | Out-Null
Call $lead POST "/api/v1/timelogs/$($meeraLog.id)/approve" | Out-Null

Step 'Ravi delivers the task'
$detail = Call $raviToken POST "/api/v1/tasks/$taskId/transitions" @{ targetStatus = 'Completed'; reason = 'Delivered to QA' }
Write-Host "Task status: $($detail.task.status)"

Step 'QA reports a bug inside the warranty window'
$bug = Call $qa POST "/api/v1/tasks/$taskId/bugs" @{ severity = 'High'; description = 'Login accepts an expired OTP'; foundDate = $today }
$bugId = $bug.bug.id
Write-Host "Bug #$bugId, warranty ends $($bug.warrantyEndsOn)"

Step 'Ravi logs 4 h of corrective work; the lead approves it'
$fix = Call $raviToken POST '/api/v1/timelogs' @{ taskId = $taskId; workDate = $today; hours = 4; workType = 'Corrective'; bugId = $bugId; note = 'Reject expired OTPs' }
Call $lead POST "/api/v1/timelogs/$($fix.id)/approve" | Out-Null

Step 'Manager reviews the bug as OwnDefect caused by the original delivery'
Call $manager POST "/api/v1/bugs/$bugId/review" @{ rootCause = 'OwnDefect'; causedByOriginalDelivery = $true; justification = 'Expiry check missing in the original delivery' } | Out-Null

Step 'Deduction lines (Draft) are notified to Ravi and Meera'
$drafts = (Call $manager GET "/api/v1/deductions?pageSize=100&bugId=$bugId&status=Draft").items
$drafts | ForEach-Object { Write-Host "  line #$($_.id) $($_.employeeName): $($_.hours) h x $($_.hourlyRateUsed) x $($_.sharePercent)% = $($_.amount) (apply $($_.applyMonth))" }
Call $manager POST '/api/v1/deductions/notify' @{ deductionIds = @($drafts | ForEach-Object { $_.id }) } | Out-Null
$raviLine = ($drafts | Where-Object { $_.employeeId -eq $ravi.id }).id
$applyMonth = $drafts[0].applyMonth

Step 'Ravi disputes his line; the manager upholds it with 3 h instead of 4 h'
Call $raviToken POST "/api/v1/deductions/$raviLine/dispute" @{ reason = 'One hour was spent on a shared config issue, not my defect' } | Out-Null
Call $manager POST "/api/v1/deductions/$raviLine/dispute/resolve" @{ outcome = 'Upheld'; justification = 'Config hour confirmed in the build log'; newHours = 3; newSharePercent = $null } | Out-Null

$lines = (Call $manager GET "/api/v1/deductions?pageSize=100&bugId=$bugId").items
$lines | ForEach-Object { Write-Host "  line #$($_.id) $($_.employeeName): $($_.status) $($_.amount)" }
$notified = @($lines | Where-Object { $_.status -eq 'Notified' })
if ($notified.Count -gt 0) {
  $windowEnd = ($notified | ForEach-Object { ([datetime]$_.disputeWindowEndsAtUtc).ToUniversalTime() } | Measure-Object -Maximum).Maximum
  if (-not $clockControl) { Write-Error "Meera's line stays Notified until $windowEnd UTC; approve it and run payroll after that."; exit 2 }
  Step "Dev clock moves past the dispute window ($($windowEnd.ToString('u')))"
  $current = ([datetime](Call $admin GET '/api/v1/dev/clock').utcNow).ToUniversalTime()
  $hours = [math]::Max(1, [math]::Floor(($windowEnd - $current).TotalHours) + 1)
  $moved = Call $admin POST '/api/v1/dev/clock/advance' @{ hours = $hours }
  Write-Host "Server clock now $($moved.utcNow) (offset $($moved.offsetHours) h)"
}

Step 'Manager approves the lines'
$ids = @((Call $manager GET "/api/v1/deductions?pageSize=100&bugId=$bugId").items | Where-Object { $_.status -in 'Notified', 'Resolved' } | ForEach-Object { $_.id })
(Call $manager POST '/api/v1/deductions/approve' @{ deductionIds = $ids }) | ForEach-Object { Write-Host "  line #$($_.id) $($_.employeeName): $($_.status) $($_.amount)" }

Step "Payroll for ${applyMonth}: generate -> review -> approve -> finalize -> lock"
$run = Call $finance POST '/api/v1/payroll/runs' @{ periodMonth = $applyMonth }
foreach ($pair in @(@($finance, 'review'), @($manager, 'approve'), @($finance, 'finalize'), @($finance, 'lock'))) {
  $run = Call $pair[0] POST "/api/v1/payroll/runs/$($run.run.id)/$($pair[1])" @{ expectedVersion = $run.run.version }
  Write-Host "  $($pair[1]) -> $($run.run.status)"
}

Step 'Done'
Write-Host "Payroll run #$($run.run.id) for $($run.run.periodMonth): $($run.run.status)"
Write-Host "Sign in as ravi@reworkguard.local (password from DEMO_USER_PASSWORD) to see the story from Ravi's side."
