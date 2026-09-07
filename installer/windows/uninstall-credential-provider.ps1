#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'credential-provider-common.ps1')

function Remove-CredentialProviderPackagesBestEffort {
    $rebootRecommended = $false
    $installRoot = Get-CredentialProviderInstallRoot

    if (-not (Test-Path -LiteralPath $installRoot)) {
        return $false
    }

    Assert-PathInsideCredentialProviderRoot -Path $installRoot -Purpose 'Credential Provider install root'

    try {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
        Write-Host "Removed Credential Provider binaries: $installRoot"
    }
    catch {
        $rebootRecommended = $true
        Write-Warning "UNREGISTERED_REBOOT_CLEANUP_REQUIRED: registration was removed, but one or more Credential Provider files could not be deleted. $($_.Exception.Message)"
    }

    return $rebootRecommended
}

Assert-IsWindowsX64
Assert-IsProcessX64
Assert-IsElevated

Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderRegistrySubKey
if (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderRegistrySubKey) {
    throw 'Credential Provider registration removal failed.'
}

Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey
if (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey) {
    throw 'Credential Provider COM registration removal failed.'
}

$rebootRecommended = Remove-CredentialProviderPackagesBestEffort

Write-Host 'Credential Provider unregistered.'
Write-Host "CLSID: $script:GaltekCredentialProviderClsid"
Write-Host "rebootRecommended: $($rebootRecommended.ToString().ToLowerInvariant())"
