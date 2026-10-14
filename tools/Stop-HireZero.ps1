# Pass the same -DataRoot given to Start-HireZero.ps1; its receipt lives there.
param([string]$DataRoot = $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' }))
$ErrorActionPreference = 'Stop'
$receipt = Join-Path $DataRoot 'local-services.json'
if (-not (Test-Path -LiteralPath $receipt -PathType Leaf)) { Write-Output 'No launcher-owned services were recorded.'; return }
$records = @(Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json)
$processSnapshot = @(Get-CimInstance Win32_Process)

function Get-Descendants([int]$parentId, [datetime]$rootStart, [int[]]$visited) {
    foreach ($child in $processSnapshot | Where-Object {
        $_.ParentProcessId -eq $parentId -and
        $_.CreationDate.ToUniversalTime() -ge $rootStart -and
        $_.ProcessId -notin $visited
    }) {
        Get-Descendants $child.ProcessId $rootStart ($visited + $child.ProcessId)
        $child.ProcessId
    }
}

foreach ($record in $records) {
    $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq $record.startTicks) {
        $children = @(Get-Descendants $record.id $process.StartTime.ToUniversalTime() @($record.id))
        foreach ($childId in $children) { Stop-Process -Id $childId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $record.id -Force -ErrorAction SilentlyContinue
        Write-Output "Stopped launcher-owned process $($record.id)."
    }
}
Remove-Item -LiteralPath $receipt
