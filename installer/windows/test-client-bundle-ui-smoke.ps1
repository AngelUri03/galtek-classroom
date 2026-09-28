#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $BundlePath,
    [ValidateSet('FRESH', 'LEGACY_SUPPORTED', 'INSTALLED_SAME', 'INSTALLED_OLDER', 'INSTALLED_NEWER', 'REPAIRABLE_PARTIAL', 'BLOCKED_CONFLICT')]
    [string] $ExpectedState = 'FRESH',
    [ValidateRange(5, 120)][int] $TimeoutSeconds = 30,
    [ValidateRange(5, 120)][int] $CloseTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Smoke([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "CUSTOM_BA_SMOKE_FAILED: $Message" }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GaltekCustomBaSmokeWindow {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
'@

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
Assert-Smoke (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Run this smoke from a non-elevated PowerShell session.'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($BundlePath)) {
    $BundlePath = Join-Path $repositoryRoot 'artifacts\windows\installer\GaltekClassroom-Client-Setup-0.0.4.exe'
}
$resolvedBundle = [IO.Path]::GetFullPath($BundlePath)
Assert-Smoke (Test-Path -LiteralPath $resolvedBundle -PathType Leaf) "Bundle does not exist: $resolvedBundle"
Assert-Smoke ([IO.Path]::GetFileName($resolvedBundle) -eq 'GaltekClassroom-Client-Setup-0.0.4.exe') 'Smoke accepts only the 0.0.4 Client bundle.'

$logRoot = Join-Path $repositoryRoot 'artifacts\windows\installer\runtime-smoke'
[IO.Directory]::CreateDirectory($logRoot) | Out-Null
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$logPath = Join-Path $logRoot "GaltekClassroom-Client-Setup-0.0.4-custom-ba-smoke-$timestamp.log"
$startedAt = [DateTime]::UtcNow
$process = Start-Process -FilePath $resolvedBundle -ArgumentList @('-l', $logPath) -PassThru
$baProcess = $null
$sawWindow = $false
$sawDetectBegin = $false
$sawDetectComplete = $false
$sawExpectedState = $false
$windowMilliseconds = $null
$detectMilliseconds = $null
$workingSet = $null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    if ($null -eq $baProcess -or $baProcess.HasExited) {
        $baProcess = Get-Process -Name 'GaltekClassroom.Bootstrapper' -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime.ToUniversalTime() -ge $startedAt.AddSeconds(-2) } |
            Sort-Object StartTime -Descending | Select-Object -First 1
    }
    if ($null -ne $baProcess -and -not $baProcess.HasExited) {
        $baProcess.Refresh()
        if ($baProcess.MainWindowHandle -ne [IntPtr]::Zero) {
            if (-not $sawWindow) { $windowMilliseconds = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds }
            $sawWindow = $true
            $workingSet = $baProcess.WorkingSet64
        }
    }
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        $liveLog = Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue
        $sawDetectBegin = $liveLog -match '(?im)DETECT_BEGIN'
        if ($liveLog -match '(?im)DETECT_COMPLETE') {
            if (-not $sawDetectComplete) { $detectMilliseconds = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds }
            $sawDetectComplete = $true
        }
        $sawExpectedState = $liveLog -match "(?im)PRODUCT_STATE_RESOLVED state=$ExpectedState"
        Assert-Smoke ($liveLog -notmatch '(?im)\bPLAN_BEGIN\b|\bAPPLY_BEGIN\b|Launching elevated engine process') 'Plan, Apply, or elevation started during Detect-only smoke.'
    }
    if ($sawWindow -and $sawDetectBegin -and $sawDetectComplete -and $sawExpectedState) { break }
    Start-Sleep -Milliseconds 100
}

if ((-not $sawWindow -or -not $sawDetectBegin -or -not $sawDetectComplete -or -not $sawExpectedState) -and $null -ne $baProcess -and -not $baProcess.HasExited) {
    $baProcess.Refresh()
    if ($baProcess.MainWindowHandle -ne [IntPtr]::Zero) {
        [void][GaltekCustomBaSmokeWindow]::PostMessage($baProcess.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    }
}

Assert-Smoke $sawWindow 'Custom BA window did not appear before timeout.'
Assert-Smoke $sawDetectBegin 'Custom BA did not log DETECT_BEGIN.'
Assert-Smoke $sawDetectComplete 'Custom BA did not log DETECT_COMPLETE.'
Assert-Smoke $sawExpectedState "Safe laptop smoke expected $ExpectedState product state."
Assert-Smoke ($null -ne $baProcess -and -not $baProcess.HasExited) 'Custom BA child process was not available for normal close.'
Assert-Smoke ([GaltekCustomBaSmokeWindow]::PostMessage($baProcess.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) 'WM_CLOSE could not be posted to the real Custom BA window.'

$closeDeadline = [DateTime]::UtcNow.AddSeconds($CloseTimeoutSeconds)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $closeDeadline) {
    Start-Sleep -Milliseconds 100
    $process.Refresh()
}
Assert-Smoke $process.HasExited "Custom BA did not close within $CloseTimeoutSeconds seconds."

$log = Get-Content -LiteralPath $logPath -Raw
foreach ($pattern in @('\bPLAN_BEGIN\b', '\bAPPLY_BEGIN\b', 'Launching elevated engine process')) {
    Assert-Smoke ($log -notmatch "(?im)$pattern") "Forbidden smoke evidence found: $pattern"
}
Assert-Smoke ($log -match '(?im)BA_SHUTDOWN') 'Custom BA did not log BA_SHUTDOWN.'
Assert-Smoke (@(Get-Process -Name 'ClientStateHelper' -ErrorAction SilentlyContinue).Count -eq 0) 'Legacy detection helper remained alive after shutdown.'

Write-Output "CUSTOM_BA_SMOKE_PASS state=$ExpectedState windowMs=$windowMilliseconds detectCompleteMs=$detectMilliseconds workingSetBytes=$workingSet plan=false apply=false elevated=false exitCode=$($process.ExitCode)"
Write-Output "Log: $logPath"
