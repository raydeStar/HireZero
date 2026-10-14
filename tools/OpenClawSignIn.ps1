param(
    [string]$Login = '',
    [switch]$Owner,
    [string]$DataRoot = $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' })
)

$ErrorActionPreference = 'Stop'
$normalized = if ($Owner) { 'owner-private' } else { $Login.Trim().ToLowerInvariant() }
if ($Owner -and $Login) { throw 'Choose -Owner or -Login, not both.' }
if (-not $Owner -and ($normalized.Length -lt 3 -or -not $normalized.Contains('@'))) { throw 'Enter the exact invited login.' }
$bytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($normalized))
$name = [Convert]::ToHexString($bytes).Substring(0, 24).ToLowerInvariant()
$cell = Join-Path (Join-Path $DataRoot 'OpenClaw') $name
$config = Join-Path $cell 'openclaw.json'
$runtime = Join-Path $DataRoot 'Runtime'
$node = Join-Path (Join-Path $runtime 'node') 'node.exe'
$entry = Join-Path (Join-Path (Join-Path (Join-Path $runtime 'openclaw') 'node_modules') 'openclaw') 'openclaw.mjs'
if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { throw 'The agent cell is not prepared. Start the workspace for -Owner, or invite this person first.' }
if (-not (Test-Path -LiteralPath $node -PathType Leaf) -or -not (Test-Path -LiteralPath $entry -PathType Leaf)) { throw 'The OpenClaw runtime is not installed.' }

$env:OPENCLAW_STATE_DIR = $cell
$env:OPENCLAW_CONFIG_PATH = $config
$env:PATH = (Split-Path -Parent $node) + [IO.Path]::PathSeparator + $env:PATH
Write-Output 'OpenClaw will show a device-code sign-in for this isolated cell. Complete it in your browser; do not paste credentials into chat.'
& $node $entry models auth login --provider openai --device-code
if ($LASTEXITCODE -ne 0) { throw 'OpenAI sign-in did not complete.' }
Write-Output 'OpenAI sign-in completed for this cell.'
