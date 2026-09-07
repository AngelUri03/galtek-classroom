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
publish-credential-provider.ps1
```

Default outputs:

```text
artifacts\windows\agent-service\
artifacts\windows\agent-session\
artifacts\windows\credential-provider\
```

Service and Session publishes are `Release`, `win-x64`, self-contained and folder-based. Credential Provider publish is native `Release|x64` and produces only the DLL plus manifest. Publishing does not modify Program Files, ProgramData or HKLM.

## Full Agent Install Or Update

From an elevated PowerShell session:

```powershell
.\installer\windows\install-agent.ps1
```

This thin orchestrator calls:

```text
install-agent-service.ps1
install-session-agent.ps1
install-credential-provider.ps1
test-credential-provider-installation.ps1
```

Order: Agent Service, Session Agent, Credential Provider, then read-only Credential Provider verification. The Service installer manages `%ProgramFiles%\Galtek\Classroom\Agent\` and preserves the `Session\` and `CredentialProvider\` subdirectories. The Session installer manages `%ProgramFiles%\Galtek\Classroom\Agent\Session\` and preserves ProgramData. If Credential Provider install fails after Service/Session succeeded, the orchestrator fails and reports a partial Agent install instead of declaring success.

## Full Agent Uninstall

From an elevated PowerShell session:

```powershell
.\installer\windows\uninstall-agent.ps1
```

This thin orchestrator removes the Credential Provider registration first, then the Session Agent, then the Service.

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

## Credential Provider Product Lifecycle

Publish only the Credential Provider:

```powershell
.\installer\windows\publish-credential-provider.ps1
```

Default output:

```text
artifacts\windows\credential-provider\
  GaltekClassroom.CredentialProvider.dll
  credential-provider.manifest.json
```

The package manifest includes schema, product/component, fixed CLSID, architecture, filename, SHA-256 and deterministic packageId. It does not contain secrets. SHA-256 validates artifact integrity but does not replace Authenticode signing.

Validate a package without elevation:

```powershell
.\installer\windows\test-credential-provider-package.ps1
```

Install or update only the Credential Provider from an elevated x64 PowerShell session:

```powershell
.\installer\windows\install-credential-provider.ps1
```

The script:

- requires Windows x64, PowerShell x64 and elevation;
- validates manifest, SHA-256, PE x64 and Authenticode status;
- fails closed for `SIGNED_INVALID`;
- requires Agent Service `GaltekClassroomAgent` to be installed;
- stages immutable versions under `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\versions\<packageId>\`;
- registers only Galtek CLSID `{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}` in HKLM x64;
- sets `InprocServer32` to the staged DLL and `ThreadingModel` to `Apartment`;
- never creates a Credential Provider Filter;
- rolls back Galtek registration in-memory if install validation fails.

Verify installed registration without repair:

```powershell
.\installer\windows\test-credential-provider-installation.ps1
```

Uninstall only the Credential Provider:

```powershell
.\installer\windows\uninstall-credential-provider.ps1
```

The uninstaller removes only Galtek provider registration and Galtek COM registration, then attempts package cleanup best-effort. If LogonUI still has a DLL mapped, it does not kill LogonUI/Winlogon and reports `UNREGISTERED_REBOOT_CLEANUP_REQUIRED`.

Authenticode signing remains a release gate before commercial external distribution if no real production certificate is present. `NOT_SIGNED` is tolerated only for controlled/lab deployment; `SIGNED_INVALID` fails closed.

## Credential Provider Lab / Dev

Manual lab-only scripts remain available:

```powershell
.\installer\windows\register-credential-provider-dev.ps1
.\installer\windows\unregister-credential-provider-dev.ps1
```

These scripts are not part of the production installer. Use them only on a disposable lab PC with a known local administrator password and recovery available.

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
