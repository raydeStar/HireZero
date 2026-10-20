# Republishes this checkout into a company-local install (docs/adr/0026-container-and-releases.md): the host in
# <Target>/app and the launcher in <Target>/tools, the layout Start-CompanyLocal.ps1 runs from. A running host locks
# its files, so the install is built beside the old one and swapped in; -Stop first stops a host running from <Target>.
# The data root is never touched. Then start it again with <Target>/tools/Start-CompanyLocal.ps1.
param(
    [Parameter(Mandatory)][string]$Target,
    [switch]$Stop,
    [switch]$SkipWeb
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$Target = [IO.Path]::GetFullPath($Target)
if ($Target.StartsWith($repository, [StringComparison]::OrdinalIgnoreCase)) { throw 'Install outside the repository.' }
$free = (Get-PSDrive -Name ([IO.Path]::GetPathRoot($Target).Substring(0, 1)) -ErrorAction SilentlyContinue)?.Free
if ($free -and $free -lt 10GB) { throw 'Less than 10 GiB free; free space before publishing.' }

function Invoke-Step([string]$what, [scriptblock]$step) {
    Write-Output "== $what"
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$what failed." }
}

$next = Join-Path $Target 'app.next'
if (Test-Path -LiteralPath $next) { Remove-Item -LiteralPath $next -Recurse -Force }
if (-not $SkipWeb) { Invoke-Step 'Web UI' { npm run build --prefix (Join-Path $repository 'web') } }
Invoke-Step 'Host' {
    dotnet publish (Join-Path $repository 'src/HireZero.Host/HireZero.Host.csproj') -c Release -o $next -p:UseAppHost=false --nologo -v quiet
}

$current = Join-Path $Target 'app'
$hostDll = Join-Path $current 'HireZero.Host.dll'
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($hostDll, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count) {
    if (-not $Stop) { throw "The company host is running from $current (process $($running.ProcessId -join ', ')). Stop it, or pass -Stop." }
    Write-Output "== Stopping the running host ($($running.ProcessId -join ', '))"
    $running | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Seconds 2
}
if (Test-Path -LiteralPath $current) { Remove-Item -LiteralPath $current -Recurse -Force }
Move-Item -LiteralPath $next -Destination $current
New-Item -ItemType Directory -Path (Join-Path $Target 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-CompanyLocal.ps1') -Destination (Join-Path $Target 'tools') -Force
$commit = git -C $repository rev-parse --short HEAD
Write-Output "Published $commit to $current. Start it with $(Join-Path $Target 'tools/Start-CompanyLocal.ps1') -DataRoot <data root>."
