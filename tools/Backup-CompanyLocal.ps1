# Takes a local backup of a company data root (docs/adr/0044-local-company-backups.md): each database through
# SQLite's online backup (safe while the company runs), the keys, secrets and settings, and a manifest; only the newest
# -Keep verified backups of this data root are kept. Other or modified folders need manual review (ADR 0104).
# Nothing leaves the machine. Restore by stopping the company and copying a backup's files
# back into an empty data root; secrets need the same Windows user.
param(
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$Destination,
    [ValidateRange(1, 365)][int]$Keep = 14
)
$ErrorActionPreference = 'Stop'
# A release keeps the published host in app/ beside tools/; a checkout runs its debug build (docs/adr/0026).
$release = Join-Path (Split-Path $PSScriptRoot -Parent) 'app/HireZero.Host.dll'
$binary = if (Test-Path -LiteralPath $release) { $release } else { Join-Path (Split-Path $PSScriptRoot -Parent) 'src/HireZero.Host/bin/Debug/net10.0/HireZero.Host.dll' }
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build or publish HireZero.Host before taking a backup.' }
& dotnet $binary --company-backup ([IO.Path]::GetFullPath($DataRoot)) ([IO.Path]::GetFullPath($Destination)) $Keep
if ($LASTEXITCODE -ne 0) { throw 'The backup failed; nothing was removed.' }
