# Release helpers (docs/adr/0026-container-and-releases.md): a CycloneDX SBOM from the committed lockfiles, SHA-256
# checksums of every release file, and an ECDSA P-256 signature over the checksum list. Everything here is offline.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# DER-encoded signatures, the form openssl dgst -verify reads, so a release can be checked without this module.
$Der = [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence

function ConvertTo-Hex([string]$base64) {
    [Convert]::ToHexString([Convert]::FromBase64String($base64)).ToLowerInvariant()
}

# Components shipped in a release: every resolved NuGet package of the host (direct and transitive) and every npm
# package bundled into the web UI (not build-only tools).
function New-HireZeroSbom {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Version)
    $components = [System.Collections.Generic.List[object]]::new()
    $nuget = Get-Content -Raw (Join-Path $Repository 'src/HireZero.Host/packages.lock.json') | ConvertFrom-Json -AsHashtable
    foreach ($framework in $nuget.dependencies.Keys) {
        foreach ($entry in $nuget.dependencies[$framework].GetEnumerator() | Sort-Object Key) {
            if ($entry.Value.type -eq 'Project') { continue }
            $components.Add([ordered]@{
                type = 'library'
                name = $entry.Key
                version = $entry.Value.resolved
                purl = "pkg:nuget/$($entry.Key)@$($entry.Value.resolved)"
                hashes = @([ordered]@{ alg = 'SHA-512'; content = ConvertTo-Hex $entry.Value.contentHash })
            })
        }
    }
    $npm = Get-Content -Raw (Join-Path $Repository 'web/package-lock.json') | ConvertFrom-Json -AsHashtable
    foreach ($entry in $npm.packages.GetEnumerator() | Sort-Object Key) {
        if (-not $entry.Key -or $entry.Value['dev']) { continue }
        $name = $entry.Key -replace '^.*node_modules/', ''
        $component = [ordered]@{
            type = 'library'
            name = $name
            version = $entry.Value.version
            purl = "pkg:npm/$($name -replace '^@', '%40')@$($entry.Value.version)"
        }
        if ($entry.Value['integrity'] -match '^sha512-(.+)$') {
            $component.hashes = @([ordered]@{ alg = 'SHA-512'; content = ConvertTo-Hex $Matches[1] })
        }
        if ($entry.Value['license']) { $component.licenses = @(@{ license = @{ id = $entry.Value.license } }) }
        $components.Add($component)
    }
    [ordered]@{
        bomFormat = 'CycloneDX'
        specVersion = '1.5'
        version = 1
        metadata = [ordered]@{
            component = [ordered]@{
                type = 'application'
                name = 'HireZero'
                version = $Version
                licenses = @(@{ license = @{ id = 'MIT' } })
            }
        }
        components = $components
    }
}

# One "<sha256>  <name>" line per file, sorted by name, the format sha256sum -c reads.
function Write-ReleaseChecksums {
    param([Parameter(Mandatory)][string]$Directory)
    $lines = Get-ChildItem -LiteralPath $Directory -File |
        Where-Object { $_.Name -notin 'SHA256SUMS', 'SHA256SUMS.sig' } |
        Sort-Object Name |
        ForEach-Object { '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name }
    $path = Join-Path $Directory 'SHA256SUMS'
    [IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"))
    $path
}

# Creates a signing key. The private key is written where the caller says, which must be outside the repository;
# the public key is returned for publishing beside the releases.
function New-ReleaseKey {
    param([Parameter(Mandatory)][string]$PrivateKeyPath)
    if (Test-Path -LiteralPath $PrivateKeyPath) { throw "A key already exists at $PrivateKeyPath." }
    $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    try {
        [IO.File]::WriteAllText($PrivateKeyPath, $key.ExportPkcs8PrivateKeyPem())
        $key.ExportSubjectPublicKeyInfoPem()
    }
    finally { $key.Dispose() }
}

function Protect-Release {
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$PrivateKeyPath)
    $sums = [IO.File]::ReadAllBytes((Join-Path $Directory 'SHA256SUMS'))
    $key = [Security.Cryptography.ECDsa]::Create()
    try {
        $key.ImportFromPem([IO.File]::ReadAllText($PrivateKeyPath))
        $signature = $key.SignData($sums, [Security.Cryptography.HashAlgorithmName]::SHA256, $Der)
        [IO.File]::WriteAllText((Join-Path $Directory 'SHA256SUMS.sig'), [Convert]::ToBase64String($signature) + "`n")
    }
    finally { $key.Dispose() }
}

# True only when the checksum list is signed by the public key and every listed file matches it, and no file in the
# directory is left out of the list.
function Test-Release {
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$PublicKeyPath)
    $sumsPath = Join-Path $Directory 'SHA256SUMS'
    $signaturePath = Join-Path $Directory 'SHA256SUMS.sig'
    if (-not (Test-Path -LiteralPath $sumsPath) -or -not (Test-Path -LiteralPath $signaturePath)) { return $false }
    $key = [Security.Cryptography.ECDsa]::Create()
    try {
        $key.ImportFromPem([IO.File]::ReadAllText($PublicKeyPath))
        $signed = $key.VerifyData(
            [IO.File]::ReadAllBytes($sumsPath),
            [Convert]::FromBase64String([IO.File]::ReadAllText($signaturePath).Trim()),
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            $Der
        )
    }
    catch { return $false }
    finally { $key.Dispose() }
    if (-not $signed) { return $false }
    $listed = @{}
    foreach ($line in [IO.File]::ReadAllLines($sumsPath)) {
        if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$') { return $false }
        $listed[$Matches[2]] = $Matches[1]
    }
    $files = @(Get-ChildItem -LiteralPath $Directory -File) | Where-Object { $_.Name -notin 'SHA256SUMS', 'SHA256SUMS.sig' }
    if (@($files).Count -ne $listed.Count) { return $false }
    foreach ($file in $files) {
        if (-not $listed.ContainsKey($file.Name)) { return $false }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant() -ne $listed[$file.Name]) { return $false }
    }
    $true
}

Export-ModuleMember -Function New-HireZeroSbom, Write-ReleaseChecksums, New-ReleaseKey, Protect-Release, Test-Release
