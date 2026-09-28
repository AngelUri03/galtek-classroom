#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideDirectory {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Parent,
        [Parameter(Mandatory = $true)][string] $Purpose
    )

    $fullPath = Resolve-FullPath $Path
    $fullParent = (Resolve-FullPath $Parent).TrimEnd('\') + '\'
    if (-not ($fullPath.TrimEnd('\') + '\').StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must remain below $fullParent. Actual: $fullPath"
    }
}

function Resolve-DotNet {
    $repoLocal = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $repoLocal -PathType Leaf) { return $repoLocal }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    throw 'The .NET SDK was not found. Install .NET SDK 8.0 or set up dotnet on PATH.'
}

function Resolve-MSBuild {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:VSINSTALLDIR)) {
        $candidates += (Join-Path $env:VSINSTALLDIR 'MSBuild\Current\Bin\MSBuild.exe')
    }
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidates += (Join-Path ([string]$installationPath) 'MSBuild\Current\Bin\MSBuild.exe')
        }
    }
    $fromPath = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) { $candidates += $fromPath.Source }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-FullPath $candidate) }
    }
    throw 'MSBuild was not found. Install Visual Studio Build Tools with the C++ desktop workload.'
}

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][scriptblock] $Command, [Parameter(Mandatory = $true)][string] $Stage)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Stage failed with exit code $LASTEXITCODE." }
}

