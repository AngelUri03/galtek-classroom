#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'credential-provider-common.ps1')

Assert-IsWindowsX64
Assert-IsProcessX64

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$resolvedPackagePath = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\credential-provider')
}
else {
    Resolve-FullPath $PackagePath
}

$package = Assert-CredentialProviderPackage -PackagePath $resolvedPackagePath -RequireCleanPackage

Write-Host 'Credential Provider package verified.'
Write-Host "Path: $($package.PackageDirectory)"
Write-Host "CLSID: $($package.Manifest.clsid)"
Write-Host "Architecture: $($package.Manifest.architecture)"
Write-Host "PackageId: $($package.Manifest.packageId)"
Write-Host "SHA256: $($package.Sha256)"
Write-Host "Authenticode: $($package.SignatureDiagnostic)"
