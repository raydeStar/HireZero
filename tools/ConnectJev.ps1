param(
    [Parameter(Mandatory=$true)][string]$VaultName,
    [Parameter(Mandatory=$true)][string]$SecretName
)
$ErrorActionPreference = 'Stop'
$credentialDirectory = Join-Path $(if ($env:HIREZERO_DATA_ROOT) { $env:HIREZERO_DATA_ROOT } else { Join-Path $env:USERPROFILE '.hirezero' }) 'Credentials'
$credentialPath = Join-Path $credentialDirectory 'jev.dpapi'
$secretValue = $null
$serialized = $null
try {
    $serialized = & az keyvault secret show --vault-name $VaultName --name $SecretName --query value --output json --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Key Vault read failed.' }
    $secretValue = ($serialized -join [Environment]::NewLine) | ConvertFrom-Json
    if ($secretValue -isnot [string] -or [string]::IsNullOrWhiteSpace($secretValue)) { throw 'Key Vault returned no usable key.' }
    New-Item -ItemType Directory -Path $credentialDirectory -Force | Out-Null
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $systemUser = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    foreach ($identity in @($currentUser, $systemUser)) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit', [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($credentialDirectory), $security)
    $protected = ConvertTo-SecureString -String $secretValue -AsPlainText -Force | ConvertFrom-SecureString
    [IO.File]::WriteAllText($credentialPath, $protected)
    Write-Output 'JEV_CREDENTIAL_SAVED_WITH_WINDOWS_PROTECTION'
} catch {
    Write-Output 'JEV_CREDENTIAL_NOT_SAVED: Key Vault read or protected local storage failed.'
    exit 1
} finally {
    $secretValue = $null
    $serialized = $null
}
