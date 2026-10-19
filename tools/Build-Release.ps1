# Builds a release (docs/adr/0026-container-and-releases.md): a portable host for any OS with the .NET 10 runtime, its
# CycloneDX SBOM, SHA256SUMS and, with -SigningKey, a signature over the checksums. -Image also builds and saves the
# container image. Run it from a clean checkout; it restores packages, so it needs the package sources.
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')][string]$Version,
    [string]$Output,
    [string]$SigningKey,
    [switch]$Image,
    [switch]$SkipAdvisories
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
if (-not $Output) { $Output = Join-Path $repository "artifacts/release/$Version" }
$Output = [IO.Path]::GetFullPath($Output)
if ((Test-Path -LiteralPath $Output) -and (Get-ChildItem -LiteralPath $Output -Force)) { throw "$Output is not empty." }
if ($SigningKey -and [IO.Path]::GetFullPath($SigningKey).StartsWith($repository, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep the signing key outside the repository.'
}
$free = (Get-PSDrive -Name ([IO.Path]::GetPathRoot($Output).Substring(0, 1)) -ErrorAction SilentlyContinue)?.Free
if ($free -and $free -lt 10GB) { throw 'Less than 10 GiB free; free space before building a release.' }
if ((git -C $repository status --porcelain) -and -not $env:HIREZERO_RELEASE_DIRTY) {
    throw 'The checkout has uncommitted changes; release from a clean commit.'
}

function Invoke-Step([string]$what, [scriptblock]$step) {
    Write-Output "== $what"
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$what failed." }
}

if (-not $SkipAdvisories) { Invoke-Step 'Dependency advisories' { & (Join-Path $PSScriptRoot 'Check-Advisories.ps1') -Repository $repository } }
$name = "hirezero-$Version"
$staging = Join-Path ([IO.Path]::GetTempPath()) ("$name-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$package = Join-Path $staging $name
try {
    Invoke-Step 'Web UI' { npm ci --ignore-scripts --prefix (Join-Path $repository 'web'); npm run build --prefix (Join-Path $repository 'web') }
    Invoke-Step 'Host' {
        dotnet publish (Join-Path $repository 'src/HireZero.Host/HireZero.Host.csproj') -c Release -o (Join-Path $package 'app') `
            -p:Version=$Version -p:ContinuousIntegrationBuild=true -p:UseAppHost=false -p:RestoreLockedMode=true
    }
    New-Item -ItemType Directory -Force (Join-Path $package 'tools') | Out-Null
    foreach ($file in 'Start-CompanyLocal.ps1', 'start-company-local.sh') {
        Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $package 'tools')
    }
    Copy-Item (Join-Path $repository 'README.md'), (Join-Path $repository 'LICENSE') $package
    New-Item -ItemType Directory -Force $Output | Out-Null
    Compress-Archive -Path $package -DestinationPath (Join-Path $Output "$name.zip")
    New-HireZeroSbom -Repository $repository -Version $Version | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $Output "$name.cdx.json") -Encoding utf8NoBOM
    if ($Image) {
        Invoke-Step 'Container image' { docker build -t "hirezero:$Version" --label "org.opencontainers.image.version=$Version" $repository }
        Invoke-Step 'Image archive' { docker save -o (Join-Path $Output "$name-image.tar") "hirezero:$Version" }
    }
    Write-ReleaseChecksums -Directory $Output | Out-Null
    if ($SigningKey) { Protect-Release -Directory $Output -PrivateKeyPath $SigningKey }
}
finally { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
Write-Output "Release $Version in $Output"
Get-ChildItem -LiteralPath $Output | ForEach-Object { Write-Output ('  {0} ({1:N0} bytes)' -f $_.Name, $_.Length) }
