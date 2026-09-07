#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'credential-provider-common.ps1')

function Get-RegistrationState {
    return [pscustomobject]@{
        ProviderExists = Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderRegistrySubKey
        ProviderName = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderRegistrySubKey -Name ''
        ClsidExists = Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey
        ClsidName = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey -Name ''
        InprocExists = Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey
        InprocPath = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name ''
        ThreadingModel = Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name 'ThreadingModel'
    }
}

function Restore-RegistrationState {
    param([Parameter(Mandatory = $true)] $State)

    if ($State.ProviderExists) {
        Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderRegistrySubKey -Name '' -Value ([string] $State.ProviderName)
    }
    else {
        Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderRegistrySubKey
    }

    if ($State.ClsidExists) {
        Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey -Name '' -Value ([string] $State.ClsidName)

        if ($State.InprocExists) {
            Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name '' -Value ([string] $State.InprocPath)
            Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name 'ThreadingModel' -Value ([string] $State.ThreadingModel)
        }
        else {
            Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey
        }
    }
    else {
        Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey
    }
}

function Assert-NoRegistrationConflict {
    param([Parameter(Mandatory = $true)] $State)

    if (-not [string]::IsNullOrWhiteSpace([string] $State.InprocPath)) {
        $currentPath = Resolve-FullPath ([string] $State.InprocPath)
        $installRoot = Get-CredentialProviderInstallRoot
        $currentPathWithSeparator = $currentPath.TrimEnd('\') + '\'
        $installRootWithSeparator = $installRoot.TrimEnd('\') + '\'

        if (-not $currentPathWithSeparator.StartsWith($installRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "CREDENTIAL_PROVIDER_REGISTRATION_CONFLICT: Galtek CLSID already points outside the Galtek CredentialProvider install root. Current InprocServer32: $currentPath"
        }
    }
}

function Copy-PackageIfNeeded {
    param(
        [Parameter(Mandatory = $true)] $Package,
        [Parameter(Mandatory = $true)][string] $DestinationDirectory
    )

    $destinationDll = Join-Path $DestinationDirectory $script:GaltekCredentialProviderFileName
    $destinationManifest = Join-Path $DestinationDirectory $script:GaltekCredentialProviderManifestName

    if (Test-Path -LiteralPath $DestinationDirectory) {
        if ((Test-Path -LiteralPath $destinationDll -PathType Leaf) -and (Test-Path -LiteralPath $destinationManifest -PathType Leaf)) {
            $existingPackage = Assert-CredentialProviderPackage -PackagePath $DestinationDirectory
            if ($existingPackage.Sha256 -eq $Package.Sha256 -and [string] $existingPackage.Manifest.packageId -eq [string] $Package.Manifest.packageId) {
                return
            }
        }

        throw "CREDENTIAL_PROVIDER_PACKAGE_ID_COLLISION: immutable package directory already exists with different contents: $DestinationDirectory"
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Package.DllPath -Destination $destinationDll -Force
    Copy-Item -LiteralPath $Package.ManifestPath -Destination $destinationManifest -Force
}

function Set-CredentialProviderRegistration {
    param([Parameter(Mandatory = $true)][string] $DllPath)

    Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey -Name '' -Value $script:GaltekCredentialProviderName
    Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name '' -Value $DllPath
    Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name 'ThreadingModel' -Value 'Apartment'

    if ((Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey -Name '') -ne $script:GaltekCredentialProviderName) {
        throw 'COM registration read-back failed for CLSID default value.'
    }

    if ((Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name '') -ne $DllPath) {
        throw 'COM registration read-back failed for InprocServer32.'
    }

    if ((Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderInprocRegistrySubKey -Name 'ThreadingModel') -ne 'Apartment') {
        throw 'COM registration read-back failed for ThreadingModel.'
    }

    Set-Hklm64StringValue -SubKey $script:GaltekCredentialProviderRegistrySubKey -Name '' -Value $script:GaltekCredentialProviderName

    if ((Get-Hklm64StringValue -SubKey $script:GaltekCredentialProviderRegistrySubKey -Name '') -ne $script:GaltekCredentialProviderName) {
        throw 'Credential Provider registration read-back failed.'
    }
}

Assert-IsWindowsX64
Assert-IsProcessX64
Assert-IsElevated

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$resolvedPackagePath = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\credential-provider')
}
else {
    Resolve-FullPath $PackagePath
}

$package = Assert-CredentialProviderPackage -PackagePath $resolvedPackagePath -RequireCleanPackage

if (Test-Hklm64SubKey -SubKey $script:GaltekCredentialProviderFilterRegistrySubKey) {
    throw 'Credential Provider Filter registration must not exist for Galtek CLSID. Remove the filter in a controlled recovery/lab flow before product install.'
}

$service = Get-Service -Name $script:GaltekAgentServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    throw "Credential Provider install requires Agent Service '$script:GaltekAgentServiceName' to be installed first."
}

$previousState = Get-RegistrationState
Assert-NoRegistrationConflict -State $previousState

$versionsRoot = Get-CredentialProviderVersionsRoot
$packageDirectory = Resolve-FullPath (Join-Path $versionsRoot ([string] $package.Manifest.packageId))
$stagedDllPath = Resolve-FullPath (Join-Path $packageDirectory $script:GaltekCredentialProviderFileName)
$stagedManifestPath = Resolve-FullPath (Join-Path $packageDirectory $script:GaltekCredentialProviderManifestName)

Assert-PathInsideCredentialProviderRoot -Path $packageDirectory -Purpose 'Credential Provider package directory'
Assert-PathInsideCredentialProviderRoot -Path $stagedDllPath -Purpose 'Credential Provider DLL'
Assert-PathInsideCredentialProviderRoot -Path $stagedManifestPath -Purpose 'Credential Provider manifest'

$registrationChanged = $false
try {
    Copy-PackageIfNeeded -Package $package -DestinationDirectory $packageDirectory
    $stagedPackage = Assert-CredentialProviderPackage -PackagePath $packageDirectory
    Assert-NoStandardUserWriteAccess -Paths @((Get-CredentialProviderInstallRoot), $versionsRoot, $packageDirectory, $stagedDllPath, $stagedManifestPath)

    Set-CredentialProviderRegistration -DllPath $stagedDllPath
    $registrationChanged = $true

    if (-not (Test-Path -LiteralPath $stagedDllPath -PathType Leaf)) {
        throw "Staged Credential Provider DLL disappeared after registration: $stagedDllPath"
    }
}
catch {
    Write-Warning "Credential Provider install failed; restoring previous Galtek registration when possible. $($_.Exception.Message)"
    Restore-RegistrationState -State $previousState

    if (-not $previousState.ProviderExists -and -not $previousState.ClsidExists) {
        Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderRegistrySubKey
        Remove-Hklm64SubKeyTree -SubKey $script:GaltekCredentialProviderClsidRegistrySubKey
    }

    if (-not $registrationChanged -and (Test-Path -LiteralPath $packageDirectory)) {
        try {
            Remove-Item -LiteralPath $packageDirectory -Recurse -Force
        }
        catch {
            Write-Warning "Best-effort cleanup left staged package directory: $packageDirectory"
        }
    }

    throw
}

Write-Host 'Credential Provider installed/updated.'
Write-Host "CLSID: $script:GaltekCredentialProviderClsid"
Write-Host "PackageId: $($stagedPackage.Manifest.packageId)"
Write-Host "DLL: $stagedDllPath"
Write-Host "SHA256: $($stagedPackage.Sha256)"
Write-Host "Authenticode: $($stagedPackage.SignatureDiagnostic)"
Write-Host 'rebootRecommended: false'
