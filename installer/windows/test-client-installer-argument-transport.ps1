#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Transport {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "ARGUMENT_TRANSPORT_FAILED: $Message" }
}

function Invoke-RawPowerShellArguments {
    param([Parameter(Mandatory = $true)][string] $Arguments)
    $powershell = (Get-Command powershell.exe -ErrorAction Stop).Source
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $powershell
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.Trim(); Error = $stderr.Trim() }
}

$packagePath = Join-Path $PSScriptRoot 'setup\Package.wxs'
[xml]$package = Get-Content -LiteralPath $packagePath -Raw
$namespace = [Xml.XmlNamespaceManager]::new($package.NameTable)
$namespace.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Galtek argument transport ' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $probePath = Join-Path $fixtureRoot 'transport probe.ps1'
    [IO.File]::WriteAllText($probePath, @'
param([string] $Action, [string] $InstallDirectory, [switch] $InitialInstall)
$normalized = try { [IO.Path]::GetFullPath($InstallDirectory) } catch { $null }
[ordered]@{
    action = $Action
    raw = $InstallDirectory
    normalized = $normalized
    initial = [bool]$InitialInstall
} | ConvertTo-Json -Compress
'@, [Text.UTF8Encoding]::new($false))

    $actions = [ordered]@{
        ConfigureClientInitial = @{ Action = 'Configure'; Initial = $true }
        ConfigureClientMaintenance = @{ Action = 'Configure'; Initial = $false }
        RollbackClientConfiguration = @{ Action = 'Rollback'; Initial = $false }
        CommitClientConfiguration = @{ Action = 'Commit'; Initial = $false }
        UnconfigureClient = @{ Action = 'Unconfigure'; Initial = $false }
    }
    $roots = @(
        'C:\Program Files\Galtek\Classroom\Agent\',
        'C:\Program Files\Galtek Classroom Test\Agent\'
    )

    foreach ($entry in $actions.GetEnumerator()) {
        $node = $package.SelectSingleNode("//w:SetProperty[@Id='$($entry.Key)']", $namespace)
        Assert-Transport ($null -ne $node) "SetProperty is missing: $($entry.Key)"
        $template = [string]$node.Value
        Assert-Transport ($template.Contains('&quot;') -eq $false) "XML entities were not decoded for $($entry.Key)"
        Assert-Transport ($template.Contains('[INSTALLFOLDER].')) "Normalized dot-segment transport is missing for $($entry.Key)"

        foreach ($root in $roots) {
            $command = $template.Replace('[System64Folder]WindowsPowerShell\v1.0\powershell.exe', (Get-Command powershell.exe).Source).
                Replace('[#filInstallerLifecycle]', $probePath).
                Replace('[INSTALLFOLDER]', $root)
            $match = [regex]::Match($command, '^"[^"]+"\s+(?<arguments>.*)$')
            Assert-Transport $match.Success "Could not split WixQuietExec command for $($entry.Key)"
            $result = Invoke-RawPowerShellArguments -Arguments $match.Groups['arguments'].Value
            Assert-Transport ($result.ExitCode -eq 0) "$($entry.Key) failed for '$root': $($result.Error)"
            $received = $result.Output | ConvertFrom-Json
            $expected = [IO.Path]::GetFullPath($root).TrimEnd('\')
            Assert-Transport ([string]::Equals([string]$received.normalized, $expected, [StringComparison]::OrdinalIgnoreCase)) "$($entry.Key) normalized path mismatch. Received '$($received.normalized)'"
            Assert-Transport (([string]$received.raw).EndsWith('\.')) "$($entry.Key) did not receive the unambiguous directory sentinel."
            Assert-Transport ([string]$received.action -eq [string]$entry.Value.Action) "$($entry.Key) action mismatch."
            Assert-Transport ([bool]$received.initial -eq [bool]$entry.Value.Initial) "$($entry.Key) InitialInstall mismatch."
        }
    }

    $oldArguments = "-NoProfile -File `"$probePath`" -Action Rollback -InstallDirectory `"C:\Program Files\Galtek\Classroom\Agent\`""
    $oldResult = Invoke-RawPowerShellArguments -Arguments $oldArguments
    $oldEquivalent = $false
    $oldDiagnostic = "exitCode=$($oldResult.ExitCode)"
    if ($oldResult.ExitCode -eq 0) {
        try {
            $oldReceived = $oldResult.Output | ConvertFrom-Json
            $oldDiagnostic = "received=$([string]$oldReceived.raw)"
            $oldEquivalent = [string]::Equals([string]$oldReceived.normalized, 'C:\Program Files\Galtek\Classroom\Agent\', [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $oldEquivalent = $false
            $oldDiagnostic = "output=$($oldResult.Output.Replace("`r", '<CR>').Replace("`n", '<LF>')) error=$($oldResult.Error.Replace("`r", '<CR>').Replace("`n", '<LF>'))"
        }
    }
    Assert-Transport (-not $oldEquivalent) 'The regression fixture must reproduce corruption from a trailing backslash before the closing quote.'

    $oldInitialArguments = "-NoProfile -File `"$probePath`" -Action Configure -InstallDirectory `"C:\Program Files\Galtek\Classroom\Agent\`" -InitialInstall"
    $oldInitialResult = Invoke-RawPowerShellArguments -Arguments $oldInitialArguments
    $oldInitialDiagnostic = "exitCode=$($oldInitialResult.ExitCode)"
    if ($oldInitialResult.ExitCode -eq 0) {
        $oldInitialReceived = $oldInitialResult.Output | ConvertFrom-Json
        $oldInitialDiagnostic = "received=$([string]$oldInitialReceived.raw) initial=$([bool]$oldInitialReceived.initial)"
        Assert-Transport ($null -eq $oldInitialReceived.normalized) 'The old initial-install transport unexpectedly produced a valid path.'
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Client installer argument transport tests passed (5 phases x 2 spaced paths).'
Write-Output "Defective trailing-separator regression: $oldDiagnostic"
Write-Output "Defective initial-install regression: $oldInitialDiagnostic"
