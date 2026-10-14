function Assert-HireZeroLaunchProfile([object]$Actual, [object]$Expected) {
    if ($null -eq $Actual -or $Actual.version -ne 1) {
        throw 'The running workspace has no verifiable launch profile. Review it and stop it explicitly before starting this configuration.'
    }
    if ($Actual.localOwnerEnabled -ne $true) { throw 'The running workspace does not have the requested local owner mode.' }
    if (-not [string]::Equals([string]$Actual.ownerLogin, [string]$Expected.ownerLogin, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The running workspace has a different owner. No process was reused or changed.'
    }
    try { $actualRoot = [IO.Path]::GetFullPath([string]$Actual.dataRoot) }
    catch { throw 'The running workspace has an invalid data root. No process was reused or changed.' }
    if (-not [string]::Equals($actualRoot.TrimEnd('\','/'), ([string]$Expected.dataRoot).TrimEnd('\','/'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The running workspace has a different data root. No process was reused or changed.'
    }
    if (-not [string]::Equals([string]$Actual.tailnetAddress, [string]$Expected.tailnetAddress, [StringComparison]::Ordinal)) {
        throw 'The running workspace has a different tailnet listener configuration. No process was reused or changed.'
    }
    $actualNames = @($Actual.tailnetNames | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique)
    $expectedNames = @($Expected.tailnetNames | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique)
    if (($actualNames -join ',') -cne ($expectedNames -join ',')) {
        throw 'The running workspace has different tailnet hostnames. No process was reused or changed.'
    }
}
