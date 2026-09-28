#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-Bridge([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "LEGACY_DETECTION_BRIDGE_FAILED: $Message" }
}
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup\BootstrapperApplication\LegacyDetector.cs') -Raw
$ba = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup\BootstrapperApplication\GaltekBootstrapperApplication.cs') -Raw
Assert-Bridge ($ba.Contains('Task.Run(() => LegacyDetector.Detect())')) 'BA must run exactly one native .NET detection pass in background.'
Assert-Bridge (-not $ba.Contains('ClientStateHelper.exe')) 'BA detection must not start the PowerShell-backed lifecycle helper.'
Assert-Bridge ($source.Contains('RegistryView.Registry64')) 'Detection must use Registry64.'
Assert-Bridge ($source.Contains('Schedule.Service')) 'Detection must query the one Session task directly.'
foreach ($forbidden in @('PowerShell', 'Get-CimInstance', 'Get-WmiObject', 'Directory.GetFiles', 'EnumerateFiles', 'GetFileHash', 'Thread.Sleep')) {
    Assert-Bridge (-not $source.Contains($forbidden)) "Detection contains forbidden expensive mechanism: $forbidden"
}
foreach ($secret in @('password', 'protectedData', 'private key', 'license.dat', 'appsettings.json', 'MasterConnection')) {
    Assert-Bridge (-not $source.Contains($secret)) "Detection references secret-bearing data: $secret"
}
Write-Output 'LEGACY_DETECTION_BRIDGE_TESTS_PASS native-dotnet single-pass no-powershell no-cim no-wmi'
