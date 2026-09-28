#Requires -Version 5.1
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-F3C([bool]$Condition,[string]$Message) { if(-not $Condition){ throw "F3C_ROLLBACK_FINAL_FAILED: $Message" } }
. (Join-Path $PSScriptRoot 'setup\client-state.ps1')
$script:fixture = [pscustomobject]@{
    Services = @([pscustomobject]@{ Name='GaltekClassroomAgent'; Exists=$true; WasRunning=$true; PathName='C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe'; StartName='LocalSystem'; StartMode='Auto'; DelayedAutoStart=1; FailureActionsBase64='fixture-5-15-60'; FailureFlag=1 })
    SessionTask = [pscustomobject]@{ Exists=$true; WasEnabled=$true; WasRunning=$true }
    CredentialProvider = [pscustomobject]@{ ProviderDefault=[pscustomobject]@{ValueExists=$true} }
}
$script:calls = [Collections.Generic.List[string]]::new()
$script:removed = $false
$script:failServiceRuntime = $false
$script:runtimeStates = [Collections.Generic.List[bool]]::new()
function Read-GaltekRollbackState { return $script:fixture }
function Remove-GaltekRollbackState { $script:removed = $true; $script:calls.Add('REMOVE') }
function Restore-GaltekServiceConfiguration { param($Snapshot) $script:calls.Add('SERVICE_CONFIG') }
function Restore-GaltekServiceRuntime { param($Snapshot) $script:calls.Add('SERVICE_RUNTIME'); $script:runtimeStates.Add([bool]$Snapshot.WasRunning); if($script:failServiceRuntime){throw 'SERVICE_START_FAILED'} }
function Restore-GaltekTaskState { param($Snapshot) $script:calls.Add('SESSION') }
function Restore-GaltekCredentialProviderState { param($Snapshot) $script:calls.Add('CP') }
function Restore-GaltekServiceRegistryState { param($Snapshot) $script:calls.Add('REGISTRY') }
function Assert-GaltekRollbackStateRestored { param($Plan) $script:calls.Add('VERIFY') }
$service = $script:fixture.Services[0]
Assert-F3C ($service.PathName -eq 'C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe' -and $service.WasRunning) 'PC14 pre-state fixture changed.'
Assert-F3C ($service.StartMode -eq 'Auto' -and $service.DelayedAutoStart -eq 1 -and $service.StartName -eq 'LocalSystem') 'Service config fixture changed.'
Invoke-GaltekRollbackState | Out-Null
Assert-F3C $script:removed 'Snapshot was not deleted after complete success.'
foreach($expected in @('SERVICE_CONFIG','SERVICE_RUNTIME','SESSION','CP','REGISTRY','VERIFY','REMOVE')) { Assert-F3C ($script:calls.Contains($expected)) "Missing successful phase: $expected" }
$script:calls.Clear(); $script:removed=$false
Invoke-GaltekRollbackState | Out-Null
Assert-F3C ($script:removed -and $script:calls.Count -eq 7) 'Second rollback did not converge idempotently.'
# Stopped pre-state remains stopped; runtime restore receives the captured value.
$script:fixture.Services[0].WasRunning = $false; $script:calls.Clear(); $script:removed=$false; $script:runtimeStates.Clear()
Invoke-GaltekRollbackState | Out-Null
Assert-F3C ($script:runtimeStates.Count -eq 1 -and -not $script:runtimeStates[0]) 'Stopped pre-state was not preserved.'
$script:fixture.Services[0].WasRunning = $true
$script:calls.Clear(); $script:removed=$false; $script:failServiceRuntime=$true
$failed=$false
try { Invoke-GaltekRollbackState | Out-Null } catch { Write-Output "EXPECTED_FAILURE=$($_.Exception.Message)"; $failed = $_.Exception.Message -match 'GALTEK_ROLLBACK_FINAL_FAILED' }
Assert-F3C $failed 'Independent component failure was not returned.'
Assert-F3C (-not $script:removed) 'Snapshot was deleted after partial rollback failure.'
Assert-F3C ($script:calls.Contains('SESSION') -and $script:calls.Contains('CP')) 'A Service failure skipped later components.'
$stateSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup\client-state.ps1') -Raw
foreach($stage in @('BEGIN','SERVICE_CONFIG','SERVICE_RUNTIME','SESSION','CP','REGISTRY','VERIFY','COMPLETE')) { Assert-F3C ($stateSource.Contains("GALTEK_ROLLBACK_FINAL_STEP=$stage") -or $stateSource.Contains("-Step '$stage'")) "Diagnostic stage missing: $stage" }
Write-Output 'F3C_ROLLBACK_FINAL_TESTS_PASS running stopped service task CP registry independent-errors snapshot-policy idempotent'
