#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $PayloadManifestPath,
    [string] $BundlePath,
    [string] $ExtractedBootstrapperPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$setupRoot = Join-Path $PSScriptRoot 'setup'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "INSTALLER_CONTRACT_FAILED: $Message" }
}
function Assert-Contains([string] $Text, [string] $Expected, [string] $Message) {
    Assert-Contract ($Text.Contains($Expected)) $Message
}

$versionPropsPath = Join-Path $setupRoot 'InstallerVersion.props'
$packagePath = Join-Path $setupRoot 'Package.wxs'
$bundlePathSource = Join-Path $setupRoot 'Bundle.wxs'
$bundleProjectPath = Join-Path $setupRoot 'ClientBundle.wixproj'
$msiProjectPath = Join-Path $setupRoot 'ClientMsi.wixproj'
$lifecyclePath = Join-Path $setupRoot 'client-lifecycle.ps1'
$legacyPath = Join-Path $setupRoot 'legacy-adoption.ps1'
$statePath = Join-Path $setupRoot 'client-state.ps1'
$detectionPath = Join-Path $setupRoot 'client-detection.ps1'
$helperPath = Join-Path $setupRoot 'ClientStateHelper.cpp'
$helperProjectPath = Join-Path $setupRoot 'ClientStateHelper.vcxproj'
$baRoot = Join-Path $setupRoot 'BootstrapperApplication'
$baProjectPath = Join-Path $baRoot 'GaltekClassroom.Bootstrapper.csproj'
$baSourcePath = Join-Path $baRoot 'GaltekBootstrapperApplication.cs'
$applyOwnerPath = Join-Path $baRoot 'ApplyOwnerWindow.cs'
$legacyDetectorPath = Join-Path $baRoot 'LegacyDetector.cs'
$viewModelPath = Join-Path $baRoot 'InstallerViewModel.cs'
$presentationPath = Join-Path $baRoot 'Presentation.cs'
$stateResolverPath = Join-Path $baRoot 'ProductState.cs'
$xamlPath = Join-Path $baRoot 'MainWindow.xaml'
$buildPath = Join-Path $PSScriptRoot 'build-client-installer.ps1'
$logoPath = Join-Path $repositoryRoot 'logo.png'
$logoContractPath = Join-Path $setupRoot 'theme\official-logo.sha256'

