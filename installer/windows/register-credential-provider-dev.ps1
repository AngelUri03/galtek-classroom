#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DllPath = "$env:ProgramFiles\Galtek\Classroom\Agent\CredentialProvider\GaltekClassroom.CredentialProvider.dll"
)

$ErrorActionPreference = "Stop"

$providerClsid = "{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}"
$providerName = "Galtek Classroom Credential Provider"
$classKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$providerClsid"
$inprocKey = "$classKey\InprocServer32"
$providerKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$providerClsid"

Write-Warning "LAB / DEV ONLY: this registers a native Windows Credential Provider. Product installs must use install-credential-provider.ps1."
Write-Warning "The Windows password/PIN/Hello providers are not filtered or modified by this script."

$resolvedDllPath = [System.IO.Path]::GetFullPath($DllPath)
if (-not (Test-Path -LiteralPath $resolvedDllPath -PathType Leaf)) {
    throw "Credential Provider DLL was not found: $resolvedDllPath"
}

$programFilesRoot = [System.IO.Path]::GetFullPath($env:ProgramFiles)
if (-not $resolvedDllPath.StartsWith($programFilesRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to register from outside Program Files. Copy the DLL to a controlled lab install directory first."
}

$repoFragment = "\galtek-classroom\"
if ($resolvedDllPath.IndexOf($repoFragment, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "Refusing to register a repo artifact directly. Register the installed lab DLL instead."
}

if ($PSCmdlet.ShouldProcess($resolvedDllPath, "Register Galtek Classroom Credential Provider")) {
    New-Item -Path $classKey -Force | Out-Null
    New-ItemProperty -Path $classKey -Name "(default)" -Value $providerName -PropertyType String -Force | Out-Null

    New-Item -Path $inprocKey -Force | Out-Null
    New-ItemProperty -Path $inprocKey -Name "(default)" -Value $resolvedDllPath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $inprocKey -Name "ThreadingModel" -Value "Apartment" -PropertyType String -Force | Out-Null

    New-Item -Path $providerKey -Force | Out-Null
    New-ItemProperty -Path $providerKey -Name "(default)" -Value $providerName -PropertyType String -Force | Out-Null
}

Write-Host "Registered $providerName ($providerClsid)"
Write-Host "DLL: $resolvedDllPath"
Write-Host "No Credential Provider Filter was registered."
