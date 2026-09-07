#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'credential-provider-common.ps1')

Assert-IsWindowsX64
Assert-IsProcessX64

if (-not (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderRegistrySubKey)) {
    throw 'Credential Provider registry key is missing.'
}

if (-not (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey)) {
    throw 'Credential Provider COM CLSID key is missing.'
}

if (-not (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey)) {
    throw 'Credential Provider COM InprocServer32 key is missing.'
}

$providerName = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderRegistrySubKey -Name ''
$clsidName = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey -Name ''
$inprocPath = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name ''
$threadingModel = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name 'ThreadingModel'

if ($providerName -ne $script:GaltekCredentialProviderName) {
    throw "Credential Provider display name mismatch. Actual: $providerName"
}

if ($clsidName -ne $script:GaltekCredentialProviderName) {
    throw "COM CLSID display name mismatch. Actual: $clsidName"
}

if ([string]::IsNullOrWhiteSpace([string] $inprocPath)) {
    throw 'COM InprocServer32 path is empty.'
}

if ($threadingModel -ne 'Apartment') {
    throw "COM ThreadingModel must be Apartment. Actual: $threadingModel"
}

$resolvedDllPath = Resolve-FullPath ([string] $inprocPath)
Assert-PathInsideCredentialProviderRoot -Path $resolvedDllPath -Purpose 'Credential Provider InprocServer32'

if (-not (Test-Path -LiteralPath $resolvedDllPath -PathType Leaf)) {
    throw "Credential Provider DLL registered in COM does not exist: $resolvedDllPath"
}

$packageDirectory = Split-Path -Parent $resolvedDllPath
$manifestPath = Join-Path $packageDirectory $script:GaltekCredentialProviderManifestName
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Credential Provider manifest is missing beside active DLL: $manifestPath"
}

$package = Assert-CredentialProviderPackage -PackagePath $packageDirectory
Assert-NoStandardUserWriteAccess -Paths @((Get-CredentialProviderInstallRoot), (Get-CredentialProviderVersionsRoot), $packageDirectory, $resolvedDllPath, $manifestPath)

if (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderFilterRegistrySubKey) {
    throw 'Credential Provider Filter registration must not exist for Galtek CLSID.'
}

Write-Host 'Credential Provider installation verified.'
Write-Host "CLSID: $script:GaltekCredentialProviderClsid"
Write-Host "DLL: $resolvedDllPath"
Write-Host "PackageId: $($package.Manifest.packageId)"
Write-Host "SHA256: $($package.Sha256)"
Write-Host "ThreadingModel: $threadingModel"
Write-Host "Authenticode: $($package.SignatureDiagnostic)"
Write-Host 'Standard provider keys: read-only checker did not create, modify or remove standard Windows provider registrations.'
