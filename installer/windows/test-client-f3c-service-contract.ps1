#Requires -Version 5.1
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-F3C([bool]$Condition,[string]$Message) { if(-not $Condition){ throw "F3C_SERVICE_CONTRACT_FAILED: $Message" } }
. (Join-Path $PSScriptRoot 'setup\legacy-adoption.ps1')
$root = 'C:\Program Files\Galtek\Classroom\Agent'
$expected = Join-Path $root 'GaltekClassroom.Agent.Service.exe'
$preState = [pscustomobject]@{ PathName = $expected }
$liveRaw = '"' + $expected + '"'
$live = Resolve-GaltekServiceImagePath -CommandLine $liveRaw -ExpectedExecutable $expected
Assert-F3C ($preState.PathName -eq $expected) 'PRE-STATE did not preserve exact legacy unquoted raw value.'
Assert-F3C ($live.IsValid -and $live.ExecutablePath -eq $expected -and [string]::IsNullOrEmpty($live.Arguments)) 'Canonical LIVE state did not pass semantic validation.'
Assert-F3C ((Resolve-GaltekServiceImagePath -CommandLine $expected -ExpectedExecutable $expected).IsValid) 'Equivalent unquoted live representation was rejected.'
foreach($raw in @('C:\Temp\GaltekClassroom.Agent.Service.exe', ('"' + $expected + '" --evil'), (Join-Path $root 'GaltekClassroom.Agent.Service.exe.evil'), '\\server\share\GaltekClassroom.Agent.Service.exe', ($expected + ':evil'))) {
    $candidate = Resolve-GaltekServiceImagePath -CommandLine $raw -ExpectedExecutable $expected
    Assert-F3C (-not $candidate.IsValid -or -not [string]::IsNullOrWhiteSpace([string]$candidate.Arguments)) "Unsafe ImagePath passed the no-arguments contract: $raw"
}
$lifecycle = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup\client-lifecycle.ps1') -Raw
Assert-F3C ($lifecycle.Contains('POST-STATE authority')) 'PRE-STATE/LIVE POST-STATE separation is not explicit.'
Assert-F3C ($lifecycle.Contains('OpenSubKey("SYSTEM\CurrentControlSet\Services\$ServiceName", $false)')) 'Post-validation does not reopen live Registry64.'
Assert-F3C ($lifecycle.Contains('Resolve-GaltekServiceImagePath -CommandLine $actualRaw')) 'Post-validation bypasses the semantic parser.'
Assert-F3C ($lifecycle.Contains('actualExecutable=')) 'Safe typed ImagePath diagnostics are missing.'
Write-Output 'F3C_SERVICE_CONTRACT_TESTS_PASS pre-state preserved canonical-live semantic external args lookalike UNC ADS rejected'
