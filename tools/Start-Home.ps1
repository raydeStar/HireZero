# Start the restored UI with fresh owner chat admission and read-only discovery.
param([string]$DataRoot = (Join-Path $env:USERPROFILE '.hirezero-home'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-HireZero.ps1') -DataRoot $DataRoot -DiscoveryOnly
