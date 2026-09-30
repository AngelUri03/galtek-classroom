#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-Recovery([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "RELATED_BUNDLE_RECOVERY_CONTRACT_FAILED: $Message" }
}

$setup = Join-Path $PSScriptRoot 'setup'
$ba = Get-Content -LiteralPath (Join-Path $setup 'BootstrapperApplication\GaltekBootstrapperApplication.cs') -Raw
$execution = Get-Content -LiteralPath (Join-Path $setup 'BootstrapperApplication\BootstrapperExecution.cs') -Raw
$applyOwner = Get-Content -LiteralPath (Join-Path $setup 'BootstrapperApplication\ApplyOwnerWindow.cs') -Raw
$bundle = Get-Content -LiteralPath (Join-Path $setup 'Bundle.wxs') -Raw
$package = Get-Content -LiteralPath (Join-Path $setup 'Package.wxs') -Raw
$props = Get-Content -LiteralPath (Join-Path $setup 'InstallerVersion.props') -Raw
$cleanup = Get-Content -LiteralPath (Join-Path $setup 'LegacyBundleRegistrationCleanup\WindowsCleanupEnvironment.cs') -Raw
$cleanupDomain = Get-Content -LiteralPath (Join-Path $setup 'LegacyBundleRegistrationCleanup\CleanupDomain.cs') -Raw
$cleanupProgram = Get-Content -LiteralPath (Join-Path $setup 'LegacyBundleRegistrationCleanup\Program.cs') -Raw
$cleanupLog = Get-Content -LiteralPath (Join-Path $setup 'LegacyBundleRegistrationCleanup\CleanupLog.cs') -Raw

