# Installs the pinned OpenClaw runtime (runtime/openclaw) into <DataRoot>\Runtime, where the host expects it.
# Only the locked dependency tree is installed, with dependency lifecycle scripts disabled; OpenClaw's own
# bundled-plugin postinstall then runs explicitly under the selected Node. No credentials are read or created.
# -NodePath selects a Node executable satisfying the runtime's engines range (default: the data root's existing
# Runtime\node\node.exe, then node on PATH). It is copied to Runtime\node\node.exe.
# See docs/adr/0004-pinned-agent-runtimes.md.
param(
    [string]$DataRoot = $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' }),
    [string]$NodePath = ''
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$source = Join-Path (Join-Path $repository 'runtime') 'openclaw'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $source 'package.json') | ConvertFrom-Json
$pinned = $manifest.dependencies.openclaw
$dataRoot = [IO.Path]::GetFullPath($DataRoot)
$runtime = Join-Path $dataRoot 'Runtime'
$nodeTarget = Join-Path (Join-Path $runtime 'node') 'node.exe'
$target = Join-Path $runtime 'openclaw'

$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($dataRoot))
if ($drive.AvailableFreeSpace -lt 10GB) { throw "Keep at least 10 GiB free before installing a runtime; $([math]::Round($drive.AvailableFreeSpace / 1GB, 1)) GiB is available." }

function Test-NodeVersion([string]$version) {
    # Mirrors runtime/openclaw engines: >=24.16.0 <25 || >=26.1.0
    $parsed = [version]($version.TrimStart('v'))
    return ($parsed.Major -eq 24 -and $parsed -ge [version]'24.16.0') -or $parsed -ge [version]'26.1.0'
}

$node = if ($NodePath) { $NodePath } elseif (Test-Path -LiteralPath $nodeTarget -PathType Leaf) { $nodeTarget } else { (Get-Command node -ErrorAction Stop).Source }
$node = [IO.Path]::GetFullPath($node)
$nodeVersion = (& $node --version).Trim()
if (-not (Test-NodeVersion $nodeVersion)) { throw "OpenClaw $pinned requires Node $($manifest.engines.node); $node is $nodeVersion. Pass -NodePath to a qualified Node." }
if ($node -ne $nodeTarget) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $nodeTarget) | Out-Null
    Copy-Item -LiteralPath $node -Destination $nodeTarget -Force
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'package.json') -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $source 'package-lock.json') -Destination $target -Force
& npm ci --prefix $target --ignore-scripts --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw 'npm ci did not complete; the runtime was not changed further.' }

$package = Join-Path (Join-Path $target 'node_modules') 'openclaw'
$installed = (Get-Content -Raw -LiteralPath (Join-Path $package 'package.json') | ConvertFrom-Json).version
if ($installed -ne $pinned) { throw "Installed OpenClaw $installed does not match the pin $pinned." }

$postinstall = Join-Path (Join-Path $package 'scripts') 'postinstall-bundled-plugins.mjs'
if (Test-Path -LiteralPath $postinstall -PathType Leaf) {
    Push-Location -LiteralPath $package
    try {
        & $nodeTarget $postinstall
        if ($LASTEXITCODE -ne 0) { throw 'The OpenClaw bundled-plugin setup did not complete.' }
    } finally { Pop-Location }
}

$receipt = [ordered]@{
    openclaw = $installed
    node = $nodeVersion
    lockSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $source 'package-lock.json')).Hash.ToLowerInvariant()
    installedAt = (Get-Date).ToUniversalTime().ToString('o')
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'hirezero-runtime.json') -Encoding utf8
Write-Output "OpenClaw $installed installed for $dataRoot with Node $nodeVersion."
