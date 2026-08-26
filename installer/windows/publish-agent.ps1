#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ServiceOutputPath,
    [string] $SessionOutputPath
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

& (Join-Path $PSScriptRoot 'publish-agent-service.ps1') @serviceArgs
& (Join-Path $PSScriptRoot 'publish-agent-session.ps1') @sessionArgs

Write-Host "Full Agent artifacts published."
