#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = "Stop"

$providerClsid = "{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}"
$providerName = "Galtek Classroom Credential Provider"
$classKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$providerClsid"
$providerKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$providerClsid"

Write-Warning "LAB / DEV ONLY: this removes only the Galtek Classroom Credential Provider registration. Product uninstalls must use uninstall-credential-provider.ps1."
Write-Warning "Windows standard Credential Providers are left untouched."

if ($PSCmdlet.ShouldProcess($providerName, "Unregister Credential Provider")) {
    if (Test-Path -LiteralPath $providerKey) {
        Remove-Item -LiteralPath $providerKey -Recurse -Force
    }

    if (Test-Path -LiteralPath $classKey) {
        Remove-Item -LiteralPath $classKey -Recurse -Force
    }
}

Write-Host "Unregistered $providerName ($providerClsid) if it was present."
