#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$GaltekStateServiceNames = @('GaltekClassroomAgent', 'GaltekClassroomAgentService')
$GaltekStateTaskName = 'GaltekClassroomSessionAgent'
$GaltekStateCredentialProviderClsid = '{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}'

function Get-GaltekRollbackStatePath {
    $commonData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    return Join-Path $commonData 'Galtek\Classroom\Installer\setup-rollback.json'
}

function ConvertTo-GaltekBase64 {
    param([AllowNull()][byte[]] $Bytes)
    if ($null -eq $Bytes -or $Bytes.Length -eq 0) { return $null }
    return [Convert]::ToBase64String($Bytes)
}

function ConvertFrom-GaltekBase64 {
    param([AllowNull()][string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    return [Convert]::FromBase64String($Value)
}

function Get-GaltekRegistryLocation {
    param([Parameter(Mandatory = $true)][string] $Path)

    $match = [regex]::Match($Path, '^(?<hive>HKLM|HKCU):\\(?<subkey>.+)$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { throw "REGISTRY_PATH_NOT_SUPPORTED path=$Path" }
    $hive = if ($match.Groups['hive'].Value.Equals('HKLM', [StringComparison]::OrdinalIgnoreCase)) {
        [Microsoft.Win32.RegistryHive]::LocalMachine
    }
    else {
        [Microsoft.Win32.RegistryHive]::CurrentUser
    }
    return [pscustomobject]@{ Hive = $hive; SubKey = $match.Groups['subkey'].Value }
}

function Open-GaltekRegistryBaseKey {
    param([Parameter(Mandatory = $true)][Microsoft.Win32.RegistryHive] $Hive)
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
}

function Test-GaltekRegistryValuesEqual {
    param([AllowNull()] $Left, [AllowNull()] $Right)
    if ($null -eq $Left -or $null -eq $Right) { return $null -eq $Left -and $null -eq $Right }
    if ($Left -is [byte[]] -or $Right -is [byte[]]) {
        return ([Convert]::ToBase64String([byte[]]$Left) -eq [Convert]::ToBase64String([byte[]]$Right))
    }
    return [object]::Equals($Left, $Right)
}

function Get-GaltekRegistryValueSnapshot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Name
    )

    $location = Get-GaltekRegistryLocation -Path $Path
    $baseKey = Open-GaltekRegistryBaseKey -Hive $location.Hive
    try {
        # Capture is intentionally read-only. Restore uses a separate writable handle.
        $key = $baseKey.OpenSubKey($location.SubKey, $false)
        if ($null -eq $key) {
            return [ordered]@{ KeyExists = $false; ValueExists = $false; Value = $null; Kind = $null }
        }
        try {
            $valueNames = @($key.GetValueNames())
            if ($valueNames -notcontains $Name) {
                return [ordered]@{ KeyExists = $true; ValueExists = $false; Value = $null; Kind = $null }
            }
            return [ordered]@{
                KeyExists = $true
                ValueExists = $true
                Value = $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                Kind = $key.GetValueKind($Name).ToString()
            }
        }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-GaltekServiceStateSnapshot {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return [ordered]@{ Name = $Name; Exists = $false; WasRunning = $false }
    }

    $baseKey = Open-GaltekRegistryBaseKey -Hive ([Microsoft.Win32.RegistryHive]::LocalMachine)
    try {
        $registry = $baseKey.OpenSubKey("SYSTEM\CurrentControlSet\Services\$Name", $false)
        if ($null -eq $registry) { throw "CLIENT_STATE_SERVICE_REGISTRY_MISSING service=$Name" }
        try {
            $registryValueNames = @($registry.GetValueNames())
            $startMode = switch ([int]$registry.GetValue('Start', -1)) {
                2 { 'Auto' }
                3 { 'Manual' }
                4 { 'Disabled' }
                default { throw "CLIENT_STATE_SERVICE_START_MODE_INVALID service=$Name" }
            }
            return [ordered]@{
                Name = $Name
                Exists = $true
                WasRunning = $service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
                PathName = [string]$registry.GetValue('ImagePath', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                PathNameKind = $registry.GetValueKind('ImagePath').ToString()
                DisplayName = [string]$registry.GetValue('DisplayName', $Name, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                StartName = [string]$registry.GetValue('ObjectName', 'LocalSystem', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                StartMode = $startMode
                DelayedAutoStart = [int]$registry.GetValue('DelayedAutostart', 0)
                Description = [string]$registry.GetValue('Description', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                FailureActionsExists = $registryValueNames -contains 'FailureActions'
                FailureActionsBase64 = ConvertTo-GaltekBase64 -Bytes ([byte[]]$registry.GetValue('FailureActions', $null))
                FailureFlagExists = $registryValueNames -contains 'FailureActionsOnNonCrashFailures'
                FailureFlag = $registry.GetValue('FailureActionsOnNonCrashFailures', $null)
            }
        }
        finally { $registry.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-GaltekTaskStateSnapshot {
    $task = Get-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return [ordered]@{ Exists = $false; WasEnabled = $false; WasRunning = $false }
    }

    $xml = Export-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction Stop
    $action = @($task.Actions)[0]
    return [ordered]@{
        Exists = $true
        WasEnabled = [string]$task.State -ne 'Disabled'
        WasRunning = [string]$task.State -eq 'Running'
        DefinitionXmlBase64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($xml))
        Execute = [string]$action.Execute
        Arguments = [string]$action.Arguments
        WorkingDirectory = [string]$action.WorkingDirectory
    }
}

function Get-GaltekCredentialProviderStateSnapshot {
    $provider = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$GaltekStateCredentialProviderClsid"
    $filter = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\$GaltekStateCredentialProviderClsid"
    $class = "HKLM:\SOFTWARE\Classes\CLSID\$GaltekStateCredentialProviderClsid"
    $inproc = Join-Path $class 'InprocServer32'
    return [ordered]@{
        ProviderDefault = Get-GaltekRegistryValueSnapshot -Path $provider -Name ''
        FilterDefault = Get-GaltekRegistryValueSnapshot -Path $filter -Name ''
        ClassDefault = Get-GaltekRegistryValueSnapshot -Path $class -Name ''
        InprocDefault = Get-GaltekRegistryValueSnapshot -Path $inproc -Name ''
        ThreadingModel = Get-GaltekRegistryValueSnapshot -Path $inproc -Name 'ThreadingModel'
    }
}

function Get-GaltekLifecycleStateSnapshot {
    return [ordered]@{
        SchemaVersion = 4
        CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
        Services = @($GaltekStateServiceNames | ForEach-Object { Get-GaltekServiceStateSnapshot -Name $_ })
        SessionTask = Get-GaltekTaskStateSnapshot
        CredentialProvider = Get-GaltekCredentialProviderStateSnapshot
    }
}

function Save-GaltekRollbackState {
    param([Parameter(Mandatory = $true)] $State)
    $path = Get-GaltekRollbackStatePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    $State | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Read-GaltekRollbackState {
    $path = Get-GaltekRollbackStatePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Remove-GaltekRollbackState {
    $path = Get-GaltekRollbackStatePath
    if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}

function Get-GaltekLifecycleRestorePlan {
    param([Parameter(Mandatory = $true)] $Before)
    return [pscustomobject]@{
        Services = @($Before.Services)
        SessionTask = $Before.SessionTask
        CredentialProvider = $Before.CredentialProvider
    }
}

function Invoke-GaltekSc {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)
    & (Join-Path $env:SystemRoot 'System32\sc.exe') @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "SERVICE_ROLLBACK_FAILED stage=$($Arguments[0]) exitCode=$LASTEXITCODE" }
}

function Wait-GaltekServiceState {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][ValidateSet('Running', 'Stopped', 'Absent')][string] $Expected,
        [int] $TimeoutSeconds = 45
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($Expected -eq 'Absent' -and $null -eq $service) { return }
        if ($null -ne $service -and [string]$service.Status -eq $Expected) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "SERVICE_ROLLBACK_STATE_TIMEOUT service=$Name expected=$Expected"
}

function Restore-GaltekRegistryValue {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Name,
        [Parameter(Mandatory = $true)] $Snapshot,
        [AllowNull()][scriptblock] $ValidateCurrentValue
    )

    $location = Get-GaltekRegistryLocation -Path $Path
    $baseKey = Open-GaltekRegistryBaseKey -Hive $location.Hive
    try {
        $key = $baseKey.OpenSubKey($location.SubKey, $true)
        if ($null -eq $key -and ([bool]$Snapshot.KeyExists -or [bool]$Snapshot.ValueExists)) {
            $key = $baseKey.CreateSubKey($location.SubKey, [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree)
        }
        if ($null -eq $key) { return }
        try {
            $currentExists = @($key.GetValueNames()) -contains $Name
            $currentValue = if ($currentExists) {
                $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            }
            else { $null }
            $targetValue = if ([bool]$Snapshot.ValueExists) { $Snapshot.Value } else { $null }
            if ($currentExists -and -not (Test-GaltekRegistryValuesEqual -Left $currentValue -Right $targetValue) -and
                $null -ne $ValidateCurrentValue -and -not (& $ValidateCurrentValue $currentValue)) {
                throw "REGISTRY_ROLLBACK_UNEXPECTED_VALUE path=$Path name=$Name"
            }

            if ([bool]$Snapshot.ValueExists) {
                $kind = [Microsoft.Win32.RegistryValueKind]::$([string]$Snapshot.Kind)
                $value = if ($kind -eq [Microsoft.Win32.RegistryValueKind]::Binary) { [byte[]]$Snapshot.Value } else { $Snapshot.Value }
                $key.SetValue($Name, $value, $kind)
            }
            elseif ($currentExists) {
                $key.DeleteValue($Name, $false)
            }
        }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Remove-GaltekRegistryKeyIfOriginallyAbsent {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $Snapshot
    )
    if ([bool]$Snapshot.KeyExists) { return }

    $location = Get-GaltekRegistryLocation -Path $Path
    $baseKey = Open-GaltekRegistryBaseKey -Hive $location.Hive
    try {
        $key = $baseKey.OpenSubKey($location.SubKey, $false)
        if ($null -eq $key) { return }
        try {
            if (@($key.GetValueNames()).Count -ne 0 -or @($key.GetSubKeyNames()).Count -ne 0) {
                throw "REGISTRY_ROLLBACK_KEY_NOT_EMPTY path=$Path"
            }
        }
        finally { $key.Dispose() }
        $baseKey.DeleteSubKey($location.SubKey, $false)
    }
    finally { $baseKey.Dispose() }
}

function Restore-GaltekServiceConfiguration {
    param([Parameter(Mandatory = $true)] $Snapshot)

    $name = [string]$Snapshot.Name
    $current = Get-Service -Name $name -ErrorAction SilentlyContinue
    if (-not [bool]$Snapshot.Exists) {
        if ($null -ne $current) {
            if ($current.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
                Stop-Service -Name $name -ErrorAction Stop
                Wait-GaltekServiceState -Name $name -Expected 'Stopped'
            }
            Invoke-GaltekSc -Arguments @('delete', $name)
            Wait-GaltekServiceState -Name $name -Expected 'Absent'
        }
        return
    }

    if ($null -ne $current -and $current.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $name -ErrorAction Stop
        Wait-GaltekServiceState -Name $name -Expected 'Stopped'
    }

    $startMode = switch ([string]$Snapshot.StartMode) {
        'Auto' { if ([int]$Snapshot.DelayedAutoStart -eq 1) { 'delayed-auto' } else { 'auto' } }
        'Manual' { 'demand' }
        'Disabled' { 'disabled' }
        default { 'demand' }
    }
    $account = if ([string]$Snapshot.StartName -in @('LocalSystem', 'Local System')) { 'LocalSystem' } else { [string]$Snapshot.StartName }
    if ($null -eq $current) {
        Invoke-GaltekSc -Arguments @('create', $name, 'binPath=', [string]$Snapshot.PathName, 'DisplayName=', [string]$Snapshot.DisplayName, 'start=', $startMode, 'obj=', $account)
    }
    else {
        Invoke-GaltekSc -Arguments @('config', $name, 'binPath=', [string]$Snapshot.PathName, 'DisplayName=', [string]$Snapshot.DisplayName, 'start=', $startMode, 'obj=', $account)
    }
    Invoke-GaltekSc -Arguments @('description', $name, [string]$Snapshot.Description)

    $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
    $pathKindProperty = $Snapshot.PSObject.Properties['PathNameKind']
    $pathKind = if ($null -eq $pathKindProperty -or [string]::IsNullOrWhiteSpace([string]$pathKindProperty.Value)) { 'ExpandString' } else { [string]$pathKindProperty.Value }
    $pathSnapshot = [pscustomobject]@{
        KeyExists = $true
        ValueExists = $true
        Value = [string]$Snapshot.PathName
        Kind = $pathKind
    }
    # sc.exe receives a semantically correct argv value but is not an authority
    # for the exact raw Registry representation. Restore PRE-STATE byte semantics
    # through a writable Registry64 handle before attempting runtime restoration.
    Restore-GaltekRegistryValue -Path $registryPath -Name 'ImagePath' -Snapshot $pathSnapshot
}

function Restore-GaltekServiceRegistryState {
    param([Parameter(Mandatory = $true)] $Snapshot)
    if (-not [bool]$Snapshot.Exists) { return }

    $name = [string]$Snapshot.Name
    $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
    $failureSnapshot = [pscustomobject]@{
        KeyExists = $true
        ValueExists = [bool]$Snapshot.FailureActionsExists
        Value = ConvertFrom-GaltekBase64 -Value ([string]$Snapshot.FailureActionsBase64)
        Kind = 'Binary'
    }
    $failureFlagSnapshot = [pscustomobject]@{
        KeyExists = $true
        ValueExists = [bool]$Snapshot.FailureFlagExists
        Value = $Snapshot.FailureFlag
        Kind = 'DWord'
    }
    Restore-GaltekRegistryValue -Path $registryPath -Name 'FailureActions' -Snapshot $failureSnapshot
    Restore-GaltekRegistryValue -Path $registryPath -Name 'FailureActionsOnNonCrashFailures' -Snapshot $failureFlagSnapshot
}

function Restore-GaltekServiceRuntime {
    param([Parameter(Mandatory = $true)] $Snapshot)

    if (-not [bool]$Snapshot.Exists) { return }
    $name = [string]$Snapshot.Name
    $restored = Get-Service -Name $name -ErrorAction Stop
    if ([bool]$Snapshot.WasRunning -and $restored.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Invoke-GaltekSc -Arguments @('start', $name)
        Wait-GaltekServiceState -Name $name -Expected 'Running'
    }
    elseif (-not [bool]$Snapshot.WasRunning -and $restored.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $name -ErrorAction Stop
        Wait-GaltekServiceState -Name $name -Expected 'Stopped'
    }
}

function Restore-GaltekServiceState {
    param([Parameter(Mandatory = $true)] $Snapshot)
    Restore-GaltekServiceConfiguration -Snapshot $Snapshot
    Restore-GaltekServiceRuntime -Snapshot $Snapshot
}

function Restore-GaltekTaskState {
    param([Parameter(Mandatory = $true)] $Snapshot)

    if (-not [bool]$Snapshot.Exists) {
        Stop-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $GaltekStateTaskName -Confirm:$false -ErrorAction SilentlyContinue
        return
    }

    $xml = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String([string]$Snapshot.DefinitionXmlBase64))
    Register-ScheduledTask -TaskName $GaltekStateTaskName -Xml $xml -Force -ErrorAction Stop | Out-Null
    if ([bool]$Snapshot.WasEnabled) { Enable-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction Stop | Out-Null }
    else { Disable-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction Stop | Out-Null }
    if ([bool]$Snapshot.WasRunning -and [bool]$Snapshot.WasEnabled) {
        Start-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction Stop
    }
    elseif (-not [bool]$Snapshot.WasRunning) {
        Stop-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction SilentlyContinue
    }
}

function Assert-GaltekRegistryValueRestored {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Name,
        [Parameter(Mandatory = $true)] $Snapshot
    )
    $actual = Get-GaltekRegistryValueSnapshot -Path $Path -Name $Name
    if ([bool]$actual.KeyExists -ne [bool]$Snapshot.KeyExists -or
        [bool]$actual.ValueExists -ne [bool]$Snapshot.ValueExists -or
        ([bool]$Snapshot.ValueExists -and -not (Test-GaltekRegistryValuesEqual -Left $actual.Value -Right $Snapshot.Value))) {
        throw "ROLLBACK_VERIFY_REGISTRY_MISMATCH path=$Path name=$Name"
    }
}

function Assert-GaltekRollbackStateRestored {
    param([Parameter(Mandatory = $true)] $Plan)

    foreach ($snapshot in @($Plan.Services)) {
        $name = [string]$snapshot.Name
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if (-not [bool]$snapshot.Exists) {
            if ($null -ne $service) { throw "ROLLBACK_VERIFY_SERVICE_PRESENT service=$name" }
            continue
        }
        if ($null -eq $service) { throw "ROLLBACK_VERIFY_SERVICE_MISSING service=$name" }
        $expectedRuntime = if ([bool]$snapshot.WasRunning) { 'Running' } else { 'Stopped' }
        if ([string]$service.Status -ne $expectedRuntime) { throw "ROLLBACK_VERIFY_SERVICE_RUNTIME service=$name expected=$expectedRuntime actual=$($service.Status)" }

        $servicePath = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
        $actualPath = Get-GaltekRegistryValueSnapshot -Path $servicePath -Name 'ImagePath'
        if (-not [bool]$actualPath.ValueExists -or [string]$actualPath.Value -cne [string]$snapshot.PathName) {
            throw "ROLLBACK_VERIFY_SERVICE_IMAGE_PATH service=$name"
        }
        $actualStart = Get-GaltekRegistryValueSnapshot -Path $servicePath -Name 'Start'
        $expectedStart = switch ([string]$snapshot.StartMode) { 'Auto' { 2 } 'Manual' { 3 } 'Disabled' { 4 } default { 3 } }
        if (-not [bool]$actualStart.ValueExists -or [int]$actualStart.Value -ne $expectedStart) { throw "ROLLBACK_VERIFY_SERVICE_START service=$name" }
        $actualDelayed = Get-GaltekRegistryValueSnapshot -Path $servicePath -Name 'DelayedAutostart'
        if ([int]$actualDelayed.Value -ne [int]$snapshot.DelayedAutoStart) { throw "ROLLBACK_VERIFY_SERVICE_DELAYED_AUTO service=$name" }
        $actualAccount = Get-GaltekRegistryValueSnapshot -Path $servicePath -Name 'ObjectName'
        $expectedAccount = if ([string]$snapshot.StartName -in @('LocalSystem', 'Local System')) { @('LocalSystem', 'Local System') } else { @([string]$snapshot.StartName) }
        if (-not [bool]$actualAccount.ValueExists -or $expectedAccount -notcontains [string]$actualAccount.Value) { throw "ROLLBACK_VERIFY_SERVICE_ACCOUNT service=$name" }
        $actualDisplayName = Get-GaltekRegistryValueSnapshot -Path $servicePath -Name 'DisplayName'
        if (-not [bool]$actualDisplayName.ValueExists -or [string]$actualDisplayName.Value -cne [string]$snapshot.DisplayName) { throw "ROLLBACK_VERIFY_SERVICE_DISPLAY_NAME service=$name" }
        $actualDescription = Get-GaltekRegistryValueSnapshot -Path $servicePath -Name 'Description'
        if ([string]$actualDescription.Value -cne [string]$snapshot.Description) { throw "ROLLBACK_VERIFY_SERVICE_DESCRIPTION service=$name" }
        $failureExpected = [pscustomobject]@{ KeyExists=$true; ValueExists=[bool]$snapshot.FailureActionsExists; Value=(ConvertFrom-GaltekBase64 -Value ([string]$snapshot.FailureActionsBase64)); Kind='Binary' }
        $failureFlagExpected = [pscustomobject]@{ KeyExists=$true; ValueExists=[bool]$snapshot.FailureFlagExists; Value=$snapshot.FailureFlag; Kind='DWord' }
        Assert-GaltekRegistryValueRestored -Path $servicePath -Name 'FailureActions' -Snapshot $failureExpected
        Assert-GaltekRegistryValueRestored -Path $servicePath -Name 'FailureActionsOnNonCrashFailures' -Snapshot $failureFlagExpected
    }

    $task = Get-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction SilentlyContinue
    if (-not [bool]$Plan.SessionTask.Exists) {
        if ($null -ne $task) { throw 'ROLLBACK_VERIFY_SESSION_PRESENT' }
    }
    else {
        if ($null -eq $task) { throw 'ROLLBACK_VERIFY_SESSION_MISSING' }
        $enabled = [string]$task.State -ne 'Disabled'
        if ($enabled -ne [bool]$Plan.SessionTask.WasEnabled) { throw 'ROLLBACK_VERIFY_SESSION_ENABLED' }
        if ([bool]$Plan.SessionTask.WasRunning -and [string]$task.State -ne 'Running') { throw 'ROLLBACK_VERIFY_SESSION_RUNTIME' }
        $actualXml = Export-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction Stop
        $expectedXml = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String([string]$Plan.SessionTask.DefinitionXmlBase64))
        $actualDocument = [xml]$actualXml
        $expectedDocument = [xml]$expectedXml
        if ($actualDocument.DocumentElement.OuterXml -ne $expectedDocument.DocumentElement.OuterXml) { throw 'ROLLBACK_VERIFY_SESSION_XML' }
    }

    $provider = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$GaltekStateCredentialProviderClsid"
    $filter = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\$GaltekStateCredentialProviderClsid"
    $class = "HKLM:\SOFTWARE\Classes\CLSID\$GaltekStateCredentialProviderClsid"
    $inproc = Join-Path $class 'InprocServer32'
    Assert-GaltekRegistryValueRestored -Path $provider -Name '' -Snapshot $Plan.CredentialProvider.ProviderDefault
    Assert-GaltekRegistryValueRestored -Path $filter -Name '' -Snapshot $Plan.CredentialProvider.FilterDefault
    Assert-GaltekRegistryValueRestored -Path $class -Name '' -Snapshot $Plan.CredentialProvider.ClassDefault
    Assert-GaltekRegistryValueRestored -Path $inproc -Name '' -Snapshot $Plan.CredentialProvider.InprocDefault
    Assert-GaltekRegistryValueRestored -Path $inproc -Name 'ThreadingModel' -Snapshot $Plan.CredentialProvider.ThreadingModel
}

function Test-GaltekCredentialProviderRollbackValue {
    param([Parameter(Mandatory = $true)][string] $Field, [AllowNull()] $Value)
    $text = [string]$Value
    switch ($Field) {
        'ProviderDefault' { return $text -eq 'Galtek Classroom Credential Provider' }
        'ClassDefault' { return $text -eq 'Galtek Classroom Credential Provider' }
        'ThreadingModel' { return $text -eq 'Apartment' }
        'InprocDefault' {
            if ([string]::IsNullOrWhiteSpace($text)) { return $false }
            try {
                $full = [IO.Path]::GetFullPath($text.Trim().Trim('"'))
                return [IO.Path]::GetFileName($full) -eq 'GaltekClassroom.CredentialProvider.dll' -and
                    $full -match '(?i)\\Galtek\\Classroom\\Agent\\CredentialProvider\\'
            }
            catch { return $false }
        }
        default { return $false }
    }
}

function Restore-GaltekCredentialProviderState {
    param([Parameter(Mandatory = $true)] $Snapshot)
    $provider = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$GaltekStateCredentialProviderClsid"
    $filter = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\$GaltekStateCredentialProviderClsid"
    $class = "HKLM:\SOFTWARE\Classes\CLSID\$GaltekStateCredentialProviderClsid"
    $inproc = Join-Path $class 'InprocServer32'
    Restore-GaltekRegistryValue -Path $provider -Name '' -Snapshot $Snapshot.ProviderDefault -ValidateCurrentValue { param($value) Test-GaltekCredentialProviderRollbackValue -Field 'ProviderDefault' -Value $value }
    Restore-GaltekRegistryValue -Path $filter -Name '' -Snapshot $Snapshot.FilterDefault -ValidateCurrentValue { return $false }
    Restore-GaltekRegistryValue -Path $class -Name '' -Snapshot $Snapshot.ClassDefault -ValidateCurrentValue { param($value) Test-GaltekCredentialProviderRollbackValue -Field 'ClassDefault' -Value $value }
    Restore-GaltekRegistryValue -Path $inproc -Name '' -Snapshot $Snapshot.InprocDefault -ValidateCurrentValue { param($value) Test-GaltekCredentialProviderRollbackValue -Field 'InprocDefault' -Value $value }
    Restore-GaltekRegistryValue -Path $inproc -Name 'ThreadingModel' -Snapshot $Snapshot.ThreadingModel -ValidateCurrentValue { param($value) Test-GaltekCredentialProviderRollbackValue -Field 'ThreadingModel' -Value $value }

    # Delete only empty Galtek-owned keys which did not exist before the transaction.
    Remove-GaltekRegistryKeyIfOriginallyAbsent -Path $inproc -Snapshot $Snapshot.InprocDefault
    Remove-GaltekRegistryKeyIfOriginallyAbsent -Path $class -Snapshot $Snapshot.ClassDefault
    Remove-GaltekRegistryKeyIfOriginallyAbsent -Path $provider -Snapshot $Snapshot.ProviderDefault
    Remove-GaltekRegistryKeyIfOriginallyAbsent -Path $filter -Snapshot $Snapshot.FilterDefault
}

function Invoke-GaltekCaptureState {
    Save-GaltekRollbackState -State (Get-GaltekLifecycleStateSnapshot)
    Write-Output 'GALTEK_SETUP_STAGE=STATE_CAPTURED'
}

function Invoke-GaltekRollbackPrepare {
    $state = Read-GaltekRollbackState
    if ($null -eq $state) { return }
    foreach ($serviceSnapshot in @($state.Services)) {
        $service = Get-Service -Name ([string]$serviceSnapshot.Name) -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name ([string]$serviceSnapshot.Name) -ErrorAction Stop
        }
    }
    Stop-ScheduledTask -TaskName $GaltekStateTaskName -ErrorAction SilentlyContinue
    Write-Output 'GALTEK_SETUP_STAGE=ROLLBACK_PREPARED'
}

function Invoke-GaltekRollbackState {
    $state = Read-GaltekRollbackState
    if ($null -eq $state) { return }
    $plan = Get-GaltekLifecycleRestorePlan -Before $state
    $failures = [Collections.Generic.List[object]]::new()

    function Invoke-RollbackComponent {
        param(
            [Parameter(Mandatory = $true)][string] $Step,
            [Parameter(Mandatory = $true)][string] $Component,
            [Parameter(Mandatory = $true)][scriptblock] $Operation
        )
        Write-Output "GALTEK_ROLLBACK_FINAL_STEP=$Step"
        try { & $Operation }
        catch {
            $exception = $_.Exception
            $hresultValue = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$exception.HResult), 0)
            $hresult = '0x' + $hresultValue.ToString('X8')
            $reason = ($exception.Message -replace '[\r\n]+', ' ').Trim()
            if ($reason.Length -gt 240) { $reason = $reason.Substring(0, 240) }
            Write-Output "GALTEK_ROLLBACK_FINAL_FAILED component=$Component step=$Step reason=$reason exception=$($exception.GetType().Name) hresult=$hresult"
            [void]$failures.Add([pscustomobject]@{ Component = $Component; Step = $Step; Reason = $reason; Exception = $exception.GetType().Name; HResult = $hresult })
        }
    }

    Write-Output 'GALTEK_ROLLBACK_FINAL_STEP=BEGIN'
    Invoke-RollbackComponent -Step 'SERVICE_CONFIG' -Component 'SERVICE' -Operation {
        foreach ($service in @($plan.Services)) { Restore-GaltekServiceConfiguration -Snapshot $service }
    }
    Invoke-RollbackComponent -Step 'SERVICE_RUNTIME' -Component 'SERVICE' -Operation {
        foreach ($service in @($plan.Services)) { Restore-GaltekServiceRuntime -Snapshot $service }
    }
    Invoke-RollbackComponent -Step 'SESSION' -Component 'SESSION' -Operation {
        Restore-GaltekTaskState -Snapshot $plan.SessionTask
    }
    Invoke-RollbackComponent -Step 'CP' -Component 'CP' -Operation {
        Restore-GaltekCredentialProviderState -Snapshot $plan.CredentialProvider
    }
    Invoke-RollbackComponent -Step 'REGISTRY' -Component 'REGISTRY' -Operation {
        foreach ($service in @($plan.Services)) { Restore-GaltekServiceRegistryState -Snapshot $service }
    }
    Invoke-RollbackComponent -Step 'VERIFY' -Component 'VERIFY' -Operation {
        if ($null -eq (Read-GaltekRollbackState)) { throw 'ROLLBACK_SNAPSHOT_DISAPPEARED_BEFORE_VERIFY' }
        Assert-GaltekRollbackStateRestored -Plan $plan
    }

    if ($failures.Count -gt 0) {
        $components = (($failures | ForEach-Object Component | Select-Object -Unique) -join ',')
        throw "GALTEK_ROLLBACK_FINAL_FAILED components=$components"
    }

    # Preserve the snapshot on every component failure. Only a complete final
    # restore (or a later successful MSI commit) owns cleanup.
    Remove-GaltekRollbackState
    Write-Output 'GALTEK_ROLLBACK_FINAL_STEP=COMPLETE'
    Write-Output 'GALTEK_SETUP_STAGE=ROLLBACK_COMPLETE'
}
