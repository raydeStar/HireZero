param([Parameter(Mandatory=$true)][string]$DataRoot,[switch]$Codex,[string]$CodexExecutable,[string]$CodexIdentity,[string]$CodexHome,[string]$TextModel='openai/gpt-6-luna',[string]$CodingModel='openai/gpt-6.1-sol',[switch]$Coding,[string[]]$CodingRead=@(),[string]$OwnerLogin)
$ErrorActionPreference='Stop'
$project=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/HireZero.Host'
$binary=Join-Path $project 'bin/Debug/net10.0/HireZero.Host.dll'
if(-not (Test-Path -LiteralPath $binary)){throw 'Build HireZero.Host and the web frontend before starting company-local mode.'}
$chosenRoot=[IO.Path]::GetFullPath($DataRoot)
$names=@('HIREZERO_OWNER_LOGIN','HIREZERO_LOCAL_OWNER','HIREZERO_DATA_ROOT','HIREZERO_EXECUTION_HOLD','HIREZERO_COMPANY_CODEX_EXECUTABLE','HIREZERO_COMPANY_CODEX_IDENTITY','HIREZERO_COMPANY_CODEX_HOME','HIREZERO_COMPANY_TEXT_MODEL','HIREZERO_COMPANY_CODING_MODEL','HIREZERO_COMPANY_CODING_READ')
$prior=@{}
foreach($name in $names){$prior[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:HIREZERO_LOCAL_OWNER='1'
    if($OwnerLogin){$env:HIREZERO_OWNER_LOGIN=$OwnerLogin}
    $env:HIREZERO_DATA_ROOT=$chosenRoot
    $env:HIREZERO_EXECUTION_HOLD='0'
    $env:HIREZERO_COMPANY_TEXT_MODEL=$TextModel
    $env:HIREZERO_COMPANY_CODING_MODEL=$CodingModel
    $profileArgs=@('--company-local')
    if($Codex){if(-not $CodexExecutable -or -not $CodexIdentity -or -not $CodexHome){throw 'Select an exact Codex executable, reviewed bundle identity and dedicated profile home.'};$env:HIREZERO_COMPANY_CODEX_EXECUTABLE=$CodexExecutable;$env:HIREZERO_COMPANY_CODEX_IDENTITY=$CodexIdentity;$env:HIREZERO_COMPANY_CODEX_HOME=[IO.Path]::GetFullPath($CodexHome);$profileArgs+='--company-codex'}
    if($Coding){if(-not $Codex){throw 'Live coding needs -Codex with the pinned Codex executable.'};$env:HIREZERO_COMPANY_CODING_READ=(($CodingRead|ForEach-Object{[IO.Path]::GetFullPath($_)}) -join ';');$profileArgs+='--company-coding'}
    Push-Location -LiteralPath $project
    try{ & dotnet $binary @profileArgs; if($LASTEXITCODE -ne 0){throw 'Company-local host stopped with an error.'} }
    finally{Pop-Location}
}
finally{foreach($name in $names){if($null -eq $prior[$name]){Remove-Item -LiteralPath ('Env:'+$name) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($name,$prior[$name],'Process')}}}
