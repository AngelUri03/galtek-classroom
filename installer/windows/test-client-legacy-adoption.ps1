#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Legacy {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "LEGACY_ADOPTION_FIXTURE_FAILED: $Message" }
}

. (Join-Path $PSScriptRoot 'setup\legacy-adoption.ps1')

$root = 'C:\Program Files\Galtek\Classroom\Agent'
$servicePath = Join-Path $root 'GaltekClassroom.Agent.Service.exe'
$sessionPath = Join-Path $root 'Session\GaltekClassroom.Agent.Session.exe'
$cpPath = Join-Path $root 'CredentialProvider\versions\sha256-fixture\GaltekClassroom.CredentialProvider.dll'

$quoted = ConvertFrom-GaltekWindowsCommandLineExecutable -CommandLine ('"' + $servicePath + '"')
Assert-Legacy ($quoted.IsValid -and $quoted.ExecutablePath -eq $servicePath -and $quoted.Arguments -eq '') 'Quoted ImagePath with spaces was not parsed exactly.'
$withArguments = ConvertFrom-GaltekWindowsCommandLineExecutable -CommandLine ('  "' + $servicePath + '" --service-mode   ')
Assert-Legacy ($withArguments.IsValid -and $withArguments.ExecutablePath -eq $servicePath -and $withArguments.Arguments -eq '--service-mode') 'Quoted ImagePath arguments or trailing whitespace contaminated the executable.'
$unquoted = ConvertFrom-GaltekWindowsCommandLineExecutable -CommandLine 'C:\Galtek\Agent.exe --background'
Assert-Legacy ($unquoted.IsValid -and $unquoted.ExecutablePath -eq 'C:\Galtek\Agent.exe' -and $unquoted.Arguments -eq '--background') 'Valid unquoted ImagePath was not parsed using Windows token boundaries.'

$pc14 = Get-GaltekLegacyAdoptionPlan -InstallRoot $root -State ([pscustomobject]@{
    ServiceExists = $true
    LegacyServiceExists = $false
    ServiceImagePath = '  "C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"   '
    SessionTaskExists = $true
    SessionExecutablePath = $sessionPath
    SessionArguments = '--background'
    SessionWorkingDirectory = (Join-Path $root 'Session')
    CredentialProviderRegistered = $true
    CredentialProviderPath = '  "C:\Program Files\Galtek\Classroom\Agent\CredentialProvider\versions\sha256-fixture\GaltekClassroom.CredentialProvider.dll"  '
})
Assert-Legacy ($pc14.IsLegacyInstallation -and $pc14.IsValid -and $pc14.Reason -eq 'VALID_LEGACY_INSTALLATION') 'The realistic PC14 topology must be adoptable.'

$serviceWithArguments = Get-GaltekLegacyAdoptionPlan -InstallRoot $root -State ([pscustomobject]@{
    ServiceExists = $true; LegacyServiceExists = $false
    ServiceImagePath = '"C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe" --service-mode  '
    SessionTaskExists = $false; SessionExecutablePath = $null
    CredentialProviderRegistered = $false; CredentialProviderPath = $null
})
Assert-Legacy $serviceWithArguments.IsValid 'Service arguments after a quoted executable must not become part of the path.'

$negativeCases = @(
    [pscustomobject]@{ Component = 'SERVICE'; State = [pscustomobject]@{
        ServiceExists = $true; LegacyServiceExists = $false; ServiceImagePath = 'C:\Temp\evil.exe'
        SessionTaskExists = $false; SessionExecutablePath = $null; CredentialProviderRegistered = $false; CredentialProviderPath = $null
    } },
    [pscustomobject]@{ Component = 'SESSION'; State = [pscustomobject]@{
        ServiceExists = $false; LegacyServiceExists = $false; ServiceImagePath = $null
        SessionTaskExists = $true; SessionExecutablePath = 'C:\Windows\System32\cmd.exe'; CredentialProviderRegistered = $false; CredentialProviderPath = $null
    } },
    [pscustomobject]@{ Component = 'CP'; State = [pscustomobject]@{
        ServiceExists = $false; LegacyServiceExists = $false; ServiceImagePath = $null
        SessionTaskExists = $false; SessionExecutablePath = $null; CredentialProviderRegistered = $true; CredentialProviderPath = 'C:\OtherVendor\provider.dll'
    } }
)
foreach ($case in $negativeCases) {
    $plan = Get-GaltekLegacyAdoptionPlan -InstallRoot $root -State $case.State
    Assert-Legacy ($plan.IsLegacyInstallation -and -not $plan.IsValid) "$($case.Component) external fixture was accepted."
    Assert-Legacy ($plan.Component -eq $case.Component -and $plan.Reason -eq 'PATH_OUTSIDE_GALTEK_ROOT') "$($case.Component) rejection is not typed."
    Assert-Legacy ([IO.Path]::IsPathRooted($plan.Path)) "$($case.Component) rejection did not retain a safe normalized local path."
}

Write-Output 'Legacy adoption fixtures passed: realistic PC14 topology adoptable; SERVICE/SESSION/CP external paths rejected.'
