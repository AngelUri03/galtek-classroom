#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Configure', 'Rollback', 'Unconfigure', 'Commit')]
    [string] $Action,

    [Parameter(Mandatory = $true)]
    [string] $InstallDirectory,

    [switch] $InitialInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'GaltekClassroomAgent'
$LegacyServiceName = 'GaltekClassroomAgentService'
$ServiceDisplayName = 'Galtek Classroom Agent Service'
$ServiceDescription = 'Servicio local de Galtek Classroom para identidad, licencia y administracion segura del equipo.'
$ServiceExecutableName = 'GaltekClassroom.Agent.Service.exe'
$SessionTaskName = 'GaltekClassroomSessionAgent'
$SessionExecutableName = 'GaltekClassroom.Agent.Session.exe'
$SessionPrincipalSid = 'S-1-5-32-545'
$CredentialProviderClsid = '{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}'
$CredentialProviderFilterPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\$CredentialProviderClsid"
$CredentialProviderPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$CredentialProviderClsid"
$CredentialProviderInprocPath = "HKLM:\SOFTWARE\Classes\CLSID\$CredentialProviderClsid\InprocServer32"
$RecoveryResetSeconds = 86400
$RecoveryActions = 'restart/5000/restart/15000/restart/60000'
$GaltekHashCache = @{}

. (Join-Path $PSScriptRoot 'legacy-adoption.ps1')
. (Join-Path $PSScriptRoot 'client-state.ps1')

function Write-Stage {
    param([Parameter(Mandatory = $true)][string] $Name)
    Write-Output "GALTEK_SETUP_STAGE=$Name"
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Invoke-Sc {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)
    & (Join-Path $env:SystemRoot 'System32\sc.exe') @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "SERVICE_CONTROL_FAILED stage=$($Arguments[0]) exitCode=$LASTEXITCODE"
    }
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Expected,
        [int] $TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($Expected -eq 'Absent' -and $null -eq $service) { return }
        if ($null -ne $service -and $service.Status.ToString() -eq $Expected) { return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "SERVICE_STATE_TIMEOUT service=$Name expected=$Expected"
}

function Stop-ServiceNormally {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) { return }
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $Name -ErrorAction Stop
        Wait-ServiceState -Name $Name -Expected 'Stopped' -TimeoutSeconds 45
    }
}

function Remove-ServiceRegistration {
    param([Parameter(Mandatory = $true)][string] $Name)

    if ($null -eq (Get-Service -Name $Name -ErrorAction SilentlyContinue)) { return }
    Stop-ServiceNormally -Name $Name
    Invoke-Sc -Arguments @('delete', $Name)
    Wait-ServiceState -Name $Name -Expected 'Absent' -TimeoutSeconds 30
}

function Get-CapturedServiceSnapshot {
    param(
        [Parameter(Mandatory = $true)] $CapturedState,
        [Parameter(Mandatory = $true)][string] $Name
    )

    $snapshot = @($CapturedState.Services | Where-Object { [string]$_.Name -eq $Name }) | Select-Object -First 1
    if ($null -ne $snapshot) { return $snapshot }
    return [pscustomobject]@{ Name = $Name; Exists = $false; WasRunning = $false; PathName = $null }
}

function Get-CapturedLegacyState {
    param([Parameter(Mandatory = $true)] $CapturedState)

    $service = Get-CapturedServiceSnapshot -CapturedState $CapturedState -Name $ServiceName
    $legacyService = Get-CapturedServiceSnapshot -CapturedState $CapturedState -Name $LegacyServiceName
    $task = $CapturedState.SessionTask
    $inproc = if ($null -ne $CapturedState.CredentialProvider -and [bool]$CapturedState.CredentialProvider.InprocDefault.ValueExists) {
        [string]$CapturedState.CredentialProvider.InprocDefault.Value
    }
    else { $null }

    return [pscustomobject]@{
        ServiceExists = [bool]$service.Exists
        LegacyServiceExists = [bool]$legacyService.Exists
        ServiceImagePath = if ([bool]$service.Exists) { [string]$service.PathName } elseif ([bool]$legacyService.Exists) { [string]$legacyService.PathName } else { $null }
        SessionTaskExists = [bool]$task.Exists
        SessionExecutablePath = if ([bool]$task.Exists) { [string]$task.Execute } else { $null }
        SessionArguments = if ([bool]$task.Exists) { [string]$task.Arguments } else { $null }
        SessionWorkingDirectory = if ([bool]$task.Exists) { [string]$task.WorkingDirectory } else { $null }
        CredentialProviderRegistered = -not [string]::IsNullOrWhiteSpace($inproc)
        CredentialProviderPath = $inproc
    }
}