Assert-Recovery ($props.Contains('<ProductVersion>0.0.6</ProductVersion>')) 'ProductVersion is not 0.0.6.'
Assert-Recovery ($props.Contains('<ProductCode>{C9698974-44A1-4751-A914-D70152A28B5B}</ProductCode>')) 'The 0.0.6 MSI ProductCode is not pinned.'
Assert-Recovery (-not $props.Contains('<ProductCode>{078A1C16-27EB-4031-96C8-342EC1CA47EB}</ProductCode>')) 'The 0.0.5 MSI ProductCode was reused.'
Assert-Recovery (-not $props.Contains('<ProductCode>{A544ED43-3AAA-4B47-8EE7-0A3807C94D59}</ProductCode>')) 'The 0.0.4 MSI ProductCode was reused.'
Assert-Recovery (-not $props.Contains('<ProductCode>{2BA4D6A0-9492-484E-A27B-9EAE40C886A9}</ProductCode>')) 'The 0.0.3 MSI ProductCode was reused.'
Assert-Recovery (-not $props.Contains('<ProductCode>{DCF26CE0-A601-489B-B32A-E14095CC50C0}</ProductCode>')) 'The 0.0.2 MSI ProductCode was reused.'
Assert-Recovery ($props.Contains('{2D9C681B-F7A8-4C5F-97C8-C5EACB2F31D6}')) 'MSI UpgradeCode changed.'
Assert-Recovery ($props.Contains('{A67E1418-0AD4-4DA1-915A-1098091D23E7}')) 'Bundle UpgradeCode changed.'
Assert-Recovery ($props.Contains('<BundleProviderKey>GaltekSolution.GaltekClassroom.Client.Bundle.0.0.6</BundleProviderKey>')) 'The 0.0.6 ProviderKey is not stable.'
Assert-Recovery (-not $bundle.Contains('<Bundle Id=')) 'Bundle/@Id is unsupported in WiX 5.0.2 and must not be authored.'
Assert-Recovery ($ba.Contains('this.command.Action') -and $ba.Contains('this.command.Display') -and $ba.Contains('this.command.Relation')) 'BA does not consume the typed WiX command contract.'
Assert-Recovery ($ba.Contains('this.Engine.Detect()') -and $ba.Contains('this.Engine.Plan(action)') -and $ba.Contains('this.Engine.Apply(')) 'Headless Detect/Plan/Apply lifecycle is incomplete.'
Assert-Recovery ($ba.Contains('GetApplyOwnerHandle()') -and $applyOwner.Contains('CreateWindowEx') -and $applyOwner.Contains('DestroyWindow')) 'Headless Apply HWND lifetime is incomplete.'
Assert-Recovery (-not $ba.Contains('Engine.Apply(this.viewModel == null ? IntPtr.Zero')) 'Headless Apply still passes a null HWND.'
Assert-Recovery ($ba.Contains('e.Cancel = e.Cancel || IsCancelRequested')) 'BA cancellation does not preserve the recommendation supplied by Burn.'
Assert-Recovery ($ba.Contains('PlanRelatedBundleType') -and $ba.Contains('RelatedBundlePlanType.None')) 'Broken related bundles are not suppressed at PlanRelatedBundleType.'
foreach ($legacyId in @('{2DCDF1F3-FBDD-42D6-8879-EA33126CA34C}', '{43F6BCC5-2BEE-4CCD-9B51-1A4BE0548D85}', '{C18369A6-9FC2-4360-9451-D77A44A7477C}', '{A2742DDC-6F6B-46F3-A42C-FF1FDFB8BD96}')) {
    Assert-Recovery ($execution.Contains($legacyId)) "Physical historical BundleId is not allowlisted: $legacyId"
}
Assert-Recovery (-not $execution.Contains('{C44D76A6-0183-4179-B45E-9123A4584B29}')) 'Healthy 0.0.4 BundleId must use normal Burn upgrade handling.'
Assert-Recovery (-not $execution.Contains('{ED86C4F6-C510-4FB7-B7BF-0359BB1B2898}')) 'Healthy 0.0.5 BundleId must use normal Burn upgrade handling.'
Assert-Recovery ($execution.Contains('LEGACY_BROKEN_INTERACTIVE_BA') -and $execution.Contains('LEGACY_EMBEDDED_APPLY_HWND_BUG')) 'Typed suppression reasons are missing.'
Assert-Recovery ($bundle.Contains('Id="LegacyBundleRegistrationCleanup"') -and $bundle.Contains('Permanent="yes"') -and $bundle.Contains('Vital="no"')) 'Scoped non-vital cleanup helper is not chained.'
Assert-Recovery ($bundle.Contains('InstallCondition="GaltekNeedsPredecessorCleanup = 1"')) 'Cleanup helper is not gated by detected maintenance state.'
Assert-Recovery ($bundle.Contains('GaltekCleanupLogPath') -and $bundle.Contains('--log &quot;[GaltekCleanupLogPath]&quot;')) 'Durable cleanup log path is not passed to the helper.'
Assert-Recovery ($bundle.Contains('--current-bundle-id &quot;[GaltekCurrentBundleId]&quot;')) 'Generated 0.0.6 BundleId is not passed to cleanup.'
foreach ($forbidden in @('taskkill', 'Stop-Process', 'SendKeys', 'UIAutomation', '/unsafeuninstall', '-burn.ignoredependencies')) {
    Assert-Recovery (-not ($ba + $execution + $cleanup).Contains($forbidden)) "Forbidden workaround found: $forbidden"
}
Assert-Recovery ($cleanup.Contains('RegistryView.Registry64')) 'Cleanup does not use typed Registry64 access.'
Assert-Recovery (-not $cleanup.Contains('reg.exe') -and -not $cleanup.Contains('powershell') -and -not $cleanup.Contains('cmd.exe')) 'Cleanup shells out instead of using typed APIs.'
Assert-Recovery ($cleanupDomain.Contains('CurrentBundleProviderKey = "GaltekSolution.GaltekClassroom.Client.Bundle.0.0.6"') -and $cleanupDomain.Contains('CurrentMsiProductCode = "{C9698974-44A1-4751-A914-D70152A28B5B}"')) '0.0.6 cleanup authority is not exact.'
Assert-Recovery ($cleanupDomain.Contains('CLEANUP_GRAPH_BEGIN') -and $cleanupDomain.Contains('CLEANUP_GRAPH_EDGE') -and $cleanupDomain.Contains('CLEANUP_GRAPH_VALIDATED') -and $cleanupDomain.Contains('CLEANUP_PLAN_STEP')) 'Graph migration diagnostics are incomplete.'
Assert-Recovery ($cleanupDomain.Contains('SelfDependent') -and $cleanupDomain.Contains('KnownLegacyDependent') -and $cleanupDomain.Contains('CurrentDependent') -and $cleanupDomain.Contains('UnknownDependent')) 'Dependent classification is incomplete.'
Assert-Recovery ($cleanupDomain.Contains('ResumeModeActive') -and $cleanupDomain.Contains('CURRENT_REGISTRATION_LIFECYCLE')) 'Active-session lifecycle is not validated explicitly.'
Assert-Recovery ($cleanup.Contains('QueryFullProcessImageName') -and $cleanupDomain.Contains('HISTORICAL_PROCESS_')) 'Historical process guard is not exact-path typed.'
Assert-Recovery ($cleanupDomain.Contains('CLEANUP_PRECONDITION') -and $cleanupDomain.Contains('CLEANUP_STEP') -and $cleanupDomain.Contains('CLEANUP_BLOCKED') -and $cleanupDomain.Contains('CLEANUP_FAILED') -and $cleanupDomain.Contains('CLEANUP_COMPLETE')) 'Typed cleanup diagnostics are incomplete.'
Assert-Recovery ($cleanupProgram.Contains('CleanupExitCode.Blocked') -and $cleanupDomain.Contains('Blocked = 2010')) 'Cleanup exit-code contract is not stable and explicit.'
Assert-Recovery ($cleanupLog.Contains('CommonApplicationData') -and $cleanupLog.Contains('MaximumRetainedLogs = 10')) 'Cleanup log is not durable and bounded.'
Assert-Recovery ($ba.Contains('LogCleanupSummary') -and $ba.Contains('CLEANUP_HELPER') -and $ba.Contains('CLEANUP_LOG path=')) 'BA does not surface helper diagnostics in the Burn log.'

$capture = $package.IndexOf('<Custom Action="CaptureClientState" Before="StopSessionAgentTask"', [StringComparison]::Ordinal)
$stopSession = $package.IndexOf('<Custom Action="StopSessionAgentTask" Before="DisableSessionAgentTask"', [StringComparison]::Ordinal)
$disableSession = $package.IndexOf('<Custom Action="DisableSessionAgentTask" Before="StopServices"', [StringComparison]::Ordinal)
Assert-Recovery ($capture -ge 0 -and $stopSession -ge 0 -and $disableSession -ge 0) 'FilesInUse quiesce order is not Capture -> Session stop/disable -> Service stop.'
Assert-Recovery ($package.Contains('<SetProperty Id="REINSTALLMODE" Value="emus"')) 'F1 same-version payload policy regressed.'

Write-Output 'RELATED_BUNDLE_RECOVERY_CONTRACT_TESTS_PASS exact-four-suppression valid-apply-hwnd graph-migration servicing files-in-use-order'
