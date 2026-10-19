$ErrorActionPreference='Stop'
$launcher=Join-Path $PSScriptRoot '../tools/Start-CompanyLocal.ps1'
$names=@('HIREZERO_OWNER_LOGIN','HIREZERO_LOCAL_OWNER','HIREZERO_DATA_ROOT','HIREZERO_EXECUTION_HOLD','HIREZERO_COMPANY_CODEX_EXECUTABLE','HIREZERO_COMPANY_CODEX_IDENTITY','HIREZERO_COMPANY_CODEX_HOME','HIREZERO_COMPANY_TEXT_MODEL','HIREZERO_COMPANY_CODING_MODEL')
$prior=@{};foreach($name in $names){$prior[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$global:CompanyLauncherCaptured=@{};$global:CompanyLauncherArguments=@();$checks=0
function dotnet {
    $global:CompanyLauncherArguments=$args
    foreach($name in $names){$global:CompanyLauncherCaptured[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
    $global:LASTEXITCODE=0
}
function Check($ok,$message){$script:checks++;if(-not $ok){throw $message}}
try{
    $temporary=[IO.Path]::GetTempPath();$data=Join-Path $temporary 'fictional-company-launcher';$profile=Join-Path $temporary 'fictional-codex-profile';$executable=Join-Path $temporary 'fictional-codex.exe'
    & $launcher -DataRoot $data -OwnerLogin 'owner@example.invalid' -Codex -CodexExecutable $executable -CodexIdentity 'fictional-reviewed-bundle' -CodexHome $profile
    Check ($CompanyLauncherCaptured.HIREZERO_OWNER_LOGIN -eq 'owner@example.invalid') 'An existing company owner can be selected explicitly.'
    Check ($CompanyLauncherCaptured.HIREZERO_DATA_ROOT -eq $data) 'Existing company data root is passed unchanged.'
    Check ($CompanyLauncherCaptured.HIREZERO_COMPANY_CODEX_EXECUTABLE -eq $executable -and $CompanyLauncherCaptured.HIREZERO_COMPANY_CODEX_IDENTITY -eq 'fictional-reviewed-bundle') 'Only the explicitly pinned native connection is selected.'
    Check ($CompanyLauncherCaptured.HIREZERO_COMPANY_CODEX_HOME -eq $profile) 'Dedicated profile is process-scoped.'
    Check ($CompanyLauncherArguments -contains '--company-local' -and $CompanyLauncherArguments -contains '--company-codex') 'Company-only explicit connection profile is used.'
    foreach($name in $names){Check ([Environment]::GetEnvironmentVariable($name,'Process') -eq $prior[$name]) ('Launcher restores '+$name)}
    Write-Output "PASS $checks company launcher checks (mocked process; no runtime, login or model call)"
}
finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$prior[$name],'Process')}}
