#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-F3DService([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "F3D_SERVICE_REGISTRY_FAILED: $Message" }
}

. (Join-Path $PSScriptRoot 'setup\legacy-adoption.ps1')
$lifecyclePath = Join-Path $PSScriptRoot 'setup\client-lifecycle.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($lifecyclePath, [ref]$tokens, [ref]$errors)
Assert-F3DService ($errors.Count -eq 0) "Lifecycle parser errors: $($errors -join '; ')"
foreach ($name in @('Open-GaltekServiceRegistryBaseKey', 'Get-GaltekServiceLiveState', 'Assert-ServiceContract')) {
    $functionAst = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true))[0]
    Assert-F3DService ($null -ne $functionAst) "Function missing: $name"
    Invoke-Expression $functionAst.Extent.Text
}

$script:keyDisposed = $false
$script:baseDisposed = $false
$script:openedWritable = $null
$root = 'C:\Program Files\Galtek\Classroom\Agent'
$expected = Join-Path $root 'GaltekClassroom.Agent.Service.exe'
$failure = [byte[]]::new(44)
[BitConverter]::GetBytes([uint32]86400).CopyTo($failure, 0)
[BitConverter]::GetBytes([uint32]3).CopyTo($failure, 12)
[BitConverter]::GetBytes([uint32]20).CopyTo($failure, 16)
foreach ($entry in @(@(20,5000), @(28,15000), @(36,60000))) {
    [BitConverter]::GetBytes([uint32]1).CopyTo($failure, $entry[0])
    [BitConverter]::GetBytes([uint32]$entry[1]).CopyTo($failure, $entry[0] + 4)
}
$script:values = @{
    ImagePath = '"' + $expected + '"'
    Start = 2
    ObjectName = 'LocalSystem'
    DelayedAutostart = 1
    FailureActionsOnNonCrashFailures = 1
    FailureActions = $failure
}
$script:fakeRegistry = [pscustomobject]@{}
$script:fakeRegistry | Add-Member ScriptMethod GetValue {
    param($name, $defaultValue, $options)
    if ($script:values.ContainsKey([string]$name)) { return $script:values[[string]$name] }
    return $defaultValue
}
$script:fakeRegistry | Add-Member ScriptMethod Dispose { $script:keyDisposed = $true }
$script:fakeBase = [pscustomobject]@{}
$script:fakeBase | Add-Member ScriptMethod OpenSubKey {
    param($path, $writable)
    $script:openedWritable = [bool]$writable
    return $script:fakeRegistry
}
$script:fakeBase | Add-Member ScriptMethod Dispose { $script:baseDisposed = $true }

$ServiceName = 'GaltekClassroomAgent'
function Get-Service { param($Name, $ErrorAction) return [pscustomobject]@{ Status = 'Running' } }
function Open-GaltekServiceRegistryBaseKey { return $script:fakeBase }

# This exact StrictMode call failed in F3C with VariableIsUndefined at the old
# `$failure = [byte[]]$registry.FailureActions` statement.
Assert-ServiceContract -ExecutablePath $expected
Assert-F3DService $script:keyDisposed 'The live Service Registry key was not disposed.'
Assert-F3DService $script:baseDisposed 'The Registry64 base key was not disposed.'
Assert-F3DService ($script:openedWritable -eq $false) 'Post-validation opened the live key writable.'

foreach ($raw in @(
    'C:\Temp\GaltekClassroom.Agent.Service.exe',
    ('"' + $expected + '" --evil'),
    (Join-Path $root 'GaltekClassroom.Agent.Service.exe.evil'),
    '\\server\share\GaltekClassroom.Agent.Service.exe',
    ($expected + ':evil'))) {
    $candidate = Resolve-GaltekServiceImagePath -CommandLine $raw -ExpectedExecutable $expected
    Assert-F3DService (-not $candidate.IsValid -or -not [string]::IsNullOrWhiteSpace([string]$candidate.Arguments)) "Unsafe ImagePath passed: $raw"
}

$source = Get-Content -LiteralPath $lifecyclePath -Raw
Assert-F3DService ($source.Contains('[Microsoft.Win32.RegistryView]::Registry64')) 'Explicit Registry64 live read is missing.'
Assert-F3DService ($source.Contains('$registry = $null') -and $source.Contains('if ($null -ne $registry) { $registry.Dispose() }')) 'Registry handle lifetime is not explicit.'
Assert-F3DService (-not $source.Contains('$failure = [byte[]]$registry.FailureActions')) 'The undefined F3C registry dereference remains.'

Write-Output 'F3D_SERVICE_REGISTRY_TESTS_PASS StrictMode live-Registry64 readonly dispose canonical exact-executable empty-arguments external UNC ADS lookalike rejected'
