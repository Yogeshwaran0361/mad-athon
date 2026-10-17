<#
.SYNOPSIS
  Adds a batch of demo tasks through the public HTTP API so the task list and board look like a working team.
.DESCRIPTION
  Creates two more projects (HR Self-Service, Mobile App) with modules, then 20 tasks across them and the existing
  Customer Portal / Net Banking modules: some just assigned, some in progress with approved hours, some delivered,
  and two delivered tasks that QA reopens with a bug. Dates follow the server clock. Runs on Windows PowerShell 5.1
  and PowerShell 7. Needs the demo story (scripts/seed-demo.ps1) first, because Ravi and Meera are assignees.
  Safe to re-run: projects and tasks that already exist are reused, not duplicated.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\seed-more-tasks.ps1 -BaseUrl http://localhost:8080
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
  if ($line) { $SeedPassword = $line.Substring('SEED_PASSWORD='.Length).Trim() }
}
if (-not $SeedPassword) { throw 'Set SEED_PASSWORD (password of the seeded demo users) or create .env' }
if (-not $DemoUserPassword) { $DemoUserPassword = "$SeedPassword-Demo2026" }

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
      $detail = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }
      throw "Sign-in as $email failed: $detail"
    }
  }
}

$admin = Login 'admin@reworkguard.local' $SeedPassword
$existing = @((Call $admin GET '/api/v1/tasks?pageSize=100').items)

$employees = (Call $admin GET '/api/v1/admin/employees?pageSize=100').items
function EmployeeId([string]$email) {
  $match = $employees | Where-Object { $_.email -eq $email }
  if (-not $match) { throw "$email not found: run scripts/seed-demo.ps1 first" }
  $match.id
}
$people = @{
  priya = @{ id = EmployeeId 'priya@reworkguard.local'; token = Login 'priya@reworkguard.local' $SeedPassword }
  dev   = @{ id = EmployeeId 'dev@reworkguard.local'; token = Login 'dev@reworkguard.local' $SeedPassword }
  hana  = @{ id = EmployeeId 'hana@reworkguard.local'; token = Login 'hana@reworkguard.local' $SeedPassword }
  ravi  = @{ id = EmployeeId 'ravi@reworkguard.local'; token = Login 'ravi@reworkguard.local' $DemoUserPassword }
  meera = @{ id = EmployeeId 'meera@reworkguard.local'; token = Login 'meera@reworkguard.local' $DemoUserPassword }
}
$lead = Login 'lead@reworkguard.local' $SeedPassword
$qa = Login 'qa@reworkguard.local' $SeedPassword

# Dates follow the server's clock (the dev clock may have moved it), in IST (fixed UTC+05:30).
try { $nowUtc = ([datetime](Call $admin GET '/api/v1/dev/clock').utcNow).ToUniversalTime() } catch { $nowUtc = [datetime]::UtcNow }
$todayIst = $nowUtc.AddMinutes(330).Date
function Day([int]$offset) { $todayIst.AddDays($offset).ToString('yyyy-MM-dd') }
Write-Host "Server today (IST): $(Day 0)"

# There is no project list endpoint, so a project is recognised by its module names (ids are sequential).
function ProjectModules([string]$name, [int]$warrantyDays, [string[]]$moduleNames) {
  for ($id = 1; $id -le 100; $id++) {
    try { $found = @(Invoke-RestMethod -Uri "$BaseUrl/api/v1/admin/projects/$id/modules" -Headers @{ Authorization = "Bearer $admin" }) }
    catch { break }
    $found = @($found | ForEach-Object { $_ })  # Windows PowerShell 5.1 hands back a JSON array as one item
    if (@($found | ForEach-Object { $_.name }) -contains $moduleNames[0]) { return $found }
  }
  $project = Call $admin POST '/api/v1/admin/projects' @{ name = $name; warrantyDays = $warrantyDays }
  Write-Host "Project $name #$($project.projectId)"
  $moduleNames | ForEach-Object { Call $admin POST "/api/v1/admin/projects/$($project.projectId)/modules" @{ name = $_ } }
}
$hrModules = @(ProjectModules 'HR Self-Service' 60 'Leave', 'Attendance', 'Payslip viewer')
$mobileModules = @(ProjectModules 'Mobile App' 30 'Onboarding', 'Push notifications')
function ModuleId($list, [string]$name) { ($list | Where-Object { $_.name -eq $name }).id }
$modules = @{
  checkout = 1; invoicing = 2; login = 3
  leave = ModuleId $hrModules 'Leave'
  attendance = ModuleId $hrModules 'Attendance'
  payslipViewer = ModuleId $hrModules 'Payslip viewer'
  onboarding = ModuleId $mobileModules 'Onboarding'
  notifications = ModuleId $mobileModules 'Push notifications'
}

