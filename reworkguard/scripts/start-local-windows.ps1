# Runs ReworkGuard on Windows without Docker: portable MySQL 8 on port 3307,
# the API on http://localhost:5081 and the web app on http://localhost:8080.
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File scripts\start-local-windows.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$mysqlBase = Join-Path $env:USERPROFILE 'reworkguard-mysql'
$logs = Join-Path $root '.local-logs'
New-Item -ItemType Directory -Force $logs | Out-Null

$envs = @{}
Get-Content (Join-Path $root '.env') | Where-Object { $_ -match '^[A-Z_]+=' } | ForEach-Object {
    $k, $v = $_.Split('=', 2); $envs[$k] = $v.Trim()
}

function Wait-Port($port, $seconds) {
    for ($i = 0; $i -lt $seconds; $i++) {
        if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { return $true }
        Start-Sleep 1
    }
    return $false
}

if (-not (Get-NetTCPConnection -LocalPort 3307 -State Listen -ErrorAction SilentlyContinue)) {
    Start-Process (Join-Path $mysqlBase 'mysql-8.0.46-winx64\bin\mysqld.exe') `
        -ArgumentList "--defaults-file=`"$mysqlBase\my.ini`"" -WindowStyle Hidden
    if (-not (Wait-Port 3307 60)) { throw 'MySQL did not start on port 3307' }
}

if (-not (Get-NetTCPConnection -LocalPort 5081 -State Listen -ErrorAction SilentlyContinue)) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://localhost:5081'
    $env:ConnectionStrings__Default = "Server=127.0.0.1;Port=3307;Database=reworkguard;User=reworkguard;Password=$($envs.MYSQL_PASSWORD)"
    $env:Jwt__SigningKey = $envs.JWT_SIGNING_KEY
    $env:Database__MigrateOnStartup = 'true'
    $env:Database__SeedOnStartup = 'true'
    $env:Database__SeedPassword = $envs.SEED_PASSWORD
    $env:DevTools__ClockControl = 'true'
    $env:Email__Provider = 'Log'
    # Local demo: managers may approve deduction lines without waiting for the dispute window (default: enforced).
    $env:Deductions__EnforceDisputeWindow = if ($envs.ENFORCE_DISPUTE_WINDOW) { $envs.ENFORCE_DISPUTE_WINDOW } else { 'false' }
    $env:Notifications__AppBaseUrl = 'http://localhost:8080'
    $env:Cors__AllowedOrigins__0 = 'http://localhost:8080'
    # Windows needs ICU (not invariant globalization) to resolve the "Asia/Kolkata" zone Hangfire schedules use.
    Start-Process dotnet -ArgumentList 'run', '--no-launch-profile', '-p:InvariantGlobalization=false', '--project', "`"$root\backend\src\ReworkGuard.Api`"" `
        -WindowStyle Hidden -RedirectStandardOutput "$logs\api.log" -RedirectStandardError "$logs\api.err.log"
    if (-not (Wait-Port 5081 180)) { throw "API did not start; see $logs\api.log" }

    # The development clock resets on every API start. Data entered while it was moved ahead (demo story, tasks
    # completed "in the future") needs the same lead again, so DEV_CLOCK_OFFSET_HOURS in .env re-applies it.
    if ($envs.DEV_CLOCK_OFFSET_HOURS) {
        $login = Invoke-RestMethod -Method Post -Uri 'http://localhost:5081/api/v1/auth/login' -ContentType 'application/json' `
            -Body (@{ email = 'admin@reworkguard.local'; password = $envs.SEED_PASSWORD } | ConvertTo-Json)
        $clock = Invoke-RestMethod -Method Post -Uri 'http://localhost:5081/api/v1/dev/clock/advance' -ContentType 'application/json' `
            -Headers @{ Authorization = "Bearer $($login.accessToken)" } -Body (@{ hours = [double]$envs.DEV_CLOCK_OFFSET_HOURS } | ConvertTo-Json)
        Write-Host "App clock moved ahead $($envs.DEV_CLOCK_OFFSET_HOURS) h: $($clock.utcNow)"
    }
}

if (-not (Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue)) {
    $env:VITE_API_PROXY_TARGET = 'http://localhost:5081'
    Start-Process cmd -ArgumentList '/c', 'npx', 'vite', '--port', '8080', '--strictPort' `
        -WorkingDirectory "$root\frontend" -WindowStyle Hidden `
        -RedirectStandardOutput "$logs\web.log" -RedirectStandardError "$logs\web.err.log"
    if (-not (Wait-Port 8080 90)) { throw "Web app did not start; see $logs\web.log" }
}

Write-Host 'ReworkGuard is running: http://localhost:8080'