function Get-CredentialProviderCleanupMarkerPath {
    $commonData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    return Join-Path $commonData 'Galtek\Classroom\Installer\credential-provider-cleanup-pending.json'
}

function Remove-CredentialProviderPackagesBestEffort {
    param([Parameter(Mandatory = $true)][string] $Root)

    $credentialRoot = Resolve-FullPath (Join-Path $Root 'CredentialProvider')
    $markerPath = Get-CredentialProviderCleanupMarkerPath
    if (-not (Test-Path -LiteralPath $credentialRoot)) {
        if (Test-Path -LiteralPath $markerPath) { Remove-Item -LiteralPath $markerPath -Force }
        return
    }

    try {
        Remove-Item -LiteralPath $credentialRoot -Recurse -Force
        if (Test-Path -LiteralPath $markerPath) { Remove-Item -LiteralPath $markerPath -Force }
    }
    catch {
        $markerDirectory = Split-Path -Parent $markerPath
        New-Item -ItemType Directory -Path $markerDirectory -Force | Out-Null
        [ordered]@{
            schemaVersion = 1
            component = 'CREDENTIAL_PROVIDER'
            state = 'UNREGISTERED_REBOOT_CLEANUP_REQUIRED'
            createdAtUtc = [DateTime]::UtcNow.ToString('O')
        } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
        Write-Warning 'UNREGISTERED_REBOOT_CLEANUP_REQUIRED rebootRecommended=true'
    }
}

function Configure-AgentService {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $quotedPath = '"' + $ExecutablePath + '"'
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Invoke-Sc -Arguments @('create', $ServiceName, 'binPath=', $quotedPath, 'DisplayName=', $ServiceDisplayName, 'start=', 'delayed-auto', 'obj=', 'LocalSystem')
    }
    else {
        Stop-ServiceNormally -Name $ServiceName
        Invoke-Sc -Arguments @('config', $ServiceName, 'binPath=', $quotedPath, 'DisplayName=', $ServiceDisplayName, 'start=', 'delayed-auto', 'obj=', 'LocalSystem')
    }

    Invoke-Sc -Arguments @('description', $ServiceName, $ServiceDescription)
    Invoke-Sc -Arguments @('failure', $ServiceName, 'reset=', [string]$RecoveryResetSeconds, 'actions=', $RecoveryActions)
    Invoke-Sc -Arguments @('failureflag', $ServiceName, '1')

    # Windows PowerShell's native argument serialization can cause sc.exe to
    # persist a semantically correct path without the protective outer quotes.
    # Converge the Registry64 representation after SCM configuration and close
    # the writable handle before performing the independent live read below.
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $baseKey.OpenSubKey("SYSTEM\CurrentControlSet\Services\$ServiceName", $true)
        if ($null -eq $key) { throw 'SERVICE_CONFIG_REGISTRY_MISSING' }
        try { $key.SetValue('ImagePath', $quotedPath, [Microsoft.Win32.RegistryValueKind]::ExpandString) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Register-SessionTask {
    param(
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $WorkingDirectory
    )

    $action = New-ScheduledTaskAction -Execute $ExecutablePath -Argument '--background' -WorkingDirectory $WorkingDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -GroupId $SessionPrincipalSid -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) `
        -MultipleInstances Parallel `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -StartWhenAvailable `
        -Hidden `
        -Priority 7

    Register-ScheduledTask -TaskName $SessionTaskName -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Description 'Galtek Classroom Session Agent' -Force | Out-Null
}

function Open-GaltekServiceRegistryBaseKey {
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
}

