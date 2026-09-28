#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-F3DRollback([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "F3D_ROLLBACK_REAL_STATE_FAILED: $Message" }
}

. (Join-Path $PSScriptRoot 'setup\client-state.ps1')

$script:pre = [pscustomobject]@{
    Services = @([pscustomobject]@{
        Name='GaltekClassroomAgent'; Exists=$true; WasRunning=$true
        PathName='C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe'
        PathNameKind='ExpandString'; DisplayName='Galtek Classroom Agent Service'
        Description='legacy description'; StartName='LocalSystem'; StartMode='Auto'; DelayedAutoStart=1
        FailureActionsExists=$true; FailureActionsBase64='BQ8PYA=='; FailureFlagExists=$true; FailureFlag=1
    })
    SessionTask = [pscustomobject]@{
        Exists=$true; WasEnabled=$true; WasRunning=$false; DefinitionXmlBase64='legacy-task-xml'
        Execute='C:\Program Files\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe'
        Arguments='--background'; WorkingDirectory='C:\Program Files\Galtek\Classroom\Agent\Session'
    }
    CredentialProvider = [pscustomobject]@{
        ProviderDefault=[pscustomobject]@{Value='Galtek Classroom Credential Provider'}
        FilterDefault=[pscustomobject]@{Value=$null}
        ClassDefault=[pscustomobject]@{Value='Galtek Classroom Credential Provider'}
        InprocDefault=[pscustomobject]@{Value='C:\Program Files\Galtek\Classroom\Agent\CredentialProvider\versions\legacy\GaltekClassroom.CredentialProvider.dll'}
        ThreadingModel=[pscustomobject]@{Value='Apartment'}
    }
}

function New-MutatedMachine {
    return [ordered]@{
        ServiceExists=$true; ServiceRunning=$false
        ServicePath='"C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"'
        ServiceStartMode='Auto'; ServiceDelayedAuto=1; ServiceAccount='LocalSystem'
        ServiceDescription='production description'; ServiceRecovery='production-recovery'; ServiceFailureFlag=1
        SessionExists=$true; SessionEnabled=$false; SessionRunning=$false; SessionXml='production-task-xml'
        CpProvider=$null; CpClass=$null; CpInproc=$null; CpThreading=$null
    }
}

$script:snapshot = $script:pre
$script:machine = New-MutatedMachine
$script:calls = [Collections.Generic.List[string]]::new()
$script:failRuntime = $true
function Read-GaltekRollbackState { return $script:snapshot }
function Remove-GaltekRollbackState { $script:calls.Add('REMOVE') | Out-Null; $script:snapshot = $null }
function Restore-GaltekServiceConfiguration {
    param($Snapshot)
    $script:calls.Add('SERVICE_CONFIG') | Out-Null
    $script:machine.ServiceExists=[bool]$Snapshot.Exists
    $script:machine.ServicePath=[string]$Snapshot.PathName
    $script:machine.ServiceStartMode=[string]$Snapshot.StartMode
    $script:machine.ServiceDelayedAuto=[int]$Snapshot.DelayedAutoStart
    $script:machine.ServiceAccount=[string]$Snapshot.StartName
    $script:machine.ServiceDescription=[string]$Snapshot.Description
}
function Restore-GaltekServiceRuntime {
    param($Snapshot)
    $script:calls.Add('SERVICE_RUNTIME') | Out-Null
    if ($script:failRuntime) { throw [InvalidOperationException]::new('SERVICE_RUNTIME_FIXTURE_FAILURE') }
    $script:machine.ServiceRunning=[bool]$Snapshot.WasRunning
}
function Restore-GaltekTaskState {
    param($Snapshot)
    $script:calls.Add('SESSION') | Out-Null
    $script:machine.SessionExists=[bool]$Snapshot.Exists
    $script:machine.SessionEnabled=[bool]$Snapshot.WasEnabled
    $script:machine.SessionRunning=[bool]$Snapshot.WasRunning
    $script:machine.SessionXml=[string]$Snapshot.DefinitionXmlBase64
}
function Restore-GaltekCredentialProviderState {
    param($Snapshot)
    $script:calls.Add('CP') | Out-Null
    $script:machine.CpProvider=$Snapshot.ProviderDefault.Value
    $script:machine.CpClass=$Snapshot.ClassDefault.Value
    $script:machine.CpInproc=$Snapshot.InprocDefault.Value
    $script:machine.CpThreading=$Snapshot.ThreadingModel.Value
}
function Restore-GaltekServiceRegistryState {
    param($Snapshot)
    $script:calls.Add('REGISTRY') | Out-Null
    $script:machine.ServiceRecovery=[string]$Snapshot.FailureActionsBase64
    $script:machine.ServiceFailureFlag=[int]$Snapshot.FailureFlag
}
function Assert-GaltekRollbackStateRestored {
    param($Plan)
    $script:calls.Add('VERIFY') | Out-Null
    if ($script:machine.ServiceRunning -ne [bool]$Plan.Services[0].WasRunning) { throw 'VERIFY_SERVICE_RUNTIME' }
    if ($script:machine.SessionEnabled -ne [bool]$Plan.SessionTask.WasEnabled) { throw 'VERIFY_SESSION_ENABLED' }
}