function ConvertTo-CompressedEncodedCommand {
    param([Parameter(Mandatory = $true)][string] $Source)

    $sourceBytes = [Text.Encoding]::UTF8.GetBytes($Source)
    $buffer = [IO.MemoryStream]::new()
    try {
        $gzip = [IO.Compression.GZipStream]::new($buffer, [IO.Compression.CompressionMode]::Compress, $true)
        try { $gzip.Write($sourceBytes, 0, $sourceBytes.Length) }
        finally { $gzip.Dispose() }
        $compressed = [Convert]::ToBase64String($buffer.ToArray())
    }
    finally { $buffer.Dispose() }

    $wrapper = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(`$false);`$OutputEncoding=[Console]::OutputEncoding;`$b=[Convert]::FromBase64String('$compressed');`$m=[IO.MemoryStream]::new(`$b);`$g=[IO.Compression.GZipStream]::new(`$m,[IO.Compression.CompressionMode]::Decompress);`$r=[IO.StreamReader]::new(`$g,[Text.Encoding]::UTF8);try{& ([scriptblock]::Create(`$r.ReadToEnd()))}finally{`$r.Dispose();`$g.Dispose();`$m.Dispose()}"
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapper))
}

function ConvertTo-CppWideStringLiteral {
    param([Parameter(Mandatory = $true)][string] $Value)
    $lines = for ($offset = 0; $offset -lt $Value.Length; $offset += 3000) {
        $length = [Math]::Min(3000, $Value.Length - $offset)
        'L"' + $Value.Substring($offset, $length) + '"'
    }
    return ($lines -join "`r`n")
}

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$setupRoot = Resolve-FullPath (Join-Path $PSScriptRoot 'setup')
$versionPropsPath = Join-Path $setupRoot 'InstallerVersion.props'
[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$productVersion = [string]$versionProps.Project.PropertyGroup.ProductVersion
$wixVersion = [string]$versionProps.Project.PropertyGroup.WixToolsetVersion

if ($productVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid ProductVersion: $productVersion" }
if ($wixVersion -ne '5.0.2') { throw "Unexpected WiX version: $wixVersion" }

$artifactRoot = Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\installer')
$buildRoot = Resolve-FullPath (Join-Path $artifactRoot ".build-$productVersion")
$payloadRoot = Join-Path $buildRoot 'payload'
$msiOutput = Join-Path $buildRoot 'msi'
$bundleOutput = Join-Path $buildRoot 'bundle'
$baOutput = Join-Path $buildRoot 'custom-ba'
$baTestOutput = Join-Path $buildRoot 'custom-ba-tests'
$cleanupOutput = Join-Path $buildRoot 'legacy-cleanup'
$cleanupTestOutput = Join-Path $buildRoot 'legacy-cleanup-tests'
$generatedWxs = Join-Path $buildRoot 'generated\PublishedPayload.wxs'
$themeRoot = Join-Path $buildRoot 'theme'
$officialLogo = Join-Path $repositoryRoot 'logo.png'
$sourceThemeRoot = Join-Path $setupRoot 'theme'
$sourceBrandAssets = Join-Path $sourceThemeRoot 'assets'

Assert-PathInsideDirectory -Path $artifactRoot -Parent (Join-Path $repositoryRoot 'artifacts') -Purpose 'Installer output'
Assert-PathInsideDirectory -Path $buildRoot -Parent $artifactRoot -Purpose 'Installer build workspace'

if (Test-Path -LiteralPath $buildRoot) { Remove-Item -LiteralPath $buildRoot -Recurse -Force }
New-Item -ItemType Directory -Path $payloadRoot, $msiOutput, $bundleOutput, $baOutput, $baTestOutput, $cleanupOutput, $cleanupTestOutput -Force | Out-Null

Write-Output 'GALTEK_INSTALLER_STAGE=PREPARE_BRANDING'
& (Join-Path $setupRoot 'generate-brand-assets.ps1') -SourceLogo $officialLogo -OutputDirectory $sourceBrandAssets
Copy-Item -LiteralPath $sourceThemeRoot -Destination $themeRoot -Recurse

$servicePayload = Join-Path $payloadRoot 'Service'
$sessionPayload = Join-Path $payloadRoot 'Session'
$credentialPayload = Join-Path $payloadRoot 'CredentialProvider'

Write-Output "GALTEK_INSTALLER_STAGE=PUBLISH_PAYLOADS version=$productVersion"
& (Join-Path $PSScriptRoot 'publish-agent.ps1') `
    -ServiceOutputPath $servicePayload `
    -SessionOutputPath $sessionPayload `
    -CredentialProviderOutputPath $credentialPayload

& (Join-Path $PSScriptRoot 'test-credential-provider-package.ps1') -PackagePath $credentialPayload

$credentialManifest = Get-Content -LiteralPath (Join-Path $credentialPayload 'credential-provider.manifest.json') -Raw | ConvertFrom-Json
$credentialPackageId = [string]$credentialManifest.packageId
if ([string]::IsNullOrWhiteSpace($credentialPackageId)) { throw 'Credential Provider packageId is missing.' }

$setupPayload = Join-Path $payloadRoot 'Setup'
New-Item -ItemType Directory -Path $setupPayload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $setupRoot 'client-lifecycle.ps1') -Destination $setupPayload
Copy-Item -LiteralPath (Join-Path $setupRoot 'legacy-adoption.ps1') -Destination $setupPayload
Copy-Item -LiteralPath (Join-Path $setupRoot 'client-state.ps1') -Destination $setupPayload

$criticalPayloadPaths = @(
    @{ Source = 'Service\GaltekClassroom.Agent.Service.exe'; Installed = 'GaltekClassroom.Agent.Service.exe' },
    @{ Source = 'Service\GaltekClassroom.Agent.Service.dll'; Installed = 'GaltekClassroom.Agent.Service.dll' },
    @{ Source = 'Session\GaltekClassroom.Agent.Session.exe'; Installed = 'Session\GaltekClassroom.Agent.Session.exe' },
    @{ Source = 'CredentialProvider\GaltekClassroom.CredentialProvider.dll'; Installed = "CredentialProvider\versions\$credentialPackageId\GaltekClassroom.CredentialProvider.dll" }
)
$installedPayloadManifest = [ordered]@{ schemaVersion = 1; files = @() }
foreach ($definition in $criticalPayloadPaths) {
    $sourcePath = Join-Path $payloadRoot $definition.Source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Critical payload is missing: $($definition.Source)" }
    $installedPayloadManifest.files += [ordered]@{
        relativePath = $definition.Installed
        sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$installedPayloadManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $setupPayload 'installed-payload-manifest.json') -Encoding UTF8

$payloadEntries = @()
foreach ($file in Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | Sort-Object FullName) {
    $relative = $file.FullName.Substring($payloadRoot.Length).TrimStart('\')
    $payloadEntries += [ordered]@{
        relativePath = $relative
        sourcePath = $file.FullName
        size = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$gitCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve Git commit metadata.' }
$gitDirty = -not [string]::IsNullOrWhiteSpace((& git -C $repositoryRoot status --porcelain))
$buildTimestampUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')

$payloadManifestPath = Join-Path $buildRoot 'payload-manifest.json'
[ordered]@{
    schemaVersion = 1
    product = 'Galtek Classroom Client'
    productVersion = $productVersion
    wixToolsetVersion = $wixVersion
    gitCommit = $gitCommit
    gitDirty = $gitDirty
    buildTimestampUtc = $buildTimestampUtc
    credentialProviderPackageId = $credentialPackageId
    payloads = $payloadEntries
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $payloadManifestPath -Encoding UTF8

Write-Output 'GALTEK_INSTALLER_STAGE=STATIC_CONTRACT_TESTS'
& (Join-Path $PSScriptRoot 'test-client-installer-contract.ps1') -PayloadManifestPath $payloadManifestPath
& (Join-Path $PSScriptRoot 'test-client-installer-argument-transport.ps1')
& (Join-Path $PSScriptRoot 'test-client-legacy-adoption.ps1')
& (Join-Path $PSScriptRoot 'test-client-f3a-service-imagepath.ps1')
& (Join-Path $PSScriptRoot 'test-client-lifecycle-fixture.ps1')
& (Join-Path $PSScriptRoot 'test-client-registry-rollback.ps1')
& (Join-Path $PSScriptRoot 'test-client-same-version-payload.ps1') -PayloadManifestPath $payloadManifestPath
& (Join-Path $PSScriptRoot 'test-client-legacy-detection-bridge.ps1')
& (Join-Path $PSScriptRoot 'test-client-custom-ba-asset-contract.ps1')
& (Join-Path $PSScriptRoot 'test-client-f3c-service-contract.ps1')
& (Join-Path $PSScriptRoot 'test-client-f3c-rollback-final.ps1')
& (Join-Path $PSScriptRoot 'test-client-f3c-performance-contract.ps1')
& (Join-Path $PSScriptRoot 'test-client-f3d-service-registry.ps1')
& (Join-Path $PSScriptRoot 'test-client-f3d-rollback-real-state.ps1')
& (Join-Path $PSScriptRoot 'test-client-related-bundle-recovery.ps1')

Write-Output 'GALTEK_INSTALLER_STAGE=GENERATE_WIX_PAYLOAD'
& (Join-Path $setupRoot 'generate-payload-wxs.ps1') -PayloadRoot $payloadRoot -OutputPath $generatedWxs

$dotnet = Resolve-DotNet
$msiName = "GaltekClassroom-Client-$productVersion"
$bundleName = "GaltekClassroom-Client-Setup-$productVersion"
$msiProject = Join-Path $setupRoot 'ClientMsi.wixproj'
$bundleProject = Join-Path $setupRoot 'ClientBundle.wixproj'
$baProject = Join-Path $setupRoot 'BootstrapperApplication\GaltekClassroom.Bootstrapper.csproj'
$baTestProject = Join-Path $setupRoot 'BootstrapperApplication.Tests\GaltekClassroom.Bootstrapper.Tests.csproj'
$cleanupProject = Join-Path $setupRoot 'LegacyBundleRegistrationCleanup\GaltekClassroom.LegacyBundleCleanup.csproj'
$cleanupTestProject = Join-Path $setupRoot 'LegacyBundleRegistrationCleanup.Tests\GaltekClassroom.LegacyBundleCleanup.Tests.csproj'
$stateModuleSource = Get-Content -LiteralPath (Join-Path $setupRoot 'client-state.ps1') -Raw
$captureStateEncodedCommand = ConvertTo-CompressedEncodedCommand -Source ($stateModuleSource + "`r`n" + (Get-Content -LiteralPath (Join-Path $setupRoot 'capture-client-state.ps1') -Raw))
$rollbackStateEncodedCommand = ConvertTo-CompressedEncodedCommand -Source ($stateModuleSource + "`r`n" + (Get-Content -LiteralPath (Join-Path $setupRoot 'rollback-client-state.ps1') -Raw))
$commitStateEncodedCommand = ConvertTo-CompressedEncodedCommand -Source ($stateModuleSource + "`r`n" + (Get-Content -LiteralPath (Join-Path $setupRoot 'commit-client-state.ps1') -Raw))
$stateHelperGeneratedRoot = Join-Path $buildRoot 'state-helper-generated'
$stateHelperOutput = Join-Path $buildRoot 'state-helper'
$stateHelperObject = Join-Path $buildRoot 'state-helper-obj'
New-Item -ItemType Directory -Path $stateHelperGeneratedRoot, $stateHelperOutput, $stateHelperObject -Force | Out-Null
$stateHeader = @"
#pragma once
static const wchar_t kGaltekCaptureStateCommand[] =
$(ConvertTo-CppWideStringLiteral -Value $captureStateEncodedCommand);
static const wchar_t kGaltekRollbackStateCommand[] =
$(ConvertTo-CppWideStringLiteral -Value $rollbackStateEncodedCommand);
static const wchar_t kGaltekCommitStateCommand[] =
$(ConvertTo-CppWideStringLiteral -Value $commitStateEncodedCommand);
"@
[IO.File]::WriteAllText((Join-Path $stateHelperGeneratedRoot 'GeneratedStateCommands.h'), $stateHeader, [Text.UTF8Encoding]::new($false))

Write-Output 'GALTEK_INSTALLER_STAGE=BUILD_STATE_HELPER'
$msbuild = Resolve-MSBuild
& $msbuild (Join-Path $setupRoot 'ClientStateHelper.vcxproj') /m /p:Configuration=Release /p:Platform=x64 /p:ResolveNuGetPackages=false `
    "/p:GeneratedStateHelperRoot=$stateHelperGeneratedRoot" "/p:OutDir=$stateHelperOutput/" "/p:IntDir=$stateHelperObject/"
if ($LASTEXITCODE -ne 0) { throw "State helper build failed with exit code $LASTEXITCODE." }
$stateHelperPath = Join-Path $stateHelperOutput 'ClientStateHelper.dll'
if (-not (Test-Path -LiteralPath $stateHelperPath -PathType Leaf)) { throw "State helper was not produced: $stateHelperPath" }

Write-Output 'GALTEK_INSTALLER_STAGE=BUILD_CUSTOM_BA'
& $dotnet build $baProject -c Release -f net48 -r win-x64 -o $baOutput `
    "-p:ProductVersion=$productVersion" `
    "-p:WixToolsetVersion=$wixVersion"
if ($LASTEXITCODE -ne 0) { throw "Custom BA build failed with exit code $LASTEXITCODE." }
$baPath = Join-Path $baOutput 'GaltekClassroom.Bootstrapper.exe'
foreach ($baFile in @($baPath, (Join-Path $baOutput 'GaltekClassroom.Bootstrapper.exe.config'), (Join-Path $baOutput 'WixToolset.BootstrapperApplicationApi.dll'), (Join-Path $baOutput 'mbanative.dll'))) {
    if (-not (Test-Path -LiteralPath $baFile -PathType Leaf)) { throw "Custom BA output is missing: $baFile" }
}

Write-Output 'GALTEK_INSTALLER_STAGE=TEST_CUSTOM_BA'
& $dotnet build $baTestProject -c Release -f net48 -o $baTestOutput
if ($LASTEXITCODE -ne 0) { throw "Custom BA tests build failed with exit code $LASTEXITCODE." }
& (Join-Path $baTestOutput 'GaltekClassroom.Bootstrapper.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw "Custom BA domain tests failed with exit code $LASTEXITCODE." }

Write-Output 'GALTEK_INSTALLER_STAGE=BUILD_LEGACY_CLEANUP_HELPER'
& $dotnet build $cleanupProject -c Release -f net48 -o $cleanupOutput `
    "-p:ProductVersion=$productVersion"
if ($LASTEXITCODE -ne 0) { throw "Legacy cleanup helper build failed with exit code $LASTEXITCODE." }
$cleanupPath = Join-Path $cleanupOutput 'GaltekClassroom.LegacyBundleCleanup.exe'
if (-not (Test-Path -LiteralPath $cleanupPath -PathType Leaf)) { throw "Legacy cleanup helper output is missing: $cleanupPath" }
& $dotnet build $cleanupTestProject -c Release -f net48 -o $cleanupTestOutput
if ($LASTEXITCODE -ne 0) { throw "Legacy cleanup tests build failed with exit code $LASTEXITCODE." }
& (Join-Path $cleanupTestOutput 'GaltekClassroom.LegacyBundleCleanup.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw "Legacy cleanup tests failed with exit code $LASTEXITCODE." }

Write-Output "GALTEK_INSTALLER_STAGE=BUILD_MSI wix=$wixVersion"
& $dotnet build $msiProject -c Release -o $msiOutput `
    "-p:OutputName=$msiName" `
    "-p:PayloadRoot=$payloadRoot" `
    "-p:BrandingRoot=$(Join-Path $themeRoot 'assets')" `
    "-p:GeneratedPayloadWxs=$generatedWxs" `
    "-p:CredentialPackageId=$credentialPackageId" `
    "-p:GitCommit=$gitCommit" `
    "-p:BuildTimestampUtc=$buildTimestampUtc" `
    "-p:ClientStateHelperPath=$stateHelperPath"
if ($LASTEXITCODE -ne 0) { throw "MSI build failed with exit code $LASTEXITCODE." }

$msiPath = Join-Path $msiOutput "$msiName.msi"
if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) { throw "Expected MSI was not produced: $msiPath" }

Write-Output "GALTEK_INSTALLER_STAGE=VALIDATE_MSI wix=$wixVersion"
$wixDll = Join-Path $env:USERPROFILE ".nuget\packages\wixtoolset.sdk\$wixVersion\tools\net6.0\wix.dll"
if (-not (Test-Path -LiteralPath $wixDll -PathType Leaf)) { throw "Pinned WiX validator was not restored: $wixDll" }
& $dotnet $wixDll msi validate $msiPath
if ($LASTEXITCODE -ne 0) { throw "MSI ICE validation failed with exit code $LASTEXITCODE." }
& (Join-Path $PSScriptRoot 'test-client-same-version-payload.ps1') -PayloadManifestPath $payloadManifestPath -MsiPath $msiPath

$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$summaryInformation = $null
try {
    $summaryInformation = $windowsInstaller.SummaryInformation($msiPath, 0)
    $packageCode = ([string]$summaryInformation.Property(9)).ToUpperInvariant()
}
finally {
    if ($null -ne $summaryInformation -and [Runtime.InteropServices.Marshal]::IsComObject($summaryInformation)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summaryInformation) }
    if ($null -ne $windowsInstaller -and [Runtime.InteropServices.Marshal]::IsComObject($windowsInstaller)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($windowsInstaller) }
}
$parsedPackageCode = [Guid]::Empty
if (-not [Guid]::TryParse($packageCode, [ref]$parsedPackageCode)) { throw "Final MSI PackageCode is invalid: $packageCode" }
$packageCode = $parsedPackageCode.ToString('B').ToUpperInvariant()
Write-Output "GALTEK_FINAL_PACKAGE_CODE=$packageCode"

Write-Output "GALTEK_INSTALLER_STAGE=BUILD_BUNDLE wix=$wixVersion"
& $dotnet build $bundleProject -c Release -o $bundleOutput `
    "-p:OutputName=$bundleName" `
    "-p:MsiSourcePath=$msiPath" `
    "-p:BrandingRoot=$(Join-Path $themeRoot 'assets')" `
    "-p:BaRoot=$baOutput" `
    "-p:ClientStateHelperPath=$stateHelperPath" `
    "-p:LegacyCleanupHelperPath=$cleanupPath"
if ($LASTEXITCODE -ne 0) { throw "Bundle build failed with exit code $LASTEXITCODE." }

$builtBundle = Join-Path $bundleOutput "$bundleName.exe"
if (-not (Test-Path -LiteralPath $builtBundle -PathType Leaf)) { throw "Expected bundle was not produced: $builtBundle" }

$finalBundle = Join-Path $artifactRoot "$bundleName.exe"
$finalSha = Join-Path $artifactRoot "$bundleName.exe.sha256"
$finalManifest = Join-Path $artifactRoot "$bundleName.manifest.json"
Copy-Item -LiteralPath $builtBundle -Destination $finalBundle -Force
$bundleHash = (Get-FileHash -LiteralPath $finalBundle -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $finalSha -Value "$bundleHash  $bundleName.exe" -Encoding ASCII

$extractedBa = Join-Path $buildRoot 'extracted-ba'
Write-Output 'GALTEK_INSTALLER_STAGE=VALIDATE_EMBEDDED_BRANDING'
& $dotnet $wixDll burn extract $finalBundle -outba $extractedBa -out (Join-Path $buildRoot 'extracted-bundle')
if ($LASTEXITCODE -ne 0) { throw "Bundle extraction failed with exit code $LASTEXITCODE." }

[xml]$baData = Get-Content -LiteralPath (Join-Path $extractedBa 'BootstrapperApplicationData.xml') -Raw
$bundleProperties = $baData.SelectSingleNode("/*[local-name()='BootstrapperApplicationData']/*[local-name()='WixBundleProperties']")
$bundleId = [string]$bundleProperties.Id
$parsedBundleId = [Guid]::Empty
if (-not [Guid]::TryParse($bundleId, [ref]$parsedBundleId)) { throw "Final generated BundleId is invalid: $bundleId" }
$bundleId = $parsedBundleId.ToString('B').ToUpperInvariant()

[xml]$burnManifest = Get-Content -LiteralPath (Join-Path $extractedBa 'manifest.xml') -Raw
$registration = $burnManifest.SelectSingleNode("/*[local-name()='BurnManifest']/*[local-name()='Registration']")
if ([string]$registration.Id -ne $bundleId) { throw 'Bundle identity differs between BA data and Burn registration.' }
if ([string]$registration.Version -ne $productVersion) { throw 'Burn registration ProductVersion mismatch.' }
if ([string]$registration.ProviderKey -ne [string]$versionProps.Project.PropertyGroup.BundleProviderKey) { throw 'Burn registration ProviderKey mismatch.' }
Write-Output "GALTEK_FINAL_BUNDLE_ID=$bundleId"

[ordered]@{
    schemaVersion = 1
    product = 'Galtek Classroom Client'
    productVersion = $productVersion
    fileName = "$bundleName.exe"
    sha256 = $bundleHash
    size = (Get-Item -LiteralPath $finalBundle).Length
    wixToolsetVersion = $wixVersion
    productUpgradeCode = [string]$versionProps.Project.PropertyGroup.ProductUpgradeCode
    bundleUpgradeCode = [string]$versionProps.Project.PropertyGroup.BundleUpgradeCode
    bundleId = $bundleId
    productCode = [string]$versionProps.Project.PropertyGroup.ProductCode
    packageCode = $packageCode
    bundleProviderKey = [string]$versionProps.Project.PropertyGroup.BundleProviderKey
    gitCommit = $gitCommit
    gitDirty = $gitDirty
    buildTimestampUtc = $buildTimestampUtc
    credentialProviderPackageId = $credentialPackageId
    customBootstrapperApplication = [ordered]@{
        targetFramework = 'net48'
        executableSize = (Get-Item -LiteralPath $baPath).Length
        packagedPayloadSize = (@($baPath, (Join-Path $baOutput 'GaltekClassroom.Bootstrapper.exe.config'), (Join-Path $baOutput 'WixToolset.BootstrapperApplicationApi.dll'), (Join-Path $baOutput 'mbanative.dll')) | ForEach-Object { (Get-Item -LiteralPath $_).Length } | Measure-Object -Sum).Sum
    }
    legacyBundleCleanup = [ordered]@{
        executableSize = (Get-Item -LiteralPath $cleanupPath).Length
        mode = 'exact-historical-graph-migration-fail-closed'
    }
    releaseFreeze = [ordered]@{
        policy = 'Do not rebuild 0.0.4 after physical testing starts; any source change advances to 0.0.5.'
        productVersion = $productVersion
        productCode = [string]$versionProps.Project.PropertyGroup.ProductCode
        packageCode = $packageCode
        bundleId = $bundleId
        sha256 = $bundleHash
    }
    payloads = $payloadEntries | ForEach-Object {
        [ordered]@{ relativePath = $_.relativePath; size = $_.size; sha256 = $_.sha256 }
    }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $finalManifest -Encoding UTF8

& (Join-Path $PSScriptRoot 'test-client-installer-contract.ps1') -PayloadManifestPath $payloadManifestPath -BundlePath $finalBundle -ExtractedBootstrapperPath $extractedBa

Write-Output 'GALTEK_INSTALLER_STAGE=COMPLETE'
Write-Output "Artifact: $finalBundle"
Write-Output "SHA256: $bundleHash"
Write-Output "Manifest: $finalManifest"
