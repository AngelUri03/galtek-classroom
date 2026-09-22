# PC14 local, elevated, read-only except for its diagnostic TXT.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as Administrator.' }

$serviceName = 'GaltekClassroomAgent'
$agentDir = 'C:\Program Files\Galtek\Classroom\Agent'
$dataDir = 'C:\ProgramData\Galtek\Classroom'
$diagnostics = Join-Path $dataDir 'Diagnostics\20E.0B2'
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
if ($null -eq $service) { throw "Service $serviceName is missing." }
$configPath = Join-Path $agentDir 'appsettings.json'
$config = if (Test-Path -LiteralPath $configPath -PathType Leaf) { Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json } else { $null }
$master = $null
if ($null -ne $config) {
    $node = $config
    foreach ($key in @('Galtek','Classroom','Agent','MasterConnection')) {
        if ($null -eq $node -or $null -eq $node.PSObject.Properties[$key]) { $node = $null; break }
        $node = $node.PSObject.Properties[$key].Value
    }
    $master = $node
}
$endpoint = if ($null -ne $master -and $null -ne $master.PSObject.Properties['MasterEndpoint']) { [string]$master.MasterEndpoint } else { '' }
$uri = $null
if ($endpoint -and [Uri]::TryCreate($endpoint, [UriKind]::Absolute, [ref]$uri)) {
    $hostValue = $uri.Host
    $portValue = $uri.Port
} else { $hostValue = '<unavailable>'; $portValue = '<unavailable>' }
$lines = @(
    "Timestamp: $((Get-Date).ToString('o'))"
    "Hostname: $env:COMPUTERNAME"
    "Windows: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version) build $((Get-CimInstance Win32_OperatingSystem).BuildNumber)"
    "Service: $serviceName; Status=$($service.State); StartMode=$($service.StartMode); StartName=$($service.StartName); PathName=$($service.PathName)"
    'sc.exe qc:'
    (& sc.exe qc $serviceName)
    'sc.exe qfailure:'
    (& sc.exe qfailure $serviceName)
)
foreach ($name in @('GaltekClassroom.Agent.Service.exe','GaltekClassroom.Agent.Service.dll')) {
    $path = Join-Path $agentDir $name
    $lines += if (Test-Path -LiteralPath $path -PathType Leaf) { "$name SHA256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash)" } else { "$name MISSING" }
}
$enabled = if ($null -ne $master -and $null -ne $master.PSObject.Properties['Enabled']) { $master.Enabled } else { '<unavailable>' }
$lines += "MasterConnection exists=$($null -ne $master); Enabled=$enabled; Host=$hostValue; Port=$portValue"
foreach ($name in @('installation.json','network-identity.json','license.dat','authorized-masters.json','managed-windows-accounts.json','managed-windows-credentials.dat')) {
    $lines += "$name present=$(Test-Path -LiteralPath (Join-Path $dataDir $name) -PathType Leaf)"
}
New-Item -ItemType Directory -Path $diagnostics -Force | Out-Null
$report = Join-Path $diagnostics ("baseline-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$lines | Set-Content -LiteralPath $report -Encoding UTF8
$lines
"Saved: $report"
