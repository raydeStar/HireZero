# Builds and starts HireZero on http://127.0.0.1:5064/ with local owner mode.
# -DataRoot selects the data folder (default %USERPROFILE%\.hirezero, outside AppData: packaged desktop apps
# such as Codex or Claude redirect %LOCALAPPDATA% into a private per-app store).
# -CheckoutRoot repo places PR checkouts in <repo>\.worktrees\<id>; a path selects a shared folder.
# -OwnerLogin is your Tailscale login (e.g. you@company.com): through Tailscale Serve that identity is the owner;
# local access on this computer stays the owner too. Everyone else must be invited under People.
# -Tailnet also listens on this computer's tailnet address (plain HTTP inside Tailscale) when Tailscale Serve isn't
# available; each caller is identified with `tailscale whois`. Windows may ask once to allow it on Private networks.
param([string]$DataRoot = $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' }), [string]$CheckoutRoot = '',
    [string]$OwnerLogin = $(if ($env:HIREZERO_OWNER_LOGIN) { $env:HIREZERO_OWNER_LOGIN } else { '' }),
    [switch]$Tailnet, [switch]$DiscoveryOnly)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$dataRoot = [IO.Path]::GetFullPath($DataRoot)
$runtime = Join-Path $dataRoot 'Runtime'
$project = Join-Path $repository 'src\HireZero.Host\HireZero.Host.csproj'
$hostDll = Join-Path $repository 'src\HireZero.Host\bin\Debug\net10.0\HireZero.Host.dll'
$jevCredential = Join-Path $dataRoot 'Credentials\jev.dpapi'
$receipt = Join-Path $dataRoot 'local-services.json'
. (Join-Path $PSScriptRoot 'HireZeroLaunchProfile.ps1')

function Test-Endpoint([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri $url -TimeoutSec 2 -SkipHttpErrorCheck
        return [int]$response.StatusCode -eq 200
    } catch { return $false }
}

function Start-LocalProcess([string]$executable, [string[]]$arguments, [hashtable]$variables, [string]$workingDirectory = $repository) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $workingDirectory
    foreach ($argument in $arguments) { [void]$start.ArgumentList.Add($argument) }
    foreach ($name in $variables.Keys) { $start.Environment[$name] = $variables[$name] }
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "Could not start $executable" }
    return $process
}

function Wait-For([string]$url, [System.Diagnostics.Process]$process) {
    for ($attempt = 0; $attempt -lt 200; $attempt++) {
        if (Test-Endpoint $url) { return }
        if ($process.HasExited) { throw "Local service stopped before becoming ready: $url" }
        Start-Sleep -Milliseconds 250
    }
    throw "Local service did not become ready: $url"
}

if ($OwnerLogin -and $OwnerLogin.Trim() -notmatch '^[^@\s]+@[^@\s]+$') { throw '-OwnerLogin must be a login like you@company.com.' }
$requestedOwner = if ($OwnerLogin) { $OwnerLogin.Trim().ToLowerInvariant() } else { 'local-owner@workspace.invalid' }
$tailnetVariables = @{ HIREZERO_TAILNET_ADDRESS=''; HIREZERO_TAILNET_NAMES=''; HIREZERO_TAILSCALE='' }
$expectedProfile = @{ ownerLogin=$requestedOwner; dataRoot=$dataRoot; tailnetAddress=''; tailnetNames=@() }
if ($DiscoveryOnly -and $Tailnet) { throw 'Discovery mode is local only.' }
if ($Tailnet) {
    if (-not $OwnerLogin) { throw '-Tailnet needs -OwnerLogin, so your own tailnet identity is the owner.' }
    $tailscale = if (Test-Path -LiteralPath 'C:\Program Files\Tailscale\tailscale.exe') { 'C:\Program Files\Tailscale\tailscale.exe' } else { (Get-Command tailscale -ErrorAction Stop).Source }
    $status = & $tailscale status --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $status.BackendState -ne 'Running') { throw 'Tailscale status could not be verified as connected.' }
    $address = (& $tailscale ip -4 | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $address) { throw 'Tailscale address could not be verified.' }
    $full = $status.Self.DNSName.TrimEnd('.'); $short = $full.Split('.')[0]
    $tailnetVariables['HIREZERO_TAILNET_ADDRESS'] = $address; $tailnetVariables['HIREZERO_TAILNET_NAMES'] = "$full,$short"; $tailnetVariables['HIREZERO_TAILSCALE'] = $tailscale
    $expectedProfile.tailnetAddress = $address
    $expectedProfile.tailnetNames = @($full,$short,$address | ForEach-Object { $_.ToLowerInvariant() + ':5064' } | Sort-Object -Unique)
    $tailnetUrl = "http://$($full):5064/"
}
if ((Test-Endpoint 'http://127.0.0.1:5064/api/session') -and (Test-Endpoint 'http://127.0.0.1:5064/')) {
    try {
        $profileResponse = Invoke-WebRequest -Uri 'http://127.0.0.1:5064/api/runtime' -TimeoutSec 2 -SkipHttpErrorCheck
        if ([int]$profileResponse.StatusCode -ne 200) { throw 'unavailable' }
        $actualProfile = $profileResponse.Content | ConvertFrom-Json
    } catch { throw 'The running workspace launch profile cannot be verified. Review it and stop it explicitly before starting this configuration.' }
    if ([bool]$actualProfile.discoveryOnly -ne [bool]$DiscoveryOnly) { throw 'The running workspace has a different discovery mode. Stop it explicitly before changing modes.' }
    Assert-HireZeroLaunchProfile $actualProfile $expectedProfile
    Write-Output 'HireZero: http://127.0.0.1:5064/ (existing configuration verified)'
    if ($tailnetUrl) { Write-Output "On your tailnet: $tailnetUrl" }
    return
}
if (Get-NetTCPConnection -LocalPort 5064 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5064 is occupied by a workspace whose configuration could not be verified.' }
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $repository 'web\node_modules') -PathType Container)) {
    & npm ci --prefix (Join-Path $repository 'web') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The frontend dependencies did not install.' }
}
& npm run build --prefix (Join-Path $repository 'web') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The web app did not build.' }
& dotnet build $project --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'HireZero did not build.' }