[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$productVersion = [string]$versionProps.Project.PropertyGroup.ProductVersion
$wixVersion = [string]$versionProps.Project.PropertyGroup.WixToolsetVersion
Assert-Contract ($productVersion -eq '0.0.4') 'ProductVersion must be the definitive 0.0.4 bridge release candidate.'
Assert-Contract ($wixVersion -eq '5.0.2') 'WiX must remain pinned to 5.0.2.'
Assert-Contract ([Guid]::TryParse([string]$versionProps.Project.PropertyGroup.ProductUpgradeCode, [ref]([Guid]::Empty))) 'Product UpgradeCode is invalid.'
Assert-Contract ([Guid]::TryParse([string]$versionProps.Project.PropertyGroup.BundleUpgradeCode, [ref]([Guid]::Empty))) 'Bundle UpgradeCode is invalid.'
Assert-Contract ([string]$versionProps.Project.PropertyGroup.ProductCode -eq '{A544ED43-3AAA-4B47-8EE7-0A3807C94D59}') '0.0.4 ProductCode changed or is not pinned.'
Assert-Contract ([string]$versionProps.Project.PropertyGroup.ProductCode -ne '{2BA4D6A0-9492-484E-A27B-9EAE40C886A9}') '0.0.3 ProductCode was reused.'
Assert-Contract ([string]$versionProps.Project.PropertyGroup.ProductCode -ne '{DCF26CE0-A601-489B-B32A-E14095CC50C0}') '0.0.2 ProductCode was reused.'
Assert-Contract ([string]$versionProps.Project.PropertyGroup.BundleProviderKey -eq 'GaltekSolution.GaltekClassroom.Client.Bundle.0.0.4') '0.0.4 ProviderKey is not exact.'

$expectedLogoHash = ((Get-Content -LiteralPath $logoContractPath -Raw) -split '\s+')[0].ToLowerInvariant()
Assert-Contract (Test-Path -LiteralPath $logoPath -PathType Leaf) 'Official logo.png is missing.'
Assert-Contract ((Get-FileHash -LiteralPath $logoPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expectedLogoHash) 'Official logo.png hash changed.'
Assert-Contract ($expectedLogoHash -eq 'fd004063ddfa305c3703190cef786d1b5b9e61dbdcd7b29419db1cb10775869e') 'Unexpected official logo contract hash.'
foreach ($asset in @('galtek-classroom-icon.png', 'galtek-classroom.ico')) {
    Assert-Contract (Test-Path -LiteralPath (Join-Path $setupRoot "theme\assets\$asset") -PathType Leaf) "Required BA branding asset is missing: $asset"
}

$package = Get-Content -LiteralPath $packagePath -Raw
$bundle = Get-Content -LiteralPath $bundlePathSource -Raw
$bundleProject = Get-Content -LiteralPath $bundleProjectPath -Raw
$msiProject = Get-Content -LiteralPath $msiProjectPath -Raw
$lifecycle = Get-Content -LiteralPath $lifecyclePath -Raw
$legacy = Get-Content -LiteralPath $legacyPath -Raw
$state = Get-Content -LiteralPath $statePath -Raw
$detection = Get-Content -LiteralPath $detectionPath -Raw
$helper = Get-Content -LiteralPath $helperPath -Raw
$baProject = Get-Content -LiteralPath $baProjectPath -Raw
$baSource = (Get-Content -LiteralPath $baSourcePath -Raw) + "`n" + (Get-Content -LiteralPath $applyOwnerPath -Raw) + "`n" + (Get-Content -LiteralPath (Join-Path $baRoot 'Program.cs') -Raw)
$legacyDetector = Get-Content -LiteralPath $legacyDetectorPath -Raw
$viewModel = Get-Content -LiteralPath $viewModelPath -Raw
$presentation = Get-Content -LiteralPath $presentationPath -Raw
$stateResolver = Get-Content -LiteralPath $stateResolverPath -Raw
$xaml = Get-Content -LiteralPath $xamlPath -Raw
$build = Get-Content -LiteralPath $buildPath -Raw

# MSI lifecycle authority and F1/F2/F3A regression contracts.
Assert-Contains $package '<MajorUpgrade' 'MSI MajorUpgrade is missing.'
Assert-Contains $package 'AllowSameVersionUpgrades="no"' 'Same-version replacement policy changed.'
Assert-Contains $package '<SetProperty Id="REINSTALLMODE" Value="emus"' 'REINSTALLMODE=emus is missing.'
Assert-Contract (([regex]::Matches($package, '\[INSTALLFOLDER\]\.')).Count -ge 5) 'F1 InstallDirectory transport must cover configure/maintenance/rollback/uninstall/commit.'
Assert-Contains $package 'NeverOverwrite="yes"' 'appsettings NeverOverwrite changed.'
Assert-Contains $package 'Permanent="yes"' 'appsettings Permanent changed.'
Assert-Contains $package 'Binary Id="ClientStateHelper"' 'Embedded lifecycle helper is missing.'
foreach ($entry in @('CaptureClientState', 'RollbackClientStateFinal', 'CommitClientStateFinal')) { Assert-Contains $package "DllEntry=`"$entry`"" "Lifecycle helper entry point is missing: $entry" }
Assert-Contains $helper 'MsiProcessMessage' 'Lifecycle helper must forward child diagnostics into the MSI log.'
Assert-Contains $helper 'CreatePipe' 'Lifecycle helper must capture PowerShell stdout/stderr.'
Assert-Contains $lifecycle "'restart/5000/restart/15000/restart/60000'" 'Service recovery delays changed.'
Assert-Contains $lifecycle '$RecoveryResetSeconds = 86400' 'Service recovery reset changed.'
Assert-Contains $lifecycle "'S-1-5-32-545'" 'Session principal SID changed.'
Assert-Contains $lifecycle '-RunLevel Limited' 'Session RunLevel changed.'
Assert-Contains $lifecycle '-MultipleInstances Parallel' 'Session parallel policy changed.'
Assert-Contains $lifecycle 'Get-CapturedLegacyState' 'F3A snapshot reuse is missing.'
Assert-Contract (-not $lifecycle.Contains('Get-CimInstance')) 'F3A must not reintroduce CIM.'
Assert-Contract (-not $lifecycle.Contains('Get-WmiObject')) 'F3A must not reintroduce WMI.'
Assert-Contains $legacy 'Test-GaltekLegacyServiceImagePath' 'F3A Service ImagePath parser is missing.'
Assert-Contains $legacy 'argument-free value' 'F3A strict unquoted fallback documentation is missing.'
Assert-Contains $state 'RegistryView]::Registry64' 'Registry64 snapshot authority changed.'
Assert-Contains $package '{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}' 'Credential Provider CLSID changed.'
Assert-Contains $package 'Apartment' 'Credential Provider threading model changed.'
Assert-Contract (-not $package.Contains('Credential Provider Filters')) 'Credential Provider Filter must remain absent.'
Assert-Contract (-not $lifecycle.Contains('Stop-Process -Name LogonUI')) 'Setup must not terminate LogonUI.'
Assert-Contract (-not $lifecycle.Contains('Stop-Process -Name winlogon')) 'Setup must not terminate Winlogon.'

# F3B Custom BA authority.
Assert-Contains $bundle '<BootstrapperApplication' 'Custom BootstrapperApplication authoring is missing.'
Assert-Contains $bundle 'GaltekClassroom.Bootstrapper.exe' 'Bundle does not point to the Custom BA executable.'
Assert-Contains $bundle 'WixToolset.BootstrapperApplicationApi.dll' 'Managed Burn API payload is missing.'
Assert-Contains $bundle 'mbanative.dll' 'Managed BA native bridge payload is missing.'
Assert-Contract (-not $bundle.Contains('ClientStateHelper.exe')) 'PowerShell-backed helper must not be a BA detection payload.'
Assert-Contract (-not $bundle.Contains('WixStandardBootstrapperApplication')) 'WixStdBA must not remain runtime authority.'
Assert-Contract (-not $bundle.Contains('ThemeFile=')) 'ThmUtil theme must not be packaged by the bundle.'
Assert-Contract (-not $bundleProject.Contains('WixToolset.Bal.wixext')) 'The bundle project must not depend on WixStdBA/Bal extension.'
Assert-Contains $baProject '<TargetFramework>net48</TargetFramework>' 'Custom BA must target .NET Framework 4.8.'
Assert-Contains $baProject 'WixToolset.BootstrapperApplicationApi" Version="5.0.2"' 'Custom BA API must be pinned to WiX 5.0.2.'
Assert-Contains $baProject '<UseWPF>true</UseWPF>' 'Custom BA must use WPF.'
Assert-Contains $baProject '<PlatformTarget>x64</PlatformTarget>' 'Custom BA must be x64.'
Assert-Contains $baSource 'ManagedBootstrapperApplication' 'WiX 5 out-of-process managed host entry point is missing.'
Assert-Contains $baSource 'DetectRelatedBundle' 'Related bundle detection is missing.'
Assert-Contains $baSource 'DetectPackageComplete' 'MSI package detection is missing.'
Assert-Contains $baSource 'PRODUCT_STATE_RESOLVED' 'Product-state logging is missing.'
Assert-Contains $baSource 'BundleIdentity.ReadCurrentBundleId' 'BA does not load the generated BundleId authority.'
Assert-Contains $baSource 'GaltekCurrentBundleId' 'BA does not pass the generated BundleId to cleanup.'
Assert-Contains $baSource 'ApplyOwnerWindow.Create()' 'Headless Apply does not create a real native owner HWND.'
Assert-Contains $baSource 'WsExToolWindow | WsExNoActivate' 'Headless Apply owner does not enforce hidden tool/no-activate styles.'
Assert-Contains $baSource 'DestroyWindow' 'Headless Apply owner lifetime is not disposed deterministically.'
Assert-Contract (-not $baSource.Contains('Engine.Apply(IntPtr.Zero)')) 'Engine.Apply must never receive a null HWND.'
foreach ($eventName in @('DETECT_BEGIN', 'DETECT_COMPLETE', 'PLAN_BEGIN', 'PLAN_COMPLETE', 'APPLY_BEGIN', 'APPLY_COMPLETE', 'BA_SHUTDOWN')) { Assert-Contains $baSource $eventName "BA log event is missing: $eventName" }
foreach ($stateName in @('Fresh', 'LegacySupported', 'InstalledSame', 'InstalledOlder', 'InstalledNewer', 'RepairablePartial', 'BlockedConflict', 'DetectionFailed', 'RestartRequired')) { Assert-Contains $stateResolver $stateName "Product state is missing: $stateName" }
foreach ($copy in @('Instalar Galtek Classroom', 'Actualizar Galtek Classroom', 'Reparar instalación', 'Desinstalar', 'No pudimos completar la instalación', 'Abrir registro', 'Se necesita reiniciar Windows', 'Más tarde')) { Assert-Contract (($presentation + $xaml).Contains($copy)) "Spanish BA copy is missing: $copy" }
Assert-Contains $viewModel 'DispatcherTimer' 'Elapsed timer is missing.'
Assert-Contains $viewModel 'TimeSpan.FromSeconds(1)' 'Elapsed timer must update at most once per second.'
Assert-Contains $xaml 'AutomationProperties.Name' 'Accessibility names are missing.'
Assert-Contains $xaml 'IsDefault="True"' 'Keyboard default action is missing.'
Assert-Contains $xaml 'Width="720" Height="520"' 'Legacy-PC window size changed.'
Assert-Contains $xaml '#456FE8' 'Classroom primary color is missing.'
Assert-Contains $xaml '#104C75' 'Corporate Galtek color is missing.'
Assert-Contains $viewModel '#FFA60C' 'Galtek yellow accent is missing.'
Assert-Contains $viewModel '#C83C3C' 'Failure semantic red accent is missing.'
Assert-Contains $legacyDetector 'RegistryView.Registry64' 'BA detection must use direct Registry64 reads.'
Assert-Contains $legacyDetector 'Schedule.Service' 'BA detection must read the Session task directly.'
Assert-Contract (-not $baSource.Contains('ClientStateHelper.exe')) 'BA detection must not launch the PowerShell helper.'
Assert-Contract (($legacyDetector + $baSource) -notmatch '(?i)Get-CimInstance|Get-WmiObject') 'BA detection must not use CIM/WMI.'
Assert-Contract (($baProject + $baSource + $xaml) -notmatch '(?i)webview|electron|react|tauri|avalonia|maui|chromium') 'Forbidden heavy UI runtime referenced.'

# Build pipeline.
foreach ($token in @('BUILD_STATE_HELPER', 'BUILD_CUSTOM_BA', 'TEST_CUSTOM_BA', 'BUILD_MSI', 'VALIDATE_MSI', 'BUILD_BUNDLE')) { Assert-Contains $build $token "Build stage is missing: $token" }
Assert-Contract (-not $build.Contains('$detectStateEncodedCommand')) 'Build must not embed PowerShell BA detection.'
Assert-Contains $build 'test-client-f3a-service-imagepath.ps1' 'F3A regression test is not in the build.'
Assert-Contains $build 'test-client-f3d-service-registry.ps1' 'F3D Service Registry regression test is not in the build.'
Assert-Contains $build 'test-client-f3d-rollback-real-state.ps1' 'F3D rollback real-state regression test is not in the build.'
Assert-Contains $build 'test-client-same-version-payload.ps1' 'Same-FileVersion regression test is not in the build.'
Assert-Contains $bundleProject 'BaRoot=$(BaRoot)' 'Bundle project does not consume Custom BA output.'
Assert-Contains $msiProject 'ClientStateHelperPath=$(ClientStateHelperPath)' 'MSI helper input changed.'
Assert-Contract (Test-Path -LiteralPath $helperProjectPath -PathType Leaf) 'C++ helper project is missing.'

if (-not [string]::IsNullOrWhiteSpace($PayloadManifestPath)) {
    $manifest = Get-Content -LiteralPath $PayloadManifestPath -Raw | ConvertFrom-Json
    Assert-Contract ([string]$manifest.productVersion -eq $productVersion) 'Payload manifest ProductVersion mismatch.'
    foreach ($payload in @($manifest.payloads)) {
        Assert-Contract (Test-Path -LiteralPath $payload.sourcePath -PathType Leaf) "Payload missing: $($payload.relativePath)"
        Assert-Contract ((Get-FileHash -LiteralPath $payload.sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -eq [string]$payload.sha256) "Payload hash mismatch: $($payload.relativePath)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($BundlePath)) {
    Assert-Contract (Test-Path -LiteralPath $BundlePath -PathType Leaf) 'Bundle artifact is missing.'
    Assert-Contract ([IO.Path]::GetFileName($BundlePath) -eq "GaltekClassroom-Client-Setup-$productVersion.exe") 'Bundle filename changed.'
}

if (-not [string]::IsNullOrWhiteSpace($ExtractedBootstrapperPath)) {
    foreach ($name in @('GaltekClassroom.Bootstrapper.exe', 'GaltekClassroom.Bootstrapper.exe.config', 'WixToolset.BootstrapperApplicationApi.dll', 'mbanative.dll', 'BootstrapperApplicationData.xml', 'manifest.xml')) {
        Assert-Contract (Test-Path -LiteralPath (Join-Path $ExtractedBootstrapperPath $name) -PathType Leaf) "Extracted Custom BA payload is missing: $name"
    }
    Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $ExtractedBootstrapperPath 'wixstdba.exe'))) 'WixStdBA was unexpectedly packaged.'
    Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $ExtractedBootstrapperPath 'assets\window-background.png'))) 'Obsolete ThmUtil assets were unexpectedly packaged.'
    Assert-Contains (Get-Content -LiteralPath (Join-Path $ExtractedBootstrapperPath 'manifest.xml') -Raw) 'FilePath="GaltekClassroom.Bootstrapper.exe"' 'Burn manifest does not reference the Custom BA.'
    [xml]$baData = Get-Content -LiteralPath (Join-Path $ExtractedBootstrapperPath 'BootstrapperApplicationData.xml') -Raw
    $bundleProperties = $baData.SelectSingleNode("/*[local-name()='BootstrapperApplicationData']/*[local-name()='WixBundleProperties']")
    $generatedId = [Guid]::Empty
    Assert-Contract ([Guid]::TryParse([string]$bundleProperties.Id, [ref]$generatedId)) 'Final generated BundleId is invalid.'
}

$scripts = @(
    $lifecyclePath, $legacyPath, $statePath, $detectionPath, $buildPath,
    (Join-Path $setupRoot 'capture-client-state.ps1'), (Join-Path $setupRoot 'rollback-client-state.ps1'),
    (Join-Path $setupRoot 'commit-client-state.ps1'), (Join-Path $setupRoot 'generate-brand-assets.ps1'),
    (Join-Path $setupRoot 'generate-payload-wxs.ps1'), (Join-Path $PSScriptRoot 'test-client-bundle-ui-smoke.ps1'),
    (Join-Path $PSScriptRoot 'test-client-legacy-detection-bridge.ps1'),
    (Join-Path $PSScriptRoot 'test-client-f3c-service-contract.ps1'),
    (Join-Path $PSScriptRoot 'test-client-f3c-rollback-final.ps1'),
    (Join-Path $PSScriptRoot 'test-client-f3c-performance-contract.ps1'),
    (Join-Path $PSScriptRoot 'test-client-f3d-service-registry.ps1'),
    (Join-Path $PSScriptRoot 'test-client-f3d-rollback-real-state.ps1'),
    (Join-Path $PSScriptRoot 'test-client-custom-ba-asset-contract.ps1'))
$parserErrors = @()
foreach ($script in $scripts) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$errors) | Out-Null
    $parserErrors += @($errors)
}
Assert-Contract ($parserErrors.Count -eq 0) "PowerShell parser errors: $($parserErrors -join '; ')"

Write-Output 'Client installer contract tests passed.'
