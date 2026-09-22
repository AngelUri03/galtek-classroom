# Local read-only final check. Reads only public identity fields and binding count.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as Administrator.' }
$service = Get-CimInstance Win32_Service -Filter "Name='GaltekClassroomAgent'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or $service.StartName -notin @('LocalSystem','Local System')) { throw 'FAIL: Service must be Running, Automatic, LocalSystem.' }
"Service PASS: Running, Automatic, LocalSystem; timestamp=$((Get-Date).ToString('o'))"
$agentDir = 'C:\Program Files\Galtek\Classroom\Agent'
$dataDir = 'C:\ProgramData\Galtek\Classroom'
foreach ($name in @('installation.json','network-identity.json','license.dat','authorized-masters.json','managed-windows-accounts.json','managed-windows-credentials.dat')) {
    $present = Test-Path -LiteralPath (Join-Path $dataDir $name) -PathType Leaf
    "$name present=$present"
    if (-not $present) { throw "FAIL: $name missing." }
}
$installationPath = Join-Path $dataDir 'installation.json'
if (Test-Path -LiteralPath $installationPath -PathType Leaf) {
    $identity = Get-Content -LiteralPath $installationPath -Raw | ConvertFrom-Json
    "Installation ID: $($identity.installationId)"
}
$networkPath = Join-Path $dataDir 'network-identity.json'
if (Test-Path -LiteralPath $networkPath -PathType Leaf) {
    $network = Get-Content -LiteralPath $networkPath -Raw | ConvertFrom-Json
    "Network ID: $($network.networkIdentityId); public fingerprint: $($network.publicKeyFingerprint)"
}
$bindingsPath = Join-Path $dataDir 'managed-windows-accounts.json'
if (Test-Path -LiteralPath $bindingsPath -PathType Leaf) {
    $bindings = Get-Content -LiteralPath $bindingsPath -Raw | ConvertFrom-Json
    $bindingCount = if ($null -eq $bindings.bindings) { 0 } else { @($bindings.bindings).Count }
    "Managed account bindings count: $bindingCount"
    if ($bindingCount -lt 1) { throw 'FAIL: no managed account bindings.' }
}
foreach ($name in @('GaltekClassroom.Agent.Service.exe','GaltekClassroom.Agent.Service.dll')) {
    $path = Join-Path $agentDir $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "FAIL: $name missing." }
    "$name SHA256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash)"
}
$configPath = Join-Path $agentDir 'appsettings.json'
$backupRoot = 'C:\Program Files\Galtek\Classroom\Agent-backups\20E.0B2'
$backup = Get-ChildItem -LiteralPath $backupRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
if ($null -eq $backup) { throw 'FAIL: deployment backup missing.' }
$backupConfig = Join-Path $backup.FullName 'appsettings.json'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf) -or -not (Test-Path -LiteralPath $backupConfig -PathType Leaf)) { throw 'FAIL: appsettings.json or its backup missing.' }
$currentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash
$backupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $backupConfig).Hash
if ($currentHash -ne $backupHash) { throw 'FAIL: appsettings.json hash differs from deployment backup.' }
"appsettings.json PASS: SHA256=$currentHash; backup=$($backup.FullName)"
