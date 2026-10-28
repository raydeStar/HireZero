# Keeps a company-local install running and backed up (docs/adr/0044-local-company-backups.md), with two scheduled
# tasks for the current Windows user (no administrator rights): "HireZero company" starts the company at sign-in and
# restarts it if it stops; "HireZero backup" backs it up every night (or at the next chance if the machine was off).
# Output goes to <Install>/logs. -Remove deletes both tasks. Run it from the install's tools folder.
param(
    [Parameter(Mandatory)][string]$DataRoot,
    [string]$BackupTo,
    [int]$Keep = 14,
    [string]$BackupAt = '03:00',
    # Separate installs need separate task names; one butler must not dismiss another's shift.
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$')][string]$TaskPrefix = 'HireZero',
    # Environment the company needs, for example @{ HIREZERO_API_MODELS = '1'; HIREZERO_MODEL_ROUTES = 'C:\...\model-routes.json' }.
    # Never secrets: keys stay in the user's environment or the app's secret store.
    [hashtable]$Environment = @{},
    [string[]]$StartArguments = @(),
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$names = "$TaskPrefix company", "$TaskPrefix backup"
if ($Remove) {
    foreach ($name in $names) { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue }
    Write-Output 'Removed the HireZero scheduled tasks.'
    return
}
if (-not $BackupTo) { throw 'Name a backup folder outside the data root with -BackupTo.' }
foreach ($key in $Environment.Keys) {
    if ($key -match 'KEY|TOKEN|SECRET|PASSWORD') { throw "Keep $key out of the scheduled task; it stays in your environment or the app's secret store." }
}
$install = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath (Join-Path $install 'app/HireZero.Host.dll'))) { throw 'Run this from a published install (Update-CompanyLocal.ps1).' }
$logs = Join-Path $install 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$pwsh = (Get-Command pwsh).Source
function Quote([string]$text) { "'" + $text.Replace("'", "''") + "'" }

$setEnvironment = ($Environment.GetEnumerator() | ForEach-Object { '$env:' + $_.Key + '=' + (Quote ([string]$_.Value)) + ';' }) -join ' '
$start = (Quote (Join-Path $PSScriptRoot 'Start-CompanyLocal.ps1')) + ' -DataRoot ' + (Quote ([IO.Path]::GetFullPath($DataRoot))) + ' ' + ($StartArguments -join ' ')
$companyCommand = "$setEnvironment & $start *>> $(Quote (Join-Path $logs 'company.log'))"
$backup = (Quote (Join-Path $PSScriptRoot 'Backup-CompanyLocal.ps1')) + ' -DataRoot ' + (Quote ([IO.Path]::GetFullPath($DataRoot))) + ' -Destination ' + (Quote ([IO.Path]::GetFullPath($BackupTo))) + " -Keep $Keep"
$backupCommand = "& $backup *>> $(Quote (Join-Path $logs 'backup.log'))"

$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$company = New-ScheduledTaskAction -Execute $pwsh -Argument ('-NoProfile -WindowStyle Hidden -Command ' + '"' + $companyCommand.Replace('"', '\"') + '"') -WorkingDirectory $install
$companySettings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $names[0] -Action $company -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $user) -Principal $principal -Settings $companySettings -Description 'Starts the HireZero company at sign-in.' -Force | Out-Null

$nightly = New-ScheduledTaskAction -Execute $pwsh -Argument ('-NoProfile -WindowStyle Hidden -Command ' + '"' + $backupCommand.Replace('"', '\"') + '"') -WorkingDirectory $install
$backupSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $names[1] -Action $nightly -Trigger (New-ScheduledTaskTrigger -Daily -At $BackupAt) -Principal $principal -Settings $backupSettings -Description 'Backs up the HireZero company every night.' -Force | Out-Null
Write-Output "Registered '$($names[0])' (at sign-in) and '$($names[1])' (daily at $BackupAt). Logs: $logs"
