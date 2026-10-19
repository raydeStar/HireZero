# Lists known vulnerabilities in the NuGet and npm dependencies and compares them with the triage record
# (docs/security/advisories.json, docs/adr/0026). Every advisory needs a recorded decision; an untriaged one fails.
# It queries the package sources, so it runs before a release, not as a routine offline check.
param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$findings = [System.Collections.Generic.List[object]]::new()

$dotnet = & dotnet list (Join-Path $Repository 'HireZero.slnx') package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw 'dotnet could not list vulnerable packages.' }
foreach ($project in (($dotnet -join "`n") | ConvertFrom-Json -AsHashtable).projects) {
    foreach ($framework in @($project['frameworks'])) {
        if (-not $framework) { continue }
        foreach ($package in @($framework['topLevelPackages']) + @($framework['transitivePackages'])) {
            if (-not $package) { continue }
            foreach ($advisory in @($package['vulnerabilities'])) {
                if (-not $advisory) { continue }
                $findings.Add([pscustomobject]@{
                    Advisory = $advisory.advisoryurl; Package = 'nuget:' + $package.id; Version = $package.resolvedVersion
                    Severity = $advisory.severity.ToLowerInvariant()
                })
            }
        }
    }
}

$npm = & npm audit --json --prefix (Join-Path $Repository 'web') 2>$null
foreach ($entry in (($npm -join "`n") | ConvertFrom-Json -AsHashtable).vulnerabilities.GetEnumerator()) {
    foreach ($via in @($entry.Value.via)) {
        if ($via -isnot [System.Collections.IDictionary]) { continue }
        $findings.Add([pscustomobject]@{
            Advisory = $via.url; Package = 'npm:' + $entry.Key; Version = $entry.Value.range; Severity = $via.severity
        })
    }
}

$triage = @{}
foreach ($decision in (Get-Content -Raw (Join-Path $Repository 'docs/security/advisories.json') | ConvertFrom-Json).advisories) {
    $triage[$decision.advisory + '|' + $decision.package] = $decision
}
$open = @($findings | Sort-Object Advisory, Package -Unique | Where-Object { -not $triage.ContainsKey($_.Advisory + '|' + $_.Package) })
foreach ($finding in $findings | Sort-Object Advisory, Package -Unique) {
    $decision = $triage[$finding.Advisory + '|' + $finding.Package]
    $state = if ($decision) { $decision.decision } else { 'UNTRIAGED' }
    Write-Output ('{0,-10} {1,-9} {2} {3} {4}' -f $state, $finding.Severity, $finding.Package, $finding.Version, $finding.Advisory)
}
if ($open.Count -gt 0) {
    Write-Output "FAIL $($open.Count) advisories need a decision in docs/security/advisories.json"
    exit 1
}
Write-Output "PASS $($findings.Count) advisories, all triaged"
