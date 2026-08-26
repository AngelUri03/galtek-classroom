#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ServiceArtifactPath,
    [string] $SessionArtifactPath,
    [switch] $NoStartCurrentSession
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($ServiceArtifactPath)) {
    $serviceArgs.ArtifactPath = $ServiceArtifactPath
}

$sessionArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($SessionArtifactPath)) {
    $sessionArgs.ArtifactPath = $SessionArtifactPath
}

if ($NoStartCurrentSession) {
    $sessionArgs.NoStartCurrentSession = $true
}

& (Join-Path $PSScriptRoot 'install-agent-service.ps1') @serviceArgs
& (Join-Path $PSScriptRoot 'install-session-agent.ps1') @sessionArgs

Write-Host "Full Agent install/update completed."
