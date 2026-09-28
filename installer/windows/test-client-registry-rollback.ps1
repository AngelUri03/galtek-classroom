#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-RegistryRollback {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "REGISTRY_ROLLBACK_FIXTURE_FAILED: $Message" }
}

. (Join-Path $PSScriptRoot 'setup\client-state.ps1')

$fixtureId = [Guid]::NewGuid().ToString('N')
$fixtureSubKey = "Software\Galtek\Classroom\InstallerTests\$fixtureId"
$fixturePath = "HKCU:\$fixtureSubKey"
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $key = $base.CreateSubKey($fixtureSubKey, [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree)
    try { $key.SetValue('Existing', 'captured-value', [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $key.Dispose() }

    $capturedExisting = Get-GaltekRegistryValueSnapshot -Path $fixturePath -Name 'Existing'
    $capturedMissing = Get-GaltekRegistryValueSnapshot -Path $fixturePath -Name 'CreatedByInstaller'
    Assert-RegistryRollback ($capturedExisting.ValueExists -and $capturedExisting.Value -eq 'captured-value') 'Read-only capture missed the existing value.'
    Assert-RegistryRollback (-not $capturedMissing.ValueExists) 'Absent value was not captured as absent.'

    $key = $base.OpenSubKey($fixtureSubKey, $true)
    try {
        $key.SetValue('Existing', 'installer-value', [Microsoft.Win32.RegistryValueKind]::String)
        $key.SetValue('CreatedByInstaller', 'installer-value', [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally { $key.Dispose() }

    $installerValueGuard = { param($value) return [string]$value -eq 'installer-value' }
    Restore-GaltekRegistryValue -Path $fixturePath -Name 'Existing' -Snapshot $capturedExisting -ValidateCurrentValue $installerValueGuard
    Restore-GaltekRegistryValue -Path $fixturePath -Name 'CreatedByInstaller' -Snapshot $capturedMissing -ValidateCurrentValue $installerValueGuard
    # A second pass must converge without errors or recreate the absent value.
    Restore-GaltekRegistryValue -Path $fixturePath -Name 'Existing' -Snapshot $capturedExisting -ValidateCurrentValue $installerValueGuard
    Restore-GaltekRegistryValue -Path $fixturePath -Name 'CreatedByInstaller' -Snapshot $capturedMissing -ValidateCurrentValue $installerValueGuard

    $key = $base.OpenSubKey($fixtureSubKey, $false)
    try {
        Assert-RegistryRollback ($key.GetValue('Existing') -eq 'captured-value') 'Captured value was not restored through a writable handle.'
        Assert-RegistryRollback (-not (@($key.GetValueNames()) -contains 'CreatedByInstaller')) 'Value absent before the transaction was not removed.'
    }
    finally { $key.Dispose() }

    $key = $base.OpenSubKey($fixtureSubKey, $true)
    try { $key.SetValue('CreatedByInstaller', 'foreign-value', [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $key.Dispose() }
    $rejected = $false
    try {
        Restore-GaltekRegistryValue -Path $fixturePath -Name 'CreatedByInstaller' -Snapshot $capturedMissing -ValidateCurrentValue $installerValueGuard
    }
    catch {
        $rejected = $_.Exception.Message -match 'REGISTRY_ROLLBACK_UNEXPECTED_VALUE'
    }
    Assert-RegistryRollback $rejected 'Unexpected foreign Registry value did not fail closed.'
    $key = $base.OpenSubKey($fixtureSubKey, $false)
    try { Assert-RegistryRollback ($key.GetValue('CreatedByInstaller') -eq 'foreign-value') 'Fail-closed rollback modified the foreign value.' }
    finally { $key.Dispose() }
}
finally {
    try { $base.DeleteSubKeyTree($fixtureSubKey, $false) } finally { $base.Dispose() }
}

Write-Output 'Registry rollback regression passed using HKCU only: writable restore, scoped delete, idempotency, and fail-closed foreign value.'
