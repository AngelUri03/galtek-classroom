#Requires -Version 5.1
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-F3C([bool]$Condition,[string]$Message) { if(-not $Condition){ throw "F3C_PERFORMANCE_CONTRACT_FAILED: $Message" } }
$setup = Join-Path $PSScriptRoot 'setup'
$bundle = Get-Content -LiteralPath (Join-Path $setup 'Bundle.wxs') -Raw
$ba = Get-Content -LiteralPath (Join-Path $setup 'BootstrapperApplication\GaltekBootstrapperApplication.cs') -Raw
$detector = Get-Content -LiteralPath (Join-Path $setup 'BootstrapperApplication\LegacyDetector.cs') -Raw
$viewModel = Get-Content -LiteralPath (Join-Path $setup 'BootstrapperApplication\InstallerViewModel.cs') -Raw
$lifecycle = Get-Content -LiteralPath (Join-Path $setup 'client-lifecycle.ps1') -Raw
Assert-F3C ($bundle.Contains('<Chain DisableSystemRestore="yes">')) 'Official Burn Chain restore-point opt-out is missing.'
Assert-F3C (([regex]::Matches($ba, 'LegacyDetector\.Detect\(\)')).Count -eq 1) 'Legacy detection must run once per BA session.'
Assert-F3C (-not $ba.Contains('powershell.exe') -and -not $ba.Contains('ClientStateHelper.exe') -and -not $ba.Contains('Arguments = "detect"')) 'BA detect must not launch PowerShell or a helper process.'
Assert-F3C (($detector + $lifecycle) -notmatch '(?i)Get-CimInstance|Get-WmiObject') 'CIM/WMI is forbidden.'
Assert-F3C ($detector -notmatch '(?i)EnumerateFiles|Directory\.GetFiles|GetFileHash|Thread\.Sleep') 'Detect contains scan/hash/sleep work.'
Assert-F3C ($viewModel.Contains('TimeSpan.FromSeconds(1)')) 'Timer exceeds the 1 Hz contract.'
Assert-F3C ($viewModel.Contains('if (!this.progressKnown && value == 0) return;')) 'Unknown zero progress does not remain indeterminate.'
Assert-F3C ($lifecycle.Contains('$GaltekHashCache')) 'Per-operation payload hash cache is missing.'
Write-Output 'F3C_PERFORMANCE_CONTRACT_TESTS_PASS restore-point-off native-single-detect no-powershell no-cim no-wmi no-scan timer-1hz hash-cache'