function Get-GaltekServiceLiveState {
    # Keep the live read handles local to this function so a writable configure
    # handle can never leak into post-validation or be used after disposal.
    $service = Get-Service -Name $ServiceName -ErrorAction Stop
    $baseKey = $null
    $registry = $null
    try {
        $baseKey = Open-GaltekServiceRegistryBaseKey
        $registry = $baseKey.OpenSubKey("SYSTEM\CurrentControlSet\Services\$ServiceName", $false)
        if ($null -eq $registry) { throw 'SERVICE_CONTRACT_REGISTRY_MISSING' }
        return [pscustomobject]@{
            Service = $service
            ImagePath = [string]$registry.GetValue('ImagePath', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            Start = [int]$registry.GetValue('Start', -1)
            ObjectName = [string]$registry.GetValue('ObjectName', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            DelayedAutoStart = [int]$registry.GetValue('DelayedAutostart', 0)
            FailureFlag = [int]$registry.GetValue('FailureActionsOnNonCrashFailures', 0)
            FailureActions = [byte[]]$registry.GetValue('FailureActions', $null)
        }
    }
    finally {
        if ($null -ne $registry) { $registry.Dispose() }
        if ($null -ne $baseKey) { $baseKey.Dispose() }
    }
}

function Assert-ServiceContract {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    # POST-STATE authority: never validate a mutation against the PRE-STATE
    # snapshot used for adoption and rollback. Reopen Registry64 and SCM live.
    $live = Get-GaltekServiceLiveState
    $service = $live.Service
    $actualRaw = [string]$live.ImagePath
    $start = [int]$live.Start
    $account = [string]$live.ObjectName
    $delayedAuto = [int]$live.DelayedAutoStart
    $failureFlag = [int]$live.FailureFlag
    $failure = [byte[]]$live.FailureActions

    $semantic = Resolve-GaltekServiceImagePath -CommandLine $actualRaw -ExpectedExecutable $ExecutablePath
    if (-not $semantic.IsValid -or -not [string]::IsNullOrWhiteSpace([string]$semantic.Arguments)) {
        $actualExecutable = if ([string]::IsNullOrWhiteSpace([string]$semantic.ExecutablePath)) { '<invalid>' } else { [string]$semantic.ExecutablePath }
        $actualArguments = if ([string]::IsNullOrWhiteSpace([string]$semantic.Arguments)) { '<none>' } else { [string]$semantic.Arguments }
        Write-Output 'SERVICE_CONTRACT_IMAGE_PATH'
        Write-Output "actualRaw=`"$actualRaw`""
        Write-Output "actualExecutable=`"$actualExecutable`""
        Write-Output "actualArguments=`"$actualArguments`""
        Write-Output "expectedExecutable=`"$ExecutablePath`""
        Write-Output 'expectedArguments="<none>"'
        throw 'SERVICE_CONTRACT_IMAGE_PATH'
    }
    if ($null -eq $service -or $start -ne 2) { throw 'SERVICE_CONTRACT_START_MODE' }
    if ($account -notin @('LocalSystem', 'Local System')) { throw 'SERVICE_CONTRACT_ACCOUNT' }
    if ($delayedAuto -ne 1) { throw 'SERVICE_CONTRACT_DELAYED_AUTO' }
    if ($failureFlag -ne 1) { throw 'SERVICE_CONTRACT_FAILURE_FLAG' }

    if ($null -eq $failure) { throw 'SERVICE_CONTRACT_RECOVERY_MISSING' }
    if ($failure.Length -lt 44 -or [BitConverter]::ToUInt32($failure, 0) -ne 86400 -or [BitConverter]::ToUInt32($failure, 12) -ne 3) {
        throw 'SERVICE_CONTRACT_RECOVERY_HEADER'
    }
    $offset = [int][BitConverter]::ToUInt32($failure, 16)
    $expected = @(5000, 15000, 60000)
    for ($index = 0; $index -lt 3; $index++) {
        if ([BitConverter]::ToUInt32($failure, $offset + ($index * 8)) -ne 1 -or
            [BitConverter]::ToUInt32($failure, $offset + ($index * 8) + 4) -ne $expected[$index]) {
            throw "SERVICE_CONTRACT_RECOVERY_ACTION_$index"
        }
    }
}

function Get-GaltekFileSha256Cached {
    param([Parameter(Mandatory = $true)][string] $Path)
    $fullPath = Resolve-FullPath $Path
    if (-not $GaltekHashCache.ContainsKey($fullPath)) {
        $GaltekHashCache[$fullPath] = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return [string]$GaltekHashCache[$fullPath]
}

function Assert-SessionTaskContract {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $task = Get-ScheduledTask -TaskName $SessionTaskName -ErrorAction Stop
    $action = @($task.Actions)[0]
    $trigger = @($task.Triggers)[0]
    $principalSid = try {
        ([System.Security.Principal.NTAccount]::new([string]$task.Principal.GroupId)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        ([System.Security.Principal.SecurityIdentifier]::new([string]$task.Principal.GroupId)).Value
    }

    if ($principalSid -ne $SessionPrincipalSid) { throw 'SESSION_TASK_PRINCIPAL' }
    if ($task.Principal.RunLevel.ToString() -ne 'Limited') { throw 'SESSION_TASK_RUN_LEVEL' }
    if ($trigger.CimClass.CimClassName -ne 'MSFT_TaskLogonTrigger') { throw 'SESSION_TASK_TRIGGER' }
    if (-not [string]::Equals((Resolve-FullPath $action.Execute), (Resolve-FullPath $ExecutablePath), [System.StringComparison]::OrdinalIgnoreCase)) { throw 'SESSION_TASK_EXECUTABLE' }
    if ($action.Arguments -ne '--background') { throw 'SESSION_TASK_ARGUMENTS' }
    if ($task.Settings.MultipleInstances.ToString() -ne 'Parallel') { throw 'SESSION_TASK_INSTANCES' }
    if ($task.Settings.RunOnlyIfNetworkAvailable) { throw 'SESSION_TASK_NETWORK' }
}

function Assert-CredentialProviderContract {
    param([Parameter(Mandatory = $true)][string] $Root)

    if (Test-Path -LiteralPath $CredentialProviderFilterPath) { throw 'CREDENTIAL_PROVIDER_FILTER_PRESENT' }
    if (-not (Test-Path -LiteralPath $CredentialProviderPath)) { throw 'CREDENTIAL_PROVIDER_NOT_REGISTERED' }
    if (-not (Test-Path -LiteralPath $CredentialProviderInprocPath)) { throw 'CREDENTIAL_PROVIDER_COM_NOT_REGISTERED' }

    $inprocKey = Get-Item -LiteralPath $CredentialProviderInprocPath
    $dllPath = [string]$inprocKey.GetValue('')
    $threadingModel = [string]$inprocKey.GetValue('ThreadingModel')
    if (-not (Test-GaltekPathInsideRoot -Path $dllPath -ExpectedRoot $Root)) { throw 'CREDENTIAL_PROVIDER_PATH' }
    if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw 'CREDENTIAL_PROVIDER_DLL_MISSING' }
    if ($threadingModel -ne 'Apartment') { throw 'CREDENTIAL_PROVIDER_THREADING_MODEL' }

    $packageDirectory = Split-Path -Parent $dllPath
    $manifestPath = Join-Path $packageDirectory 'credential-provider.manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'CREDENTIAL_PROVIDER_MANIFEST_MISSING' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $actualHash = Get-GaltekFileSha256Cached -Path $dllPath
    if ([string]$manifest.clsid -ne $CredentialProviderClsid) { throw 'CREDENTIAL_PROVIDER_MANIFEST_CLSID' }
    if ([string]$manifest.sha256 -ne $actualHash) { throw 'CREDENTIAL_PROVIDER_HASH' }
    if ([string]$manifest.packageId -ne (Split-Path -Leaf $packageDirectory)) { throw 'CREDENTIAL_PROVIDER_PACKAGE_ID' }

    $standardUserSids = @('S-1-1-0', 'S-1-5-11', 'S-1-5-32-545')
    $writeMask = [System.Security.AccessControl.FileSystemRights]::WriteData -bor
        [System.Security.AccessControl.FileSystemRights]::CreateFiles -bor
        [System.Security.AccessControl.FileSystemRights]::AppendData -bor
        [System.Security.AccessControl.FileSystemRights]::CreateDirectories -bor
        [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
        [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor
        [System.Security.AccessControl.FileSystemRights]::Delete -bor
        [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [System.Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($path in @($packageDirectory, $dllPath, $manifestPath)) {
        foreach ($rule in (Get-Acl -LiteralPath $path).Access) {
            if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) { continue }
            $sid = try { $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { [string]$rule.IdentityReference.Value }
            if ($standardUserSids -contains $sid -and (($rule.FileSystemRights -band $writeMask) -ne 0)) {
                throw 'CREDENTIAL_PROVIDER_ACL_WRITABLE_BY_STANDARD_USER'
            }
        }
    }
}

function Assert-InstalledPayloadContract {
    param([Parameter(Mandatory = $true)][string] $Root)

    $manifestPath = Join-Path $PSScriptRoot 'installed-payload-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'INSTALLED_PAYLOAD_MANIFEST_MISSING' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in @($manifest.files)) {
        $relativePath = [string]$entry.relativePath
        if ([string]::IsNullOrWhiteSpace($relativePath) -or [System.IO.Path]::IsPathRooted($relativePath)) {
            throw 'INSTALLED_PAYLOAD_MANIFEST_PATH'
        }
        $path = Resolve-FullPath (Join-Path $Root $relativePath)
        if (-not (Test-GaltekPathInsideRoot -Path $path -ExpectedRoot $Root)) { throw 'INSTALLED_PAYLOAD_PATH_OUTSIDE_ROOT' }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "INSTALLED_PAYLOAD_MISSING file=$relativePath" }
        $actual = Get-GaltekFileSha256Cached -Path $path
        if ($actual -ne ([string]$entry.sha256).ToLowerInvariant()) { throw "INSTALLED_PAYLOAD_HASH_MISMATCH file=$relativePath" }
    }
    Write-Stage 'PAYLOAD_HASHES_VERIFIED'
}

function Invoke-Configure {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [switch] $DetectLegacy
    )

    Write-Stage 'CAPTURE_STATE'
    $capturedState = Read-GaltekRollbackState
    if ($null -eq $capturedState) { throw 'CLIENT_STATE_SNAPSHOT_MISSING' }
    $primaryBefore = Get-CapturedServiceSnapshot -CapturedState $capturedState -Name $ServiceName
    $legacyBefore = Get-CapturedServiceSnapshot -CapturedState $capturedState -Name $LegacyServiceName
    $legacyState = Get-CapturedLegacyState -CapturedState $capturedState
    $plan = Get-GaltekLegacyAdoptionPlan -State $legacyState -InstallRoot $Root

    if ($DetectLegacy -and $plan.IsLegacyInstallation -and -not $plan.IsValid) {
        $diagnostic = "LEGACY_ADOPTION_REJECTED component=$($plan.Component) reason=$($plan.Reason)"
        if (-not [string]::IsNullOrWhiteSpace([string]$plan.Path)) {
            $diagnostic += " path=`"$($plan.Path)`""
        }
        Write-Output $diagnostic
        throw $diagnostic
    }

    if ($DetectLegacy) {
        Write-Output "GALTEK_LEGACY_ADOPTION=$($plan.IsLegacyInstallation.ToString().ToLowerInvariant())"
    }

    Assert-InstalledPayloadContract -Root $Root

    Write-Stage 'CONFIGURE_SERVICE'
    if ($legacyBefore.Exists) {
        Remove-ServiceRegistration -Name $LegacyServiceName
    }

    $serviceExecutable = Resolve-FullPath (Join-Path $Root $ServiceExecutableName)
    if (-not (Test-Path -LiteralPath $serviceExecutable -PathType Leaf)) { throw 'SERVICE_PAYLOAD_MISSING' }
    Configure-AgentService -ExecutablePath $serviceExecutable
    Assert-ServiceContract -ExecutablePath $serviceExecutable

    Write-Stage 'CONFIGURE_SESSION_AGENT'
    $sessionDirectory = Resolve-FullPath (Join-Path $Root 'Session')
    $sessionExecutable = Resolve-FullPath (Join-Path $sessionDirectory $SessionExecutableName)
    if (-not (Test-Path -LiteralPath $sessionExecutable -PathType Leaf)) { throw 'SESSION_PAYLOAD_MISSING' }
    Register-SessionTask -ExecutablePath $sessionExecutable -WorkingDirectory $sessionDirectory
    Assert-SessionTaskContract -ExecutablePath $sessionExecutable

    Write-Stage 'VERIFY_CREDENTIAL_PROVIDER'
    Assert-CredentialProviderContract -Root $Root

    Write-Stage 'START_SERVICE'
    Start-Service -Name $ServiceName -ErrorAction Stop
    Wait-ServiceState -Name $ServiceName -Expected 'Running' -TimeoutSeconds 45

    try {
        Start-ScheduledTask -TaskName $SessionTaskName -ErrorAction Stop
    }
    catch {
        Write-Warning 'SESSION_TASK_START_DEFERRED'
    }

    Write-Stage 'CONFIGURE_COMPLETE'
}

function Invoke-Rollback {
    Write-Stage 'ROLLBACK_PREPARE'
    Invoke-GaltekRollbackPrepare
}

function Invoke-Unconfigure {
    param([Parameter(Mandatory = $true)][string] $Root)

    Write-Stage 'UNREGISTER_SESSION_AGENT'
    Stop-ScheduledTask -TaskName $SessionTaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $SessionTaskName -Confirm:$false -ErrorAction SilentlyContinue

    Write-Stage 'REMOVE_SERVICE'
    Remove-ServiceRegistration -Name $ServiceName
    Remove-ServiceRegistration -Name $LegacyServiceName

    Write-Stage 'CLEANUP_CREDENTIAL_PROVIDER_PACKAGES'
    Remove-CredentialProviderPackagesBestEffort -Root $Root

    # Credential Provider keys are MSI components. Locked DLL cleanup is
    # best-effort and explicitly marked; setup never terminates LogonUI/Winlogon.
    Write-Stage 'UNCONFIGURE_COMPLETE'
}

$root = Resolve-FullPath $InstallDirectory
switch ($Action) {
    'Configure' { Invoke-Configure -Root $root -DetectLegacy:$InitialInstall }
    'Rollback' { Invoke-Rollback }
    'Unconfigure' { Invoke-Unconfigure -Root $root }
    'Commit' { Remove-GaltekRollbackState; Write-Stage 'COMMIT_COMPLETE' }
}