$diagnostics = [Collections.Generic.List[string]]::new()
$failed = $false
try {
    Invoke-GaltekRollbackState *>&1 | ForEach-Object { $diagnostics.Add([string]$_) | Out-Null }
}
catch { $failed = $_.Exception.Message -match 'GALTEK_ROLLBACK_FINAL_FAILED' }
Assert-F3DRollback $failed 'A partial runtime failure did not return failure.'
Assert-F3DRollback ($null -ne $script:snapshot) 'Snapshot was deleted after partial failure.'
foreach ($phase in @('SERVICE_RUNTIME','SESSION','CP','REGISTRY','VERIFY')) { Assert-F3DRollback ($script:calls.Contains($phase)) "Partial failure skipped $phase." }
Assert-F3DRollback $script:machine.SessionEnabled 'Session PRE enabled state was not attempted after Service runtime failure.'
Assert-F3DRollback ($script:machine.CpInproc -eq $script:pre.CredentialProvider.InprocDefault.Value) 'CP PRE-state was not restored after Service runtime failure.'
Assert-F3DRollback ($script:machine.ServiceRecovery -eq $script:pre.Services[0].FailureActionsBase64) 'Registry recovery state was not restored after Service runtime failure.'
Assert-F3DRollback (($diagnostics -join "`n") -match 'GALTEK_ROLLBACK_FINAL_FAILED component=SERVICE step=SERVICE_RUNTIME reason=SERVICE_RUNTIME_FIXTURE_FAILURE exception=InvalidOperationException hresult=0x80131509') 'Typed partial-failure diagnostics changed.'

$script:failRuntime = $false
$script:calls.Clear()
$diagnostics.Clear()
Invoke-GaltekRollbackState *>&1 | ForEach-Object { $diagnostics.Add([string]$_) | Out-Null }
Assert-F3DRollback ($null -eq $script:snapshot) 'Snapshot was not deleted after full rollback success.'
Assert-F3DRollback $script:machine.ServiceRunning 'Running Service PRE-state was not restored.'
Assert-F3DRollback ($script:machine.ServicePath -ceq $script:pre.Services[0].PathName) 'Legacy raw Service ImagePath was not restored exactly.'
Assert-F3DRollback ($script:machine.ServiceStartMode -eq 'Auto' -and $script:machine.ServiceDelayedAuto -eq 1 -and $script:machine.ServiceAccount -eq 'LocalSystem') 'Service configuration PRE-state was not restored.'
Assert-F3DRollback ($script:machine.SessionExists -and $script:machine.SessionEnabled -and -not $script:machine.SessionRunning) 'Session enabled/runtime PRE-state was not restored.'
Assert-F3DRollback ($script:machine.SessionXml -eq $script:pre.SessionTask.DefinitionXmlBase64) 'Session XML PRE-state was not restored.'
Assert-F3DRollback ($script:machine.CpProvider -eq 'Galtek Classroom Credential Provider' -and $script:machine.CpThreading -eq 'Apartment') 'Credential Provider PRE-state was not restored.'
foreach ($stage in @('BEGIN','SERVICE_CONFIG','SERVICE_RUNTIME','SESSION','CP','REGISTRY','VERIFY','COMPLETE')) {
    Assert-F3DRollback (($diagnostics -join "`n").Contains("GALTEK_ROLLBACK_FINAL_STEP=$stage")) "Successful diagnostics omitted $stage."
}

$script:calls.Clear()
Invoke-GaltekRollbackState
Assert-F3DRollback ($script:calls.Count -eq 0) 'Second rollback was not a safe no-op after snapshot cleanup.'

$helperSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup\ClientStateHelper.cpp') -Raw
$packageSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup\Package.wxs') -Raw
Assert-F3DRollback ($helperSource.Contains('CreatePipe') -and $helperSource.Contains('MsiProcessMessage')) 'Rollback diagnostics are not captured and forwarded to MSI.'
Assert-F3DRollback ($packageSource.Contains('DllEntry="RollbackClientStateFinal"') -and $packageSource.Contains('Return="ignore"')) 'Rollback DLL custom action or continue marking changed.'
Assert-F3DRollback (-not $packageSource.Contains('ExeCommand="rollback"')) 'Opaque EXE rollback custom action remains authored.'

Write-Output 'F3D_ROLLBACK_REAL_STATE_TESTS_PASS running legacy-ImagePath service-config session-enabled CP registry snapshot-policy partial-failure typed-diagnostics idempotent'
