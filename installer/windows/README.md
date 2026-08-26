# Galtek Classroom Agent Service - Windows Installer Scripts

These scripts manage the Windows Service lifecycle without MSI, WiX, Inno Setup or a graphical installer.

## Requirements

- Windows x64.
- PowerShell 5.1 or newer.
- .NET SDK 8.0 to publish.
- Elevated PowerShell for install, update and uninstall.

Run installation scripts from an Administrator PowerShell session. The scripts do not bypass UAC.

## Publish

From the repository root:

```powershell
.\installer\windows\publish-agent-service.ps1
```

Default artifact output:

```text
artifacts\windows\agent-service\
```

The publish is `Release`, `win-x64`, and self-contained. Publishing does not modify Program Files or ProgramData.

Optional custom output:

```powershell
.\installer\windows\publish-agent-service.ps1 -OutputPath C:\Builds\Galtek\agent-service
```

## Install

From an elevated PowerShell session:

```powershell
.\installer\windows\install-agent-service.ps1
```

The script:

- validates that the published artifact exists;
- stops the existing service when present;
- copies binaries to `%ProgramFiles%\Galtek\Classroom\Agent\`;
- creates or reconfigures service `GaltekClassroomAgent`;
- sets startup type to `Automatic`;
- runs as `LocalSystem`;
- configures service recovery restart delays of 5, 15 and 60 seconds;
- starts the service and verifies `Running`.

ProgramData is created or verified at `%ProgramData%\Galtek\Classroom\`, but existing `installation.json` and `license.dat` are not deleted.

## Update

Publish a new artifact, then run the same install script again:

```powershell
.\installer\windows\publish-agent-service.ps1
.\installer\windows\install-agent-service.ps1
```

The service name is stable. Updates replace binaries in Program Files and preserve ProgramData, so the Installation Identity and Commercial License survive normal upgrades.

## Verify

```powershell
Get-Service GaltekClassroomAgent
Get-CimInstance Win32_Service -Filter "Name='GaltekClassroomAgent'" |
    Select-Object Name, State, StartMode, StartName, PathName
```

Useful lifecycle commands:

```powershell
Stop-Service GaltekClassroomAgent
Start-Service GaltekClassroomAgent
Restart-Service GaltekClassroomAgent
```

IPC checks from the development Session Agent:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj -- --ipc-ping
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj -- --ipc-status
```

## Uninstall

From an elevated PowerShell session:

```powershell
.\installer\windows\uninstall-agent-service.ps1
```

The script stops and removes the Windows Service registration, then removes the installed binaries from Program Files.

By default it preserves:

```text
%ProgramData%\Galtek\Classroom\
```

That directory contains the persistent Installation Identity and Commercial License.

To intentionally remove machine identity and license data:

```powershell
.\installer\windows\uninstall-agent-service.ps1 -PurgeData
```

`-PurgeData` deletes Installation Identity and Commercial License. The next installation will require activation again.

## Troubleshooting

- If install or uninstall reports elevation errors, reopen PowerShell as Administrator.
- If publish cannot find the SDK, install .NET SDK 8.0 or set `DOTNET_ROOT`.
- If the service fails to start with an identity error, inspect `%ProgramData%\Galtek\Classroom\installation.json`; corrupt identity files are not regenerated silently.
- If license status is `ACTIVATION_REQUIRED` or `LICENSE_KEY_NOT_CONFIGURED`, the Windows Service should still remain running and IPC read-only status should remain available.
