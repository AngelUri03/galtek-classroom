# One on-demand, local read-only snapshot. No probes or polling.
[CmdletBinding()]
param(
    [datetime] $Since
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as Administrator.' }
$service = Get-CimInstance Win32_Service -Filter "Name='GaltekClassroomAgent'"
"Timestamp: $((Get-Date).ToString('o')); Service: $(if ($null -eq $service) { 'MISSING' } else { $service.State })"
'Physical adapters and current IPv4:'
Get-NetAdapter -Physical | ForEach-Object {
    $adapter = $_
    $addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty IPAddress)
    $gateways = @(Get-NetRoute -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty NextHop)
    "$($adapter.Name): Link=$($adapter.Status); IPv4=$($addresses -join ','); Gateway=$($gateways -join ',')"
}
$configPath = 'C:\Program Files\Galtek\Classroom\Agent\appsettings.json'
if (Test-Path -LiteralPath $configPath -PathType Leaf) {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $node = $config
    foreach ($key in @('Galtek','Classroom','Agent','MasterConnection','MasterEndpoint')) {
        if ($null -eq $node -or $null -eq $node.PSObject.Properties[$key]) { $node = $null; break }
        $node = $node.PSObject.Properties[$key].Value
    }
    $endpoint = [string]$node
    $uri = $null
    if ($endpoint -and [Uri]::TryCreate($endpoint, [UriKind]::Absolute, [ref]$uri)) {
        $addresses = @(Resolve-DnsName -Name $uri.Host -Type A -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress)
        foreach ($address in $addresses) {
            $route = Find-NetRoute -RemoteIPAddress $address -ErrorAction SilentlyContinue
            if ($route) { "Master route $address via interface=$($route.InterfaceAlias), nextHop=$($route.NextHop)" }
        }
        if (-not $addresses) { 'Master route unavailable: host did not resolve locally.' }
    } else { 'Master route unavailable: endpoint not configured.' }
}
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
$eventStart = if ($PSBoundParameters.ContainsKey('Since')) { $Since } else { $boot }
$markers = @('MASTER_CONNECTION_ATTEMPT','MASTER_CONNECTION_ESTABLISHED','MASTER_CONNECTION_LOST','MASTER_CONNECTION_RETRY_SCHEDULED')
"Agent connection markers since $($eventStart.ToString('o')):"
Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$eventStart } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in @('GaltekClassroom.Agent.Service','GaltekClassroomAgent','Galtek Classroom Agent Service') } |
    ForEach-Object {
        $event = $_
        foreach ($marker in $markers) { if ($event.Message -match [regex]::Escape($marker)) { "$($event.TimeCreated.ToString('o')) $marker"; break } }
    }
