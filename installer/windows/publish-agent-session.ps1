#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SessionExecutableName = 'GaltekClassroom.Agent.Session.exe'
$RuntimeIdentifier = 'win-x64'

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-SafePublishPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $RepositoryRoot,
        [Parameter(Mandatory = $true)][bool] $AllowOutsideArtifacts
    )

    $fullPath = Resolve-FullPath $Path
    $artifactsRoot = Resolve-FullPath (Join-Path $RepositoryRoot 'artifacts')
    $artifactsRootWithSeparator = $artifactsRoot.TrimEnd('\') + '\'
    $pathWithSeparator = $fullPath.TrimEnd('\') + '\'
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)

    if ($fullPath -eq $pathRoot) {
        throw "Refusing to clean publish output at filesystem root: $fullPath"
    }

    if (-not $pathWithSeparator.StartsWith($artifactsRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -and -not $AllowOutsideArtifacts) {
        throw "Default publish output must stay under artifacts/: $fullPath"
    }
}

function Resolve-DotNet {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }

    $candidates += (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $fromPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    throw 'dotnet SDK was not found. Install .NET SDK 8.0 or set DOTNET_ROOT.'
}

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$projectPath = Resolve-FullPath (Join-Path $repositoryRoot 'agent\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj')
$publishOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\agent-session')
}
else {
    Resolve-FullPath $OutputPath
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Session Agent project was not found: $projectPath"
}

Assert-SafePublishPath `
    -Path $publishOutputPath `
    -RepositoryRoot $repositoryRoot `
    -AllowOutsideArtifacts (-not [string]::IsNullOrWhiteSpace($OutputPath))

if (Test-Path -LiteralPath $publishOutputPath) {
    Remove-Item -LiteralPath $publishOutputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $publishOutputPath -Force | Out-Null

$dotnet = Resolve-DotNet

& $dotnet publish $projectPath `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $publishOutputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$sessionExecutablePath = Join-Path $publishOutputPath $SessionExecutableName
if (-not (Test-Path -LiteralPath $sessionExecutablePath -PathType Leaf)) {
    throw "Publish did not produce expected executable: $sessionExecutablePath"
}

Write-Host "Session Agent artifact published."
Write-Host "Runtime: $RuntimeIdentifier"
Write-Host "Self-contained: true"
Write-Host "Path: $publishOutputPath"
