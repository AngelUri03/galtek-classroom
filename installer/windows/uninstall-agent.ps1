#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $PurgeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'uninstall-session-agent.ps1')

$serviceArgs = @{}
if ($PurgeData) {
    $serviceArgs.PurgeData = $true
}

& (Join-Path $PSScriptRoot 'uninstall-agent-service.ps1') @serviceArgs

Write-Host "Full Agent uninstall completed."
