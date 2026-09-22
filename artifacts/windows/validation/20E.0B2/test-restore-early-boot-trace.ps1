#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'early-boot-trace-registry-helpers.ps1')

$testPath = "Software\GaltekClassroomTests\F8-$([guid]::NewGuid().ToString('N'))"
$testKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testPath, $true)
if ($null -eq $testKey) { throw 'Could not create temporary HKCU regression key.' }

try {
    $testKey.SetValue(
        'Environment',
        [string[]]@('COREHOST_TRACE=1', 'COREHOST_TRACE_VERBOSITY=4'),
        [Microsoft.Win32.RegistryValueKind]::MultiString)

    Restore-ServiceEnvironmentValue -WritableServiceKey $testKey -PreviousExisted $false -PreviousEnvironment @()
    Restore-ServiceEnvironmentValue -WritableServiceKey $testKey -PreviousExisted $false -PreviousEnvironment @()

    if ($testKey.GetValueNames() -contains 'Environment') {
        throw 'Environment remained after idempotent absent-state restore.'
    }

    $testKey.SetValue('Environment', [string[]]@('FOREIGN_VALUE=keep'), [Microsoft.Win32.RegistryValueKind]::MultiString)
    $exception = $null
    try {
        Restore-ServiceEnvironmentValue -WritableServiceKey $testKey -PreviousExisted $false -PreviousEnvironment @()
    }
    catch {
        $exception = $_.Exception
    }
    if ($null -eq $exception -or $testKey.GetValueNames() -notcontains 'Environment') {
        throw 'Restore helper did not preserve a foreign Environment entry.'
    }

    $testKey.DeleteValue('Environment', $false)
    $previous = @('COREHOST_TRACE=0', 'COREHOST_TRACE_VERBOSITY=2')
    Restore-ServiceEnvironmentValue -WritableServiceKey $testKey -PreviousExisted $true -PreviousEnvironment $previous
    Restore-ServiceEnvironmentValue -WritableServiceKey $testKey -PreviousExisted $true -PreviousEnvironment $previous
    $effective = Get-ServiceEnvironmentValue -Key $testKey
    if (-not $effective.Exists -or (Get-ServiceEnvironmentStateHash -Values $effective.Values) -cne (Get-ServiceEnvironmentStateHash -Values $previous)) {
        throw 'Present-state restore was not idempotent.'
    }

    Write-Host 'F8 registry restore regression tests passed.'
}
finally {
    $testKey.Dispose()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testPath, $false)
}