# state: Assigned | InProgress | Completed | Bug (completed, then reopened by QA). log = hours per assignee per day.
$plan = @(
  @{ module = 'leave'; title = 'Leave: apply and cancel leave'; due = 10; who = 'priya', 'hana'; state = 'InProgress'; log = 3; days = 2 }
  @{ module = 'leave'; title = 'Leave: balance carry-forward rules'; due = 14; who = 'dev'; state = 'Assigned' }
  @{ module = 'leave'; title = 'Leave: manager approval inbox'; due = 2; who = 'ravi', 'meera'; state = 'Completed'; log = 2; days = 3 }
  @{ module = 'attendance'; title = 'Attendance: biometric sync job'; due = 7; who = 'dev'; state = 'InProgress'; log = 4; days = 2 }
  @{ module = 'attendance'; title = 'Attendance: monthly summary report'; due = 1; who = 'hana'; state = 'Completed'; log = 3; days = 2 }
  @{ module = 'attendance'; title = 'Attendance: regularisation requests'; due = 18; who = 'priya'; state = 'Assigned' }
  @{ module = 'payslipViewer'; title = 'Payslip viewer: PDF download'; due = 0; who = 'meera'; state = 'Bug'; log = 3; days = 2; bug = 'PDF shows the previous month for employees who joined mid-month'; severity = 'High' }
  @{ module = 'payslipViewer'; title = 'Payslip viewer: tax breakup panel'; due = 12; who = 'ravi'; state = 'InProgress'; log = 2; days = 3 }
  @{ module = 'onboarding'; title = 'Onboarding: welcome carousel'; due = 3; who = 'priya'; state = 'Completed'; log = 2; days = 2 }
  @{ module = 'onboarding'; title = 'Onboarding: phone number OTP'; due = 9; who = 'ravi', 'dev'; state = 'InProgress'; log = 2; days = 2 }
  @{ module = 'onboarding'; title = 'Onboarding: profile photo upload'; due = 21; who = 'hana'; state = 'Assigned' }
  @{ module = 'notifications'; title = 'Push: payment reminder notifications'; due = 1; who = 'dev'; state = 'Bug'; log = 3; days = 2; bug = 'Reminder is sent twice when the app is reinstalled'; severity = 'Medium' }
  @{ module = 'notifications'; title = 'Push: notification preferences screen'; due = 15; who = 'meera'; state = 'Assigned' }
  @{ module = 'checkout'; title = 'Checkout: UPI intent payments'; due = 6; who = 'priya', 'meera'; state = 'InProgress'; log = 2; days = 2 }
  @{ module = 'checkout'; title = 'Checkout: saved cards with tokenisation'; due = 2; who = 'hana'; state = 'Completed'; log = 2; days = 3 }
  @{ module = 'checkout'; title = 'Checkout: coupon code validation'; due = 20; who = 'dev'; state = 'Assigned' }
  @{ module = 'invoicing'; title = 'Invoicing: GST e-invoice upload'; due = 8; who = 'hana'; state = 'InProgress'; log = 2; days = 2 }
  @{ module = 'invoicing'; title = 'Invoicing: credit note generation'; due = 1; who = 'ravi'; state = 'Completed'; log = 3; days = 2 }
  @{ module = 'login'; title = 'Login: device trust and remember me'; due = 11; who = 'meera'; state = 'InProgress'; log = 2; days = 2 }
  @{ module = 'login'; title = 'Login: account lockout emails'; due = 25; who = 'priya'; state = 'Assigned' }
)

# Each person's log days start further back as they get more tasks, so nobody passes the daily-hours limit.
$nextDay = @{ priya = -12; dev = -12; hana = -12; ravi = -12; meera = -12 }
$counts = @{ Assigned = 0; InProgress = 0; Completed = 0; Bug = 0 }
foreach ($t in $plan) {
  $who = @($t.who)
  $done = $existing | Where-Object { $_.title -eq $t.title } | Select-Object -First 1
  # Re-runs skip finished tasks; a task left at Assigned by an interrupted run continues from there.
  if ($done -and ($t.state -eq 'Assigned' -or $done.status -ne 'Assigned')) {
    if ($t.state -ne 'Assigned') { $who | ForEach-Object { $nextDay[$_] += 1 } }
    $counts[$t.state]++
    continue
  }
  $assignees = @($who | ForEach-Object { $people[$_].id })
  $committed = Day $t.due  # the API refuses a committed date in the past
  $task = if ($done) { $done } else { Call $lead POST '/api/v1/tasks' @{ moduleId = $modules[$t.module]; title = $t.title; committedDate = $committed; assigneeIds = $assignees } }
  $owner = $people[$who[0]]
  if ($t.state -ne 'Assigned') {
    Call $owner.token POST "/api/v1/tasks/$($task.id)/transitions" @{ targetStatus = 'InProgress'; reason = 'Started' } | Out-Null
    foreach ($name in $who) {
      for ($d = 0; $d -lt $t.days; $d++) {
        $log = Call $people[$name].token POST '/api/v1/timelogs' @{ taskId = $task.id; workDate = (Day ($nextDay[$name] + $d)); hours = $t.log; workType = 'Regular'; bugId = $null; note = $t.title }
        Call $lead POST "/api/v1/timelogs/$($log.id)/approve" | Out-Null
      }
      $nextDay[$name] += 1
    }
  }
  if ($t.state -in 'Completed', 'Bug') {
    Call $owner.token POST "/api/v1/tasks/$($task.id)/transitions" @{ targetStatus = 'Completed'; reason = 'Delivered to QA' } | Out-Null
  }
  if ($t.state -eq 'Bug') {
    Call $qa POST "/api/v1/tasks/$($task.id)/bugs" @{ severity = $t.severity; description = $t.bug; foundDate = (Day 0) } | Out-Null
  }
  $counts[$t.state]++
  Write-Host ("  #{0} {1} [{2}]" -f $task.id, $t.title, $t.state)
}

Write-Host ("Demo task batch in place, {0} tasks: {1} assigned, {2} in progress, {3} completed, {4} reopened with a bug." -f $plan.Count, $counts.Assigned, $counts.InProgress, $counts.Completed, $counts.Bug)
