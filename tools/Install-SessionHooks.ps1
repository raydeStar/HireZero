# Adds (or removes) HireZero's session hook to Claude Code (~\.claude\settings.json) and Codex (~\.codex\hooks.json).
# Default is a dry run that prints what would change. -Apply writes, after backing each file up. -Remove takes ours out.
# Existing hooks (yours, plugins', SemaForge's) are left exactly as they are; only entries pointing at our script change.
param(
    [string]$DataRoot = $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' }),
    [switch]$Apply,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$script = (Join-Path $PSScriptRoot 'hirezero-session-hook.mjs').Replace('\', '/')
$data = ([IO.Path]::GetFullPath($DataRoot)).Replace('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $DataRoot 'hook.token'))) { Write-Warning "No hook.token in $DataRoot yet. Start HireZero once with this data root first." }
$events = 'SessionStart', 'UserPromptSubmit', 'Stop', 'Notification', 'SessionEnd'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Update-HookFile([string]$path, [string]$app, [string[]]$appEvents, [scriptblock]$entry) {
    $exists = Test-Path -LiteralPath $path
    $config = if ($exists) { Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable } else { [ordered]@{} }
    if (-not $config.Contains('hooks')) { $config['hooks'] = [ordered]@{} }
    $changes = @()
    foreach ($event in $appEvents) {
        $groups = @($config['hooks'][$event] | Where-Object { $_ })
        # Drop only our own hook commands, keep every other hook in place.
        $kept = @(foreach ($group in $groups) {
            $hooks = @($group['hooks'] | Where-Object { "$($_['command'])$($_['commandWindows'])" -notmatch 'hirezero-session-hook\.mjs' })
            if ($hooks.Count -gt 0) { $group['hooks'] = $hooks; $group }
        })
        if (-not $Remove) { $kept += & $entry }
        if ($kept.Count -gt 0) { $config['hooks'][$event] = $kept } else { $config['hooks'].Remove($event) }
        $changes += "$app ${event}: " + $(if ($Remove) { 'remove HireZero hook' } else { 'add HireZero hook' })
    }
    $json = $config | ConvertTo-Json -Depth 20
    if ($Apply) {
        if ($exists) { Copy-Item -LiteralPath $path "$path.bak-hirezero-$stamp" }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        Set-Content -LiteralPath $path -Value $json -Encoding utf8
        Write-Output "Updated $path$(if ($exists) { " (backup: $path.bak-hirezero-$stamp)" })"
    } else {
        Write-Output "Dry run for ${path}:"; $changes | ForEach-Object { Write-Output "  $_" }
    }
}

$command = "node `"$script`" APP `"$data`""
Update-HookFile (Join-Path $env:USERPROFILE '.claude\settings.json') 'Claude' $events {
    [ordered]@{ hooks = @([ordered]@{ type = 'command'; command = $command.Replace('APP', 'claude'); timeout = 5 }) }
}
Update-HookFile (Join-Path $env:USERPROFILE '.codex\hooks.json') 'Codex' ($events | Where-Object { $_ -ne 'Notification' }) {
    [ordered]@{ hooks = @([ordered]@{ type = 'command'; command = $command.Replace('APP', 'codex');
        commandWindows = "powershell -NoProfile -ExecutionPolicy Bypass -Command `"& node '$script' codex '$data'`""; timeout = 5 }) }
}
if (-not $Apply) { Write-Output 'Nothing was written. Re-run with -Apply to install (each file is backed up first), or -Remove -Apply to uninstall.' }
