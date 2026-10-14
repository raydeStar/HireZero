$ErrorActionPreference = 'Stop'
$credentialPath = Join-Path $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' }) 'Credentials\jev.dpapi'
$handler = $null
$client = $null
$request = $null
$response = $null
$key = $null
$body = $null
try {
    if (-not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
        Write-Output 'JEV_CREDENTIAL_MISSING'
        exit 1
    }
    $secureKey = Get-Content -LiteralPath $credentialPath -Raw | ConvertTo-SecureString
    $key = [Net.NetworkCredential]::new('', $secureKey).Password
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, 'https://api.typesafe.ai/v1/models')
    $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $key)
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    if ($response.StatusCode -in @([Net.HttpStatusCode]::Unauthorized, [Net.HttpStatusCode]::Forbidden)) {
        Write-Output 'JEV_CREDENTIAL_REJECTED'
        exit 1
    }
    if (-not $response.IsSuccessStatusCode) {
        Write-Output 'JEV_CATALOG_UNAVAILABLE'
        exit 1
    }
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    if (@($body.models | Where-Object { $_.name -eq 'jev-latest' }).Count -eq 0) {
        Write-Output 'JEV_LATEST_UNCONFIRMED'
        exit 1
    }
    Write-Output 'JEV_CREDENTIAL_ACCEPTED_AND_LATEST_AVAILABLE'
} catch {
    Write-Output 'JEV_CONNECTION_UNVERIFIED'
    exit 1
} finally {
    if ($null -ne $response) { $response.Dispose() }
    if ($null -ne $request) { $request.Dispose() }
    if ($null -ne $client) { $client.Dispose() }
    if ($null -ne $handler) { $handler.Dispose() }
    $key = $null
    $body = $null
}
