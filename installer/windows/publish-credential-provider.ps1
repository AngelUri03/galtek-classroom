#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'credential-provider-common.ps1')

function Resolve-MSBuild {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:VSINSTALLDIR)) {
        $candidates += (Join-Path $env:VSINSTALLDIR 'MSBuild\Current\Bin\MSBuild.exe')
    }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
            $installationPath = & $vswhere `
                -latest `
                -products * `
                -requires Microsoft.Component.MSBuild `
                -property installationPath

            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
                $candidates += (Join-Path ([string] $installationPath) 'MSBuild\Current\Bin\MSBuild.exe')
            }
        }
    }

    $fromPath = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        $candidates += $fromPath.Source
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-FullPath $candidate)
        }
    }

    throw 'MSBuild was not found. Install Visual Studio Build Tools with the C++ desktop workload.'
}

Assert-IsWindowsX64
Assert-IsProcessX64

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$projectPath = Resolve-FullPath (Join-Path $repositoryRoot 'agent\native\GaltekClassroom.CredentialProvider\GaltekClassroom.CredentialProvider.vcxproj')
$publishOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\credential-provider')
}
else {
    Resolve-FullPath $OutputPath
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Credential Provider project was not found: $projectPath"
}

Assert-SafePublishPath `
    -Path $publishOutputPath `
    -RepositoryRoot $repositoryRoot `
    -AllowOutsideArtifacts (-not [string]::IsNullOrWhiteSpace($OutputPath))

$msbuild = Resolve-MSBuild

& $msbuild $projectPath `
    /m `
    /restore `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /p:RuntimeLibrary=MultiThreaded

if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$buildOutputDirectory = Resolve-FullPath (Join-Path (Split-Path -Parent $projectPath) 'x64\Release')
$sourceDllPath = Join-Path $buildOutputDirectory $script:GaltekCredentialProviderFileName
if (-not (Test-Path -LiteralPath $sourceDllPath -PathType Leaf)) {
    throw "MSBuild did not produce expected DLL: $sourceDllPath"
}

Assert-PeX64 -Path $sourceDllPath

if (Test-Path -LiteralPath $publishOutputPath) {
    Remove-Item -LiteralPath $publishOutputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $publishOutputPath -Force | Out-Null

$publishedDllPath = Join-Path $publishOutputPath $script:GaltekCredentialProviderFileName
Copy-Item -LiteralPath $sourceDllPath -Destination $publishedDllPath -Force

$sha256 = Get-FileSha256 -Path $publishedDllPath
$packageId = "sha256-$($sha256.Substring(0, 16))"
$signature = Assert-AuthenticodeAcceptable -Path $publishedDllPath

$manifest = [ordered]@{
    schemaVersion = $script:GaltekCredentialProviderPackageSchemaVersion
    product = $script:GaltekCredentialProviderProduct
    component = $script:GaltekCredentialProviderComponent
    clsid = $script:GaltekCredentialProviderClsid
    architecture = $script:GaltekCredentialProviderArchitecture
    fileName = $script:GaltekCredentialProviderFileName
    sha256 = $sha256
    packageId = $packageId
}

$manifestPath = Join-Path $publishOutputPath $script:GaltekCredentialProviderManifestName
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$package = Assert-CredentialProviderPackage -PackagePath $publishOutputPath -RequireCleanPackage

Write-Host 'Credential Provider artifact published.'
Write-Host 'Configuration: Release'
Write-Host 'Platform: x64'
Write-Host "RuntimeLibrary: MultiThreaded (/MT)"
Write-Host "Path: $publishOutputPath"
Write-Host "PackageId: $($package.Manifest.packageId)"
Write-Host "SHA256: $($package.Sha256)"
Write-Host "Authenticode: $($package.SignatureDiagnostic)"
