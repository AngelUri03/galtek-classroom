#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ServiceArtifactPath,
    [string] $SessionArtifactPath,
    [string] $CredentialProviderArtifactPath,
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

$credentialProviderArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($CredentialProviderArtifactPath)) {
    $credentialProviderArgs.PackagePath = $CredentialProviderArtifactPath
}

& (Join-Path $PSScriptRoot 'install-agent-service.ps1') @serviceArgs
& (Join-Path $PSScriptRoot 'install-session-agent.ps1') @sessionArgs
try {
    & (Join-Path $PSScriptRoot 'install-credential-provider.ps1') @credentialProviderArgs
    & (Join-Path $PSScriptRoot 'test-credential-provider-installation.ps1')
}
catch {
    Write-Warning "Agent Service and Session Agent may be installed, but Credential Provider installation failed. The full Agent install is partial and not complete. $($_.Exception.Message)"
    throw
}

Write-Host "Full Agent install/update completed."
