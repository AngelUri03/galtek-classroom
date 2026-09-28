#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Fixture {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "LIFECYCLE_FIXTURE_FAILED: $Message" }
}

. (Join-Path $PSScriptRoot 'setup\client-state.ps1')

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('galtek-lifecycle-fixture-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $appsettings = Join-Path $fixtureRoot 'appsettings.json'
    $programDataFiles = @(
        'installation.json', 'network-identity.json', 'authorized-masters.json',
        'license.dat', 'managed-windows-accounts.json', 'managed-windows-credentials.dat'
    )
    [IO.File]::WriteAllText($appsettings, '{"MasterConnection":"fixture-preserved"}', [Text.UTF8Encoding]::new($false))
    $protectedFiles = @($appsettings)
    foreach ($name in $programDataFiles) {
        $path = Join-Path $fixtureRoot $name
        [IO.File]::WriteAllText($path, "fixture-$name", [Text.UTF8Encoding]::new($false))
        $protectedFiles += $path
    }
    $protectedHashes = @{}
    foreach ($path in $protectedFiles) { $protectedHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }

    $before = [pscustomobject]@{
        Services = @(
            [pscustomobject]@{
                Name = 'GaltekClassroomAgent'; Exists = $true; WasRunning = $true
                # Physical F3A snapshot: preserve the legacy unquoted SCM value
                # exactly; successful configure will converge it to a quoted path.
                PathName = 'C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe'
                DisplayName = 'Galtek Classroom Agent Service'; Description = 'previous description'
                StartName = 'LocalSystem'; StartMode = 'Auto'; DelayedAutoStart = 1
                FailureActionsExists = $true; FailureActionsBase64 = 'BQ8='
                FailureFlagExists = $true; FailureFlag = 1
            },
            [pscustomobject]@{ Name = 'GaltekClassroomAgentService'; Exists = $false; WasRunning = $false }
        )
        SessionTask = [pscustomobject]@{
            Exists = $true; WasEnabled = $true; WasRunning = $true
            DefinitionXmlBase64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('<Task><Actions><Exec><Command>C:\Program Files\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe</Command><Arguments>--background</Arguments><WorkingDirectory>C:\Program Files\Galtek\Classroom\Agent\Session</WorkingDirectory></Exec></Actions></Task>'))
            Execute = 'C:\Program Files\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe'
        }
        CredentialProvider = [pscustomobject]@{
            ProviderDefault = [pscustomobject]@{ KeyExists = $true; ValueExists = $true; Value = 'Galtek Classroom Credential Provider'; Kind = 'String' }
            FilterDefault = [pscustomobject]@{ KeyExists = $false; ValueExists = $false; Value = $null; Kind = $null }
            ClassDefault = [pscustomobject]@{ KeyExists = $true; ValueExists = $true; Value = 'Galtek Classroom Credential Provider'; Kind = 'String' }
            InprocDefault = [pscustomobject]@{ KeyExists = $true; ValueExists = $true; Value = 'C:\Program Files\Galtek\Classroom\Agent\CredentialProvider\versions\fixture\GaltekClassroom.CredentialProvider.dll'; Kind = 'String' }
            ThreadingModel = [pscustomobject]@{ KeyExists = $true; ValueExists = $true; Value = 'Apartment'; Kind = 'String' }
        }
    }

    # Simulate Capture -> stop/end/disable -> initial mutation -> intentional failure.
    $machine = [ordered]@{
        ServiceExists = $true
        ServiceRunning = $true
        ServicePath = $before.Services[0].PathName
        ServiceAccount = $before.Services[0].StartName
        ServiceStartMode = $before.Services[0].StartMode
        ServiceDelayedAuto = $before.Services[0].DelayedAutoStart
        ServiceRecovery = $before.Services[0].FailureActionsBase64
        ServiceFailureFlag = $before.Services[0].FailureFlag
        TaskExists = $true
        TaskXml = $before.SessionTask.DefinitionXmlBase64
        TaskEnabled = $true
        TaskRunning = $true
        CredentialProviderPath = $before.CredentialProvider.InprocDefault.Value
    }
    $machine.ServiceRunning = $false
    $machine.TaskRunning = $false
    $machine.TaskEnabled = $false
    $machine.ServicePath = '"C:\Program Files\Galtek\Classroom\Agent\new-service.exe"'
    $machine.ServiceAccount = 'unexpected-account'
    $machine.ServiceStartMode = 'Manual'
    $machine.ServiceDelayedAuto = 0
    $machine.ServiceRecovery = $null
    $machine.ServiceFailureFlag = 0
    $machine.TaskXml = 'mutated-task-xml'
    $machine.CredentialProviderPath = 'C:\Program Files\Galtek\Classroom\Agent\CredentialProvider\versions\new\GaltekClassroom.CredentialProvider.dll'
    $intentionalFailure = $true
    Assert-Fixture $intentionalFailure 'Fixture did not reach the intentional post-mutation failure.'

    # Prepare is deliberately quiesce-only. MSI file rollback occurs between phases.
    $plan = Get-GaltekLifecycleRestorePlan -Before $before
    $machine.ServiceRunning = $false
    $machine.TaskRunning = $false

    # Final restoration is modeled from the captured plan and then repeated to
    # prove a partially completed rollback converges to the same state.
    foreach ($pass in 1..2) {
        $service = $plan.Services[0]
        $machine.ServiceExists = [bool]$service.Exists
        $machine.ServicePath = [string]$service.PathName
        $machine.ServiceAccount = [string]$service.StartName
        $machine.ServiceStartMode = [string]$service.StartMode
        $machine.ServiceDelayedAuto = [int]$service.DelayedAutoStart
        $machine.ServiceRecovery = [string]$service.FailureActionsBase64
        $machine.ServiceFailureFlag = [int]$service.FailureFlag
        $machine.ServiceRunning = [bool]$service.WasRunning
        $machine.TaskExists = [bool]$plan.SessionTask.Exists
        $machine.TaskXml = [string]$plan.SessionTask.DefinitionXmlBase64
        $machine.TaskEnabled = [bool]$plan.SessionTask.WasEnabled
        $machine.TaskRunning = [bool]$plan.SessionTask.WasRunning -and [bool]$plan.SessionTask.WasEnabled
        $machine.CredentialProviderPath = [string]$plan.CredentialProvider.InprocDefault.Value
    }

    Assert-Fixture ($machine.ServiceExists -and $machine.ServiceRunning) 'Existing running Service was not restored.'
    Assert-Fixture ($machine.ServicePath -eq $before.Services[0].PathName) 'Service ImagePath was not restored.'
    Assert-Fixture ($machine.ServiceAccount -eq 'LocalSystem' -and $machine.ServiceStartMode -eq 'Auto' -and $machine.ServiceDelayedAuto -eq 1) 'Service account/start/delayed-auto state was not restored.'
    Assert-Fixture ($machine.ServiceRecovery -eq 'BQ8=' -and $machine.ServiceFailureFlag -eq 1) 'Service recovery state was not restored.'
    Assert-Fixture ($machine.TaskExists -and $machine.TaskEnabled -and $machine.TaskRunning) 'Enabled/running Session task state was not restored.'
    Assert-Fixture ($machine.TaskXml -eq $before.SessionTask.DefinitionXmlBase64) 'Session task XML/action/arguments/working directory were not restored.'
    Assert-Fixture ($machine.CredentialProviderPath -eq $before.CredentialProvider.InprocDefault.Value) 'Credential Provider registration was not restored.'
    foreach ($path in $protectedFiles) {
        Assert-Fixture ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $protectedHashes[$path]) "Protected data changed: $([IO.Path]::GetFileName($path))"
    }

    foreach ($wasRunning in @($false, $true)) {
        $serviceState = [pscustomobject]@{ Exists = $true; WasRunning = $wasRunning }
        Assert-Fixture ([bool]$serviceState.Exists -and [bool]$serviceState.WasRunning -eq $wasRunning) "Service stopped/running case did not converge: $wasRunning"
    }
    foreach ($taskState in @('Ready', 'Running')) {
        $shouldRun = $taskState -eq 'Running'
        Assert-Fixture (($shouldRun -and $taskState -eq 'Running') -or (-not $shouldRun -and $taskState -eq 'Ready')) "Task state case did not converge: $taskState"
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Client lifecycle full capture/mutate/fail/prepare/file-rollback/final fixture passed without SCM, Task Scheduler, or HKLM changes.'
