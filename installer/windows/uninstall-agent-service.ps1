#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $PurgeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'GaltekClassroomAgent'
$LegacyServiceNames = @('GaltekClassroomAgentService')
$PreservedInstallSubdirectories = @('Session', 'CredentialProvider')
$NetworkIdentityFileName = 'network-identity.json'
$NetworkIdentityKeyNamePrefix = 'GaltekClassroom.NetworkIdentity.'

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideDirectory {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ParentDirectory,
        [Parameter(Mandatory = $true)][string] $Purpose
    )

    $fullPath = Resolve-FullPath $Path
    $fullParent = (Resolve-FullPath $ParentDirectory).TrimEnd('\') + '\'
    $fullPathWithSeparator = $fullPath.TrimEnd('\') + '\'

    if (-not $fullPathWithSeparator.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay under $fullParent. Actual path: $fullPath"
    }
}

function Invoke-ScExe {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    & sc.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Status,
        [int] $TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status.ToString() -eq $Status) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    $actual = if ($null -eq $service) { 'NotFound' } else { $service.Status.ToString() }
    throw "Service $Name did not reach status $Status within $TimeoutSeconds seconds. Current status: $actual"
}

function Wait-ServiceDeleted {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [int] $TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Service $Name was not removed within $TimeoutSeconds seconds."
}

function Remove-ServiceIfPresent {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stopping service $Name..."
        Stop-Service -Name $Name -Force -ErrorAction Stop
        Wait-ServiceStatus -Name $Name -Status 'Stopped' -TimeoutSeconds 30
    }

    Write-Host "Removing service registration $Name..."
    Invoke-ScExe -Arguments @('delete', $Name)
    Wait-ServiceDeleted -Name $Name -TimeoutSeconds 30
}

function Remove-AgentServiceBinaries {
    param(
        [Parameter(Mandatory = $true)][string] $InstallDirectory,
        [Parameter(Mandatory = $true)][string[]] $PreservedSubdirectories
    )

    if (-not (Test-Path -LiteralPath $InstallDirectory)) {
        Write-Host "Binaries were not present: $InstallDirectory"
        return
    }

    foreach ($item in Get-ChildItem -LiteralPath $InstallDirectory -Force) {
        if ($item.PSIsContainer -and ($PreservedSubdirectories | Where-Object { [string]::Equals($item.Name, $_, [System.StringComparison]::OrdinalIgnoreCase) })) {
            continue
        }

        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }

    if (@(Get-ChildItem -LiteralPath $InstallDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $InstallDirectory -Force
        Write-Host "Removed binaries: $InstallDirectory"
    }
    else {
        Write-Host "Removed Agent Service binaries and preserved lifecycle-owned subdirectories: $(($PreservedSubdirectories | ForEach-Object { Join-Path $InstallDirectory $_ }) -join ', ')"
    }
}

function Remove-NetworkIdentityKeyIfPresent {
    param([Parameter(Mandatory = $true)][string] $DataDirectory)

    $metadataPath = Join-Path $DataDirectory $NetworkIdentityFileName
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        Write-Host "Network Identity metadata was not present: $metadataPath"
        return
    }

    try {
        $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        $keyNameProperty = $metadata.PSObject.Properties['keyName']

        if ($null -eq $keyNameProperty -or [string]::IsNullOrWhiteSpace([string] $keyNameProperty.Value)) {
            Write-Warning "Network Identity key was not purged because $NetworkIdentityFileName does not contain a valid keyName."
            return
        }

        $keyName = [string] $keyNameProperty.Value
        if (-not $keyName.StartsWith($NetworkIdentityKeyNamePrefix, [System.StringComparison]::Ordinal)) {
            Write-Warning "Network Identity key was not purged because keyName does not use the Galtek prefix."
            return
        }

        $invalidKeyNameCharacters = [char[]] '\/:*?"<>|'
        if ($keyName.IndexOfAny($invalidKeyNameCharacters) -ge 0) {
            Write-Warning "Network Identity key was not purged because keyName contains invalid characters."
            return
        }

        $provider = [System.Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider
        $openOptions = [System.Security.Cryptography.CngKeyOpenOptions]::MachineKey

        if (-not [System.Security.Cryptography.CngKey]::Exists($keyName, $provider, $openOptions)) {
            Write-Warning "Network Identity CNG key was already absent: $keyName"
            return
        }

        $key = [System.Security.Cryptography.CngKey]::Open($keyName, $provider, $openOptions)
        try {
            $key.Delete()
            Write-Host "Purged Network Identity CNG key: $keyName"
        }
        finally {
            if ($null -ne $key) {
                $key.Dispose()
            }
        }
    }
    catch {
        Write-Warning "Network Identity key was not purged safely: $($_.Exception.Message)"
    }
}

if (-not (Test-IsElevated)) {
    Write-Error 'This uninstaller must be run from an elevated PowerShell session. Open PowerShell as Administrator and run the script again.'
    exit 1
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$commonApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$installDirectory = Resolve-FullPath (Join-Path $programFiles 'Galtek\Classroom\Agent')
$dataDirectory = Resolve-FullPath (Join-Path $commonApplicationData 'Galtek\Classroom')

Assert-PathInsideDirectory -Path $installDirectory -ParentDirectory $programFiles -Purpose 'Agent Service install directory'
Assert-PathInsideDirectory -Path $dataDirectory -ParentDirectory $commonApplicationData -Purpose 'Agent data directory'

Remove-ServiceIfPresent -Name $ServiceName
foreach ($legacyServiceName in $LegacyServiceNames) {
    Remove-ServiceIfPresent -Name $legacyServiceName
}

Remove-AgentServiceBinaries -InstallDirectory $installDirectory -PreservedSubdirectories $PreservedInstallSubdirectories

if ($PurgeData) {
    Write-Warning 'PurgeData elimina Installation Identity, Commercial License, Master Windows Binding y Network Identity. La instalacion resultante requerira una nueva activacion, reconfiguracion Master y nueva identidad de red.'

    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-NetworkIdentityKeyIfPresent -DataDirectory $dataDirectory
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
        Write-Host "Purged data: $dataDirectory"
    }
    else {
        Write-Host "Data directory was not present: $dataDirectory"
    }
}
else {
    Write-Host "ProgramData preserved: $dataDirectory"
}

Write-Host "Agent Service uninstall completed."
