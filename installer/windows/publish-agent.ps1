#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ServiceOutputPath,
    [string] $SessionOutputPath,
    [string] $CredentialProviderOutputPath,
    [switch] $SkipCredentialProvider
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($ServiceOutputPath)) {
    $serviceArgs.OutputPath = $ServiceOutputPath
}

$sessionArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($SessionOutputPath)) {
    $sessionArgs.OutputPath = $SessionOutputPath
}

$credentialProviderArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($CredentialProviderOutputPath)) {
    $credentialProviderArgs.OutputPath = $CredentialProviderOutputPath
}

& (Join-Path $PSScriptRoot 'publish-agent-service.ps1') @serviceArgs
& (Join-Path $PSScriptRoot 'publish-agent-session.ps1') @sessionArgs
if ($SkipCredentialProvider) {
    Write-Warning 'Skipping Credential Provider publish by explicit dev request. Product publish must not use this switch.'
}
else {
    & (Join-Path $PSScriptRoot 'publish-credential-provider.ps1') @credentialProviderArgs
}

Write-Host "Full Agent artifacts published."
