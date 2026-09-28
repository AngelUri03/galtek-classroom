#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-F3A {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "F3A_SERVICE_IMAGEPATH_REGRESSION_FAILED: $Message" }
}

. (Join-Path $PSScriptRoot 'setup\legacy-adoption.ps1')

$root = 'C:\Program Files\Galtek\Classroom\Agent'
$pc14ImagePath = 'C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe'

# Exact physical failure #3: the legacy SCM value had no surrounding quotes,
# so ordinary Windows argv parsing produced C:\Program and rejected adoption.
$ordinaryToken = ConvertFrom-GaltekWindowsCommandLineExecutable -CommandLine $pc14ImagePath
Assert-F3A ($ordinaryToken.IsValid -and $ordinaryToken.ExecutablePath -eq 'C:\Program') 'Fixture no longer reproduces the physical PC14 token boundary.'

$serviceResult = Test-GaltekLegacyServiceImagePath -CommandLine $pc14ImagePath -ExpectedRoot $root
Assert-F3A ($serviceResult.IsValid -and $serviceResult.Component -eq 'SERVICE' -and $serviceResult.Path -eq $pc14ImagePath) 'Exact unquoted PC14 Service ImagePath was not adopted.'

$plan = Get-GaltekLegacyAdoptionPlan -InstallRoot $root -State ([pscustomobject]@{
    ServiceExists = $true
    LegacyServiceExists = $false
    ServiceImagePath = $pc14ImagePath
    SessionTaskExists = $false
    SessionExecutablePath = $null
    CredentialProviderRegistered = $false
    CredentialProviderPath = $null
})
Assert-F3A ($plan.IsLegacyInstallation -and $plan.IsValid -and $plan.Reason -eq 'VALID_LEGACY_INSTALLATION') 'Real adoption plan still rejects the exact PC14 topology.'

$quotedWithArguments = Test-GaltekLegacyServiceImagePath -CommandLine ('"' + $pc14ImagePath + '" --service-mode') -ExpectedRoot $root
Assert-F3A $quotedWithArguments.IsValid 'F2 quoted Service command-line parsing regressed.'

foreach ($rejected in @(
    ($pc14ImagePath + ' --service-mode'),
    'C:\OtherVendor\GaltekClassroom.Agent.Service.exe',
    'C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe.evil'
)) {
    $result = Test-GaltekLegacyServiceImagePath -CommandLine $rejected -ExpectedRoot $root
    Assert-F3A (-not $result.IsValid -and $result.Component -eq 'SERVICE') "Unsafe or ambiguous ImagePath was accepted: $rejected"
}

Write-Output 'F3A Service ImagePath regression passed: exact unquoted PC14 path adopted; arguments, external roots, and lookalike filenames rejected.'
