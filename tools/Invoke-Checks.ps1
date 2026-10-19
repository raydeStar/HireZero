# Every offline check in one run, for a developer machine or any CI runner (docs/adr/0026). Nothing here calls a
# model, GitHub or a package source beyond the local caches. -Browser adds the headless Chrome smokes.
param([switch]$Browser)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
Push-Location -LiteralPath $repository
$failed = [System.Collections.Generic.List[string]]::new()
function Step([string]$name, [scriptblock]$run) {
    Write-Output "== $name"
    $global:LASTEXITCODE = 0
    try { & $run; if ($LASTEXITCODE -ne 0) { $failed.Add($name) } }
    catch { Write-Output $_; $failed.Add($name) }
}
try {
    Step 'C# formatting' { dotnet csharpier check . }
    Step 'Web formatting' { npm run format:check --prefix web }
    Step 'Web types' { npm run typecheck --prefix web }
    Step 'Web build' { npm run build --prefix web }
    Step '.NET build' { dotnet build HireZero.slnx }
    Step '.NET tests' { dotnet test --solution HireZero.slnx --no-build }
    Step 'Frontend unit tests' { node --test (Get-ChildItem tests -Filter '*.test.mjs').FullName }
    Step 'PowerShell launcher' { pwsh -NoProfile -File tests/company-launcher.test.ps1 }
    if (Get-Command sh -ErrorAction SilentlyContinue) { Step 'POSIX launcher' { sh tests/company-launcher.test.sh } }
    Step 'Release helpers' { pwsh -NoProfile -File tests/release.test.ps1 }
    if ($Browser) {
        foreach ($smoke in Get-ChildItem tests -Filter '*-browser-smoke.mjs') {
            Step $smoke.BaseName { node $smoke.FullName }
        }
    }
}
finally { Pop-Location }
if ($failed.Count -gt 0) {
    Write-Output ('FAIL ' + ($failed -join ', '))
    exit 1
}
Write-Output 'PASS all offline checks'
