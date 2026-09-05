# Galtek Classroom Agent - Windows Installer Scripts

These scripts manage the Windows Agent lifecycle without MSI, WiX, Inno Setup or a graphical installer.

## Requirements

- Windows x64.
- PowerShell 5.1 or newer.
- .NET SDK 8.0 to publish.
- Elevated PowerShell for install, update and uninstall.

Run installation scripts from an Administrator PowerShell session. The scripts do not bypass UAC.

## Full Agent Publish

From the repository root:

```powershell
.\installer\windows\publish-agent.ps1
```

This thin orchestrator calls:

```text
publish-agent-service.ps1
publish-agent-session.ps1
```

Default outputs:

```text
artifacts\windows\agent-service\
artifacts\windows\agent-session\
```

Both publishes are `Release`, `win-x64`, self-contained and folder-based. Publishing does not modify Program Files or ProgramData.

## Full Agent Install Or Update

From an elevated PowerShell session:

```powershell
.\installer\windows\install-agent.ps1
```

This thin orchestrator calls:

```text
install-agent-service.ps1
install-session-agent.ps1
```

The Service installer manages `%ProgramFiles%\Galtek\Classroom\Agent\` and preserves the `Session\` subdirectory. The Session installer manages `%ProgramFiles%\Galtek\Classroom\Agent\Session\` and preserves ProgramData.

## Full Agent Uninstall

From an elevated PowerShell session:

```powershell
.\installer\windows\uninstall-agent.ps1
```

This thin orchestrator removes the Session Agent first and then the Service.

By default it preserves:

```text
%ProgramData%\Galtek\Classroom\
```

To intentionally remove machine identity, license data and Master Windows Binding:

```powershell
.\installer\windows\uninstall-agent.ps1 -PurgeData
```

`-PurgeData` is passed only to `uninstall-agent-service.ps1`.

Warning: `PurgeData` elimina Installation Identity, Commercial License, Master Windows Binding y Network Identity. La instalacion resultante requerira una nueva activacion, reconfiguracion Master y nueva identidad de red.

## Agent Service

Publish only the Service:

```powershell
.\installer\windows\publish-agent-service.ps1
```

Install or update only the Service:

```powershell
.\installer\windows\install-agent-service.ps1
```

The script:

- validates that the published artifact exists;
- stops the existing service when present;
- copies Service binaries to `%ProgramFiles%\Galtek\Classroom\Agent\`;
- preserves `%ProgramFiles%\Galtek\Classroom\Agent\Session\` if it exists;
- creates or reconfigures service `GaltekClassroomAgent`;
- sets startup type to `Automatic`;
- runs as `LocalSystem`;
- configures service recovery restart delays of 5, 15 and 60 seconds;
- starts the service and verifies `Running`.

ProgramData is created or verified at `%ProgramData%\Galtek\Classroom\`, but existing `installation.json`, `license.dat` and `master-binding.json` are not deleted.

Verify:

```powershell
Get-Service GaltekClassroomAgent
Get-CimInstance Win32_Service -Filter "Name='GaltekClassroomAgent'" |
    Select-Object Name, State, StartMode, StartName, PathName
```

Uninstall only the Service:

```powershell
.\installer\windows\uninstall-agent-service.ps1
```

The Service uninstaller preserves `Agent\Session\` when present and preserves ProgramData unless `-PurgeData` is passed. With `-PurgeData`, it also attempts to remove the Network Identity CNG machine key only when `network-identity.json` contains a safe Galtek `keyName`.

## Session Agent

Publish only the Session Agent:

```powershell
.\installer\windows\publish-agent-session.ps1
```

Install or update only the Session Agent:

```powershell
.\installer\windows\install-session-agent.ps1
```

The script:

- validates that the published artifact exists;
- stops the scheduled task if it is running;
- stops only installed Session Agent processes whose executable path is `%ProgramFiles%\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe`;
- copies Session Agent binaries to `%ProgramFiles%\Galtek\Classroom\Agent\Session\`;
- creates or replaces scheduled task `GaltekClassroomSessionAgent`;
- validates action, trigger, principal, run level, network setting and multiple-instance policy;
- starts the task for the current session when possible.

Scheduled Task configuration:

```text
TaskName: GaltekClassroomSessionAgent
Description: Galtek Classroom Session Agent
Trigger: AtLogon
Principal: S-1-5-32-545 (Builtin Users)
RunLevel: Limited
Action: GaltekClassroom.Agent.Session.exe --background
MultipleInstances: Parallel
ExecutionTimeLimit: none
Network required: false
Battery start allowed: true
```

`MultipleInstances Parallel` keeps the design compatible with fast user switching and RDP. Duplicate control is done inside the Session Agent with a per-session mutex.

Verify:

```powershell
Get-ScheduledTask -TaskName GaltekClassroomSessionAgent
Get-CimInstance Win32_Process -Filter "Name='GaltekClassroom.Agent.Session.exe'" |
    Select-Object ProcessId, SessionId, ExecutablePath, CommandLine
```

`SessionId` should match the interactive user session and should not be `0` during normal operation.

Uninstall only the Session Agent:

```powershell
.\installer\windows\uninstall-session-agent.ps1
```

The Session uninstaller removes the task and `%ProgramFiles%\Galtek\Classroom\Agent\Session\`. It does not touch ProgramData and has no `-PurgeData` option.

## Credential Provider Lab

Prompt 19G1 adds manual lab-only scripts for the native Credential Provider V2 foundation:

```powershell
.\installer\windows\register-credential-provider-dev.ps1
.\installer\windows\unregister-credential-provider-dev.ps1
```

These scripts are not part of the general production installer. Use them only on a disposable lab PC with a known local administrator password and recovery available.

The registration script points by default to:

```text
%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\GaltekClassroom.CredentialProvider.dll
```

It refuses DLL paths outside Program Files and does not register a Credential Provider Filter. Standard Windows providers such as password, PIN and Windows Hello must remain visible.

## Diagnostics

From development builds:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-ping
C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-status
```

The commands print JSON and terminate. They do not start the background supervisor.

Useful Service lifecycle commands:

```powershell
Stop-Service GaltekClassroomAgent
Start-Service GaltekClassroomAgent
Restart-Service GaltekClassroomAgent
```

## Troubleshooting

- If install or uninstall reports elevation errors, reopen PowerShell as Administrator.
- If publish cannot find the SDK, install .NET SDK 8.0 or set `DOTNET_ROOT`.
- If the Service fails to start with an identity error, inspect `%ProgramData%\Galtek\Classroom\installation.json`; corrupt identity files are not regenerated silently.
- If license status is `ACTIVATION_REQUIRED` or `LICENSE_KEY_NOT_CONFIGURED`, the Windows Service and Session Agent should still remain running and IPC read-only status should remain available.
