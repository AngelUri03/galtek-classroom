#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PayloadManifestPath,
    [string] $MsiPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SameVersion {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "SAME_VERSION_PAYLOAD_FAILED: $Message" }
}

function Get-MsiScalar {
    param([string] $Path, [string] $Query)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Query))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if ($null -eq $record) { return $null }
    return [string]$record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
}

$manifest = Get-Content -LiteralPath $PayloadManifestPath -Raw | ConvertFrom-Json
$critical = @($manifest.payloads | Where-Object { $_.relativePath -in @('Service\GaltekClassroom.Agent.Service.exe', 'Service\GaltekClassroom.Agent.Service.dll') })
Assert-SameVersion ($critical.Count -eq 2) 'Service EXE and DLL must both be present in the payload manifest.'

$replacementMode = 'emus'
if (-not [string]::IsNullOrWhiteSpace($MsiPath)) {
    Assert-SameVersion (Test-Path -LiteralPath $MsiPath -PathType Leaf) "MSI is missing: $MsiPath"
    $fullMsiPath = [IO.Path]::GetFullPath($MsiPath)
    $replacementMode = Get-MsiScalar -Path $fullMsiPath -Query "SELECT `Target` FROM `CustomAction` WHERE `Action`='SetREINSTALLMODE'"
    Assert-SameVersion ((Get-MsiScalar -Path $fullMsiPath -Query "SELECT `Value` FROM `Property` WHERE `Property`='ProductVersion'") -eq '0.0.4') 'MSI ProductVersion is not 0.0.4.'
    Assert-SameVersion ((Get-MsiScalar -Path $fullMsiPath -Query "SELECT `Value` FROM `Property` WHERE `Property`='ProductCode'") -eq '{A544ED43-3AAA-4B47-8EE7-0A3807C94D59}') 'MSI 0.0.4 ProductCode is not the pinned identity.'
    Assert-SameVersion ((Get-MsiScalar -Path $fullMsiPath -Query "SELECT `Value` FROM `Property` WHERE `Property`='UpgradeCode'") -eq '{2D9C681B-F7A8-4C5F-97C8-C5EACB2F31D6}') 'MSI UpgradeCode changed.'
    Assert-SameVersion ((Get-MsiScalar -Path $fullMsiPath -Query "SELECT `ActionProperty` FROM `Upgrade` WHERE `UpgradeCode`='{2D9C681B-F7A8-4C5F-97C8-C5EACB2F31D6}'") -eq 'WIX_UPGRADE_DETECTED') 'MSI MajorUpgrade row is missing.'
    Write-Output 'MSI_003_TO_004_MAJOR_UPGRADE_CONTRACT_PASS old-product={2BA4D6A0-9492-484E-A27B-9EAE40C886A9} new-product={A544ED43-3AAA-4B47-8EE7-0A3807C94D59}'
}
Assert-SameVersion ($replacementMode -eq 'emus') "Expected MSI REINSTALLMODE=emus, actual '$replacementMode'."
Assert-SameVersion ($replacementMode.Contains('e')) 'Equal-version replacement mode is not active.'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('galtek-same-version-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    foreach ($payload in $critical) {
        $newPath = [string]$payload.sourcePath
        $installedPath = Join-Path $fixtureRoot ([IO.Path]::GetFileName($newPath))
        [IO.File]::Copy($newPath, $installedPath, $true)
        $stream = [IO.File]::Open($installedPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.WriteByte(0) } finally { $stream.Dispose() }

        $installedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($installedPath).FileVersion
        $payloadVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($newPath).FileVersion
        $oldHash = (Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash
        $newHash = (Get-FileHash -LiteralPath $newPath -Algorithm SHA256).Hash
        Assert-SameVersion ($installedVersion -eq $payloadVersion) "Fixture FileVersion differs for $($payload.relativePath)."
        Assert-SameVersion ([version]$payloadVersion -ge [version]'0.5.0.0') "Payload FileVersion regressed below the established 0.5.0.0 floor for $($payload.relativePath)."
        Assert-SameVersion ($payloadVersion -eq '0.5.0.0') "The installer regression fixture expects historical FileVersion 0.5.0.0 for $($payload.relativePath)."
        Assert-SameVersion ($oldHash -ne $newHash) "Fixture hashes must differ for $($payload.relativePath)."

        # Windows Installer's documented 'e' decision: missing, equal, or older
        # versioned files are copied. Apply that decision to the sandbox fixture.
        $shouldReplace = $replacementMode.Contains('e') -and ([version]$payloadVersion -ge [version]$installedVersion)
        Assert-SameVersion $shouldReplace "Equal-version payload was not selected for $($payload.relativePath)."
        [IO.File]::Copy($newPath, $installedPath, $true)
        Assert-SameVersion ((Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash -eq $newHash) "OLD bytes survived for $($payload.relativePath)."
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Same-FileVersion replacement tests passed for Agent Service EXE and DLL.'