$started = @()
if (-not ((Test-Endpoint 'http://127.0.0.1:5064/api/session') -and (Test-Endpoint 'http://127.0.0.1:5064/'))) {
    if (Get-NetTCPConnection -LocalPort 5064 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5064 is occupied by a workspace without local owner mode.' }
    $variables = @{ HIREZERO_LOCAL_OWNER='1'; HIREZERO_SYNTHETIC_WIKI='1'; HIREZERO_DATA_ROOT=$dataRoot; HIREZERO_OWNER_LOGIN=$requestedOwner }
    foreach ($name in $tailnetVariables.Keys) { $variables[$name] = $tailnetVariables[$name] }
    if ($DiscoveryOnly) { $variables['HIREZERO_DISCOVERY_ONLY']='1'; $variables['HIREZERO_EXECUTION_HOLD']='1' }
    if ($CheckoutRoot) { $variables['HIREZERO_CHECKOUT_ROOT'] = $CheckoutRoot }
    if ($OwnerLogin) {
        if ($OwnerLogin.Trim() -notmatch '^[^@\s]+@[^@\s]+$') { throw '-OwnerLogin must be a login like you@company.com.' }
        $variables['HIREZERO_OWNER_LOGIN'] = $OwnerLogin.Trim().ToLowerInvariant()
    }
    $node = Join-Path $runtime 'node\node.exe'; $entry = Join-Path $runtime 'openclaw\node_modules\openclaw\openclaw.mjs'
    if ((Test-Path -LiteralPath $node -PathType Leaf) -and (Test-Path -LiteralPath $entry -PathType Leaf)) {
        $variables['HIREZERO_OPENCLAW_NODE'] = $node; $variables['HIREZERO_OPENCLAW_ENTRY'] = $entry
    } else { Write-Warning "OpenClaw runtime not found under $runtime; chat replies will be unavailable." }
    if (-not $DiscoveryOnly -and (Test-Path -LiteralPath $jevCredential -PathType Leaf)) {
        try {
            $secureKey = Get-Content -LiteralPath $jevCredential -Raw | ConvertTo-SecureString
            $variables['HIREZERO_JEV_API_KEY'] = [Net.NetworkCredential]::new('', $secureKey).Password
        } catch { throw 'Jev credential could not be unlocked for this Windows user.' }
    }
    $hostProcess = Start-LocalProcess (Get-Command dotnet).Source @($hostDll, '--logging:LogLevel:Default=Warning') $variables (Split-Path -Parent $project)
    $variables.Remove('HIREZERO_JEV_API_KEY')
    $started += $hostProcess
    Wait-For 'http://127.0.0.1:5064/api/session' $hostProcess
    Wait-For 'http://127.0.0.1:5064/' $hostProcess
}
else { throw 'A workspace appeared during the build. Run the launcher again to verify its configuration before reuse.' }

$existing = if (Test-Path -LiteralPath $receipt) { @(Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json) } else { @() }
$alive = @($existing | Where-Object {
    $process = Get-Process -Id $_.id -ErrorAction SilentlyContinue
    $process -and $process.StartTime.ToUniversalTime().Ticks -eq $_.startTicks
})
$records = @($alive) + @($started | ForEach-Object {
    [pscustomobject]@{ id = $_.Id; startTicks = $_.StartTime.ToUniversalTime().Ticks }
})
ConvertTo-Json -InputObject $records -Depth 4 | Set-Content -LiteralPath $receipt

Write-Output 'HireZero: http://127.0.0.1:5064/'
if ($tailnetUrl) { Write-Output "On your tailnet: $tailnetUrl (invited people only)" }
Write-Output "SemaForge: $(if(Test-Endpoint 'http://127.0.0.1:5058/health'){'online'}else{'unavailable'})"
Write-Output "Synthetic Wiki: $(if(Test-Endpoint 'http://127.0.0.1:5094/healthz'){'online'}else{'unavailable'})"
Write-Output 'Jev: server-side connection configured when its protected credential is present.'
