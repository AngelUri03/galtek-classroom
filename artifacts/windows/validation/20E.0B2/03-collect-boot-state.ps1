# Local read-only boot snapshot. EventLog is the ILogger provider registered by AddWindowsService.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as Administrator.' }
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
$serviceName = 'GaltekClassroomAgent'
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
"Boot: $($boot.ToString('o')); Now: $((Get-Date).ToString('o'))"
"Service: $(if ($null -eq $service) { 'MISSING' } else { $service.State })"
Get-CimInstance Win32_Process -Filter "Name='GaltekClassroom.Agent.Service.exe'" | ForEach-Object { "Agent process PID=$($_.ProcessId); started=$($_.CreationDate)" }
$scmIds = 7000,7009,7011,7022,7023,7024,7031,7034,7036
'SCM events for GaltekClassroomAgent since boot:'
Get-WinEvent -FilterHashtable @{ LogName='System'; ProviderName='Service Control Manager'; Id=$scmIds; StartTime=$boot } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match [regex]::Escape($serviceName) } |
    ForEach-Object { "$($_.TimeCreated.ToString('o')) Id=$($_.Id)" }
$markers = @('AGENT_SERVICE_STARTING','AGENT_SERVICE_READY','MASTER_CONNECTION_ATTEMPT','MASTER_CONNECTION_ESTABLISHED','MASTER_CONNECTION_LOST','MASTER_CONNECTION_RETRY_SCHEDULED')
'Agent lifecycle markers from Windows Application EventLog since boot:'
Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$boot } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in @('GaltekClassroom.Agent.Service','GaltekClassroomAgent','Galtek Classroom Agent Service') } |
    ForEach-Object {
        $event = $_
        foreach ($marker in $markers) { if ($event.Message -match [regex]::Escape($marker)) { "$($event.TimeCreated.ToString('o')) $marker"; break } }
    }
$bootstrapPath = 'C:\ProgramData\Galtek\Classroom\Diagnostics\startup-bootstrap.log'
'Bounded bootstrap startup diagnostics (last 200 recognized records):'
if (Test-Path -LiteralPath $bootstrapPath -PathType Leaf) {
    Get-Content -LiteralPath $bootstrapPath -Tail 200 |
        Where-Object { $_ -match ' BOOTSTRAP_(PROCESS_ENTER|CREATE_BUILDER_(BEGIN|OK)|CONFIG_(BEGIN|OK)|DI_BUILD_(BEGIN|OK)|HOST_RUN_BEGIN|FATAL )' }
}
else {
    'MISSING'
}
