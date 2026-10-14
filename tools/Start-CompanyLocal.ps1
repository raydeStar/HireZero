param([Parameter(Mandatory=$true)][string]$DataRoot)
$ErrorActionPreference='Stop'
$project=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/HireZero.Host'
$binary=Join-Path $project 'bin/Debug/net10.0/HireZero.Host.dll'
if(-not (Test-Path -LiteralPath $binary)){throw 'Build HireZero.Host and the web frontend before starting company-local mode.'}
$chosenRoot=[IO.Path]::GetFullPath($DataRoot)
$names=@('HIREZERO_LOCAL_OWNER','HIREZERO_DATA_ROOT','HIREZERO_EXECUTION_HOLD')
$prior=@{}
foreach($name in $names){$prior[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:HIREZERO_LOCAL_OWNER='1'
    $env:HIREZERO_DATA_ROOT=$chosenRoot
    $env:HIREZERO_EXECUTION_HOLD='0'
    Push-Location -LiteralPath $project
    try{ & dotnet $binary --company-local; if($LASTEXITCODE -ne 0){throw 'Company-local host stopped with an error.'} }
    finally{Pop-Location}
}
finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$prior[$name],'Process')}}
