# Verifies a release directory against the publisher's public key: the signature over SHA256SUMS, and every file
# against its checksum (docs/adr/0026). Without PowerShell: sha256sum -c SHA256SUMS, then
# openssl base64 -d -in SHA256SUMS.sig -out sums.der; openssl dgst -sha256 -verify KEY.pem -signature sums.der SHA256SUMS
param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$PublicKey)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
if (Test-Release -Directory $Directory -PublicKeyPath $PublicKey) { Write-Output 'PASS signature and checksums'; exit 0 }
Write-Output 'FAIL the release does not match its signed checksums'
exit 1
