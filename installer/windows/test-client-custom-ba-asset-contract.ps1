#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$setupRoot = Join-Path $PSScriptRoot 'setup'
$logo = Join-Path $repositoryRoot 'logo.png'
$expected = 'fd004063ddfa305c3703190cef786d1b5b9e61dbdcd7b29419db1cb10775869e'

function Assert-Asset([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "CUSTOM_BA_ASSET_CONTRACT_FAILED: $Message" }
}

Assert-Asset (Test-Path -LiteralPath $logo -PathType Leaf) 'Official logo.png is missing.'
Assert-Asset ((Get-FileHash -LiteralPath $logo -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected) 'Official logo hash changed.'
$project = Get-Content -LiteralPath (Join-Path $setupRoot 'BootstrapperApplication\GaltekClassroom.Bootstrapper.csproj') -Raw
$xaml = Get-Content -LiteralPath (Join-Path $setupRoot 'BootstrapperApplication\MainWindow.xaml') -Raw
$bundle = Get-Content -LiteralPath (Join-Path $setupRoot 'Bundle.wxs') -Raw
Assert-Asset ($project.Contains('galtek-classroom-icon.png')) 'Custom BA does not embed the official derived symbol.'
Assert-Asset ($xaml.Contains('Assets/galtek-classroom-icon.png')) 'Custom BA header does not render the embedded symbol.'
Assert-Asset ($bundle.Contains('galtek-classroom.ico')) 'Bundle icon does not use the official derived icon.'
Assert-Asset (-not $bundle.Contains('window-background.png')) 'Obsolete ThmUtil background remains packaged.'
Assert-Asset (-not $bundle.Contains('button-primary.png')) 'Obsolete ThmUtil button sprites remain packaged.'
Assert-Asset (-not $bundle.Contains('progress-brand.png')) 'Obsolete ThmUtil progress bitmap remains packaged.'

Write-Output 'CUSTOM_BA_ASSET_CONTRACT_TESTS_PASS'
