# Release helpers, offline (docs/adr/0026): the SBOM lists what ships, and a signed checksum list rejects any changed,
# added or re-signed file. openssl, when present, verifies the same signature independently.
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $repository 'tools/Release.psm1') -Force
$checks = 0
function Check($ok, $message) { $script:checks++; if (-not $ok) { throw $message } }
$work = Join-Path ([IO.Path]::GetTempPath()) ('hirezero-release-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $work | Out-Null
try {
    $sbom = New-HireZeroSbom -Repository $repository -Version '0.0.0-test'
    $names = $sbom.components | ForEach-Object { $_.name }
    Check ($sbom.bomFormat -eq 'CycloneDX' -and $sbom.metadata.component.version -eq '0.0.0-test') 'The SBOM is CycloneDX for this version.'
    Check ($names -contains 'Npgsql' -and $names -contains 'react') 'Shipped NuGet and npm packages are listed.'
    Check ($names -notcontains 'vite' -and $names -notcontains 'typescript') 'Build-only tools are not.'
    Check (@($sbom.components | Where-Object { $_.purl -like 'pkg:nuget/*' -and $_.hashes[0].content -notmatch '^[0-9a-f]{128}$' }).Count -eq 0) 'Every NuGet package carries its SHA-512.'

    $release = Join-Path $work 'release'
    New-Item -ItemType Directory $release | Out-Null
    Set-Content -LiteralPath (Join-Path $release 'hirezero-0.0.0-test.zip') -Value 'fictional archive' -NoNewline
    $sbom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $release 'hirezero-0.0.0-test.cdx.json')
    $public = Join-Path $work 'release.pub.pem'
    Set-Content -LiteralPath $public -Value (New-ReleaseKey -PrivateKeyPath (Join-Path $work 'release.key.pem'))
    Write-ReleaseChecksums -Directory $release | Out-Null
    Protect-Release -Directory $release -PrivateKeyPath (Join-Path $work 'release.key.pem')
    Check (Test-Release -Directory $release -PublicKeyPath $public) 'A signed release verifies.'

    $openssl = Get-Command openssl -ErrorAction SilentlyContinue
    if ($openssl) {
        $der = Join-Path $work 'sums.der'
        [IO.File]::WriteAllBytes($der, [Convert]::FromBase64String((Get-Content -Raw (Join-Path $release 'SHA256SUMS.sig')).Trim()))
        & $openssl dgst -sha256 -verify $public -signature $der (Join-Path $release 'SHA256SUMS') | Out-Null
        Check ($LASTEXITCODE -eq 0) 'openssl verifies the same signature.'
    }

    Set-Content -LiteralPath (Join-Path $release 'hirezero-0.0.0-test.zip') -Value 'changed archive' -NoNewline
    Check (-not (Test-Release -Directory $release -PublicKeyPath $public)) 'A changed file is rejected.'
    Set-Content -LiteralPath (Join-Path $release 'hirezero-0.0.0-test.zip') -Value 'fictional archive' -NoNewline
    Set-Content -LiteralPath (Join-Path $release 'extra.txt') -Value 'added'
    Check (-not (Test-Release -Directory $release -PublicKeyPath $public)) 'A file left out of the checksums is rejected.'
    Remove-Item -LiteralPath (Join-Path $release 'extra.txt')
    $other = Join-Path $work 'other.pub.pem'
    Set-Content -LiteralPath $other -Value (New-ReleaseKey -PrivateKeyPath (Join-Path $work 'other.key.pem'))
    Check (-not (Test-Release -Directory $release -PublicKeyPath $other)) 'Another key does not verify it.'
    Write-ReleaseChecksums -Directory $release | Out-Null
    Protect-Release -Directory $release -PrivateKeyPath (Join-Path $work 'other.key.pem')
    Check (-not (Test-Release -Directory $release -PublicKeyPath $public)) 'Checksums re-signed with another key are rejected.'
    Write-Output "PASS $checks release checks (SBOM, checksums, signature; offline)"
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
