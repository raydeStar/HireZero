param([string]$Launcher = (Join-Path $PSScriptRoot '..\tools\Start-HireZero.ps1'))
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('hirezero-launch-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$global:HireZeroLauncherTestbuildCalls = 0
$global:HireZeroLauncherTestcount = 0
function npm { $global:HireZeroLauncherTestbuildCalls++; $global:LASTEXITCODE = 0 }
function dotnet { $global:HireZeroLauncherTestbuildCalls++; $global:LASTEXITCODE = 0 }
function Get-NetTCPConnection { @() }
function Invoke-WebRequest {
    param([string]$Uri, [int]$TimeoutSec, [switch]$SkipHttpErrorCheck)
    if ($Uri.EndsWith('/api/runtime')) {
        return [pscustomobject]@{ StatusCode=$global:HireZeroLauncherTestprofileStatus; Content=($global:HireZeroLauncherTestprofile | ConvertTo-Json -Compress) }
    }
    [pscustomobject]@{ StatusCode=200; Content='{}' }
}
function Check([bool]$ok, [string]$message) {
    $global:HireZeroLauncherTestcount++
    if (-not $ok) { throw $message }
}
function Run-Launcher {
    & $Launcher -DataRoot $testRoot -OwnerLogin 'owner@example.invalid' | Out-Null
}
try {
    $global:HireZeroLauncherTestprofileStatus = 200
    foreach ($scenario in @('owner','data-root','tailnet','local-owner','legacy')) {
        $global:HireZeroLauncherTestprofile = @{version=1;ownerLogin='owner@example.invalid';dataRoot=$testRoot;tailnetAddress=$null;tailnetNames=@();localOwnerEnabled=$true}
        switch ($scenario) {
            'owner' { $global:HireZeroLauncherTestprofile.ownerLogin = 'other@example.invalid' }
            'data-root' { $global:HireZeroLauncherTestprofile.dataRoot = Join-Path $testRoot 'other' }
            'tailnet' { $global:HireZeroLauncherTestprofile.tailnetAddress = '100.64.0.12'; $global:HireZeroLauncherTestprofile.tailnetNames = @('example.invalid:5064') }
            'local-owner' { $global:HireZeroLauncherTestprofile.localOwnerEnabled = $false }
            'legacy' { $global:HireZeroLauncherTestprofileStatus = 404 }
        }
        $refused = $false
        try { Run-Launcher } catch { $refused = $true }
        Check $refused ('The launcher must refuse ' + $scenario + ' mismatch before reuse.')
        Check ($global:HireZeroLauncherTestbuildCalls -eq 0) 'A mismatched existing workspace must be refused before a build or launch.'
        $global:HireZeroLauncherTestprofileStatus = 200
    }
    $global:HireZeroLauncherTestprofile = @{version=1;ownerLogin='OWNER@example.invalid';dataRoot=$testRoot;tailnetAddress=$null;tailnetNames=@();localOwnerEnabled=$true}
    Run-Launcher
    Check ($global:HireZeroLauncherTestbuildCalls -eq 0) 'A matching live workspace is reused without rebuilding or starting a process.'
    Check (-not (Test-Path -LiteralPath (Join-Path $testRoot 'local-services.json'))) 'Reuse must not write a service receipt.'
    . (Join-Path (Split-Path -Parent $Launcher) 'HireZeroLaunchProfile.ps1')
    $expected = @{ownerLogin='owner@example.invalid';dataRoot=$testRoot;tailnetAddress='100.64.0.12';tailnetNames=@('host.example.ts.net:5064','100.64.0.12:5064')}
    $global:HireZeroLauncherTestprofile.tailnetAddress='100.64.0.12';$global:HireZeroLauncherTestprofile.tailnetNames=@('100.64.0.12:5064','HOST.example.ts.net:5064')
    Assert-HireZeroLaunchProfile $global:HireZeroLauncherTestprofile $expected
    Check $true 'Tailnet hostname comparison ignores order and case.'
    $global:HireZeroLauncherTestprofile.tailnetNames=@('other.example.ts.net:5064')
    $refused=$false
    try { Assert-HireZeroLaunchProfile $global:HireZeroLauncherTestprofile $expected } catch { $refused=$true }
    Check $refused 'Changed tailnet hostnames must be refused.'
    Write-Output "PASS $global:HireZeroLauncherTestcount launcher checks (fake HTTP, no builds, processes, or Tailscale calls)"
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup escaped its temporary root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
