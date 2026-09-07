# Galtek Classroom

Galtek Classroom is a LAN-first classroom and cybercafe administration product for Windows environments. The product will let one or more Master computers supervise authorized Client computers in a local network, while keeping commercial licensing, network trust, and device identity as separate concerns.

The current iteration implements local Installation Identity, development Machine Code output, local Commercial License validation, local Master Windows Binding authorization through the Agent Service, Local IPC API v1 for read-only status queries, a real installable Windows Service flow for the Agent Service, a background/autostart lifecycle for the Session Agent, SQLite persistence for the Master classroom domain, and the first protected Master administrative API. It does not implement remote control, discovery, pairing, screen capture, projection, network transport, or the future desktop UI.

## Architecture

- `master-backend/`: Java 21, Spring Boot 3.x, Maven backend for the Master application.
- `master-backend/src/main/resources/db/migration/sqlite/`: Flyway migrations for the Master SQLite database.
- `agent/`: C#/.NET solution for Windows Agent components.
- `agent/src/GaltekClassroom.Agent.Service/`: Worker Service / Generic Host; owns Installation Identity and Commercial License locally, and hosts Local IPC API v1.
- `agent/src/GaltekClassroom.Agent.Session/`: silent user-session Agent with background lifecycle, Local IPC supervisor, and status/ping diagnostics.
- `agent/src/GaltekClassroom.Agent.Shared/`: shared constants, Installation Identity, Machine Code, Commercial License state models, and Local IPC contracts/framing.
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`: automated tests for Installation Identity, Machine Code, Commercial License, and Local IPC behavior.
- `agent/tests/GaltekClassroom.Agent.Session.Tests/`: automated tests for Session Agent lifecycle, single instance locking, backoff, reconnect behavior, and CLI mode parsing.
- `protocol/`: cross-language protocol notes, including `protocol/local-ipc-v1.md`.
- `installer/windows/`: PowerShell scripts for publishing, installing/updating, verifying and uninstalling the Agent Service, Session Agent, Credential Provider, or full Agent.
- `docs/`: project context, API notes, architecture notes, decisions, and handoff state.

Before developing classroom functionality, read `docs/context/FUNCTIONAL_MODEL.md`. It is the stable domain contract for Devices, Students, Student Workspaces, Master Windows Binding, batch-first operations, error handling, retry, and rollback.

## Development Requirements

- Windows development environment.
- Java 21.
- Maven 3.9+.
- .NET SDK 8.0 LTS or newer compatible LTS SDK.

In this workspace, the global `dotnet` command may expose only the runtime. Use `C:\Users\angel\.dotnet\dotnet.exe` when that happens.

## Master Backend

Run from PowerShell:

```powershell
cd master-backend
mvn spring-boot:run
```

For development, prefer an explicit local data directory so the backend does not use ProgramData:

```powershell
cd master-backend
$env:GALTEK_CLASSROOM_MASTER_DATA_DIR = "$PWD\.local-master-data"
mvn spring-boot:run
```

Health endpoint:

```powershell
Invoke-RestMethod http://localhost:8080/api/system/health
```

Local Agent status through IPC:

```powershell
Invoke-RestMethod http://localhost:8080/api/device/status
```

Machine Code through IPC:

```powershell
Invoke-RestMethod http://localhost:8080/api/device/machine-code
```

Master authorization through IPC:

```powershell
Invoke-RestMethod http://localhost:8080/api/master/authorization
```

If the Agent Service is not available, `/api/device/status`, `/api/device/machine-code` and `/api/master/authorization` return HTTP `503` with code `LOCAL_AGENT_UNAVAILABLE`. `/api/system/health` only reports Master Backend health and does not depend on the Agent Service.

### Master Administrative API

Administrative endpoints are documented in `docs/api/master-api-v1.md`.

They require local Master authorization through `MasterAccessGuard`. The only public diagnostic endpoints are:

- `GET /api/system/health`
- `GET /api/device/status`
- `GET /api/device/machine-code`
- `GET /api/master/authorization`

Bootstrap for the future UI:

```powershell
Invoke-RestMethod http://localhost:8080/api/master/bootstrap
```

Classroom snapshot:

```powershell
Invoke-RestMethod http://localhost:8080/api/classrooms/<classroomId>/snapshot
```

The API includes protected endpoints for classrooms, groups, students, batch student registration, assignments, assignment batch preflight/write, applications, operations and retryable targets. Assignment endpoints only change SQLite metadata in this phase; they do not move files or run remote commands.

### Master SQLite Data

The Master stores classroom-domain data in SQLite:

```text
<CommonApplicationData>\Galtek\Classroom\Master\classroom.db
```

Development/tests can override the directory:

```powershell
$env:GALTEK_CLASSROOM_MASTER_DATA_DIR = "$PWD\.local-master-data"
```

Relevant settings:

```yaml
galtek:
  classroom:
    master:
      storage:
        data-dir: ${GALTEK_CLASSROOM_MASTER_DATA_DIR:}
        database-file-name: classroom.db
        busy-timeout-ms: 5000
        maximum-pool-size: 4
        quick-check-on-startup: true
```

Migrations live in:

```text
master-backend/src/main/resources/db/migration/sqlite/
```

`classroom.db`, WAL/SHM sidecar files, and local `.db` files are ignored by Git. Master Windows Binding is intentionally not stored in this database; the Agent Service is the authority for that sensitive binding.

Run tests:

```powershell
cd master-backend
mvn clean verify
```

## Agent Data

The Agent stores machine-level data in:

```text
<CommonApplicationData>\Galtek\Classroom\
```

Files currently used:

- `installation.json`: local Installation Identity.
- `license.dat`: Commercial License JWT.
- `master-binding.json`: single local Master Windows Binding.

For development and tests, override that directory with:

```powershell
$env:GALTEK_CLASSROOM_DATA_DIR = "$PWD\.local-agent-data"
```

## Agent Build And Test

Build the full Agent solution:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln
```

Run .NET tests:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln
```

Run the Agent Service in development console mode:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj
```

The Service starts the Local IPC Named Pipe server on `GaltekClassroom.Agent.v1`.

### Master Windows Binding

Master features require both a Commercial License with role `MASTER` and a local Windows account binding. The binding is stored by the Agent Service in:

```text
<CommonApplicationData>\Galtek\Classroom\master-binding.json
```

Schema v1 contains `schemaVersion`, `installationId`, `windowsSid`, `accountDisplayName` and `boundAtUtc`. It does not store passwords, password hashes, tokens, JWTs or license material. The Master Backend never reads this file and does not know its path; it asks the Agent Service through Local IPC.

Configure the current Windows account from an elevated administrator shell:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --bind-master-current-user
```

Configure another existing local/domain account:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --bind-master-account "AULA\MaestraPrimaria"
```

Replace an existing binding only with explicit intent:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --bind-master-account "AULA\MaestraB" --replace-master-binding
```

The CLI does not autoelevate or bypass UAC. Without elevation it returns `ADMINISTRATOR_REQUIRED`. A missing account returns `WINDOWS_ACCOUNT_NOT_FOUND`, and an existing binding without replace returns `MASTER_BINDING_ALREADY_CONFIGURED`.

The authorization response is available at:

```powershell
Invoke-RestMethod http://localhost:8080/api/master/authorization
```

Possible statuses are `NOT_CONFIGURED`, `AUTHORIZED`, `CURRENT_ACCOUNT_NOT_AUTHORIZED`, `MASTER_LICENSE_REQUIRED`, `MASTER_BINDING_INVALID` and `MASTER_BINDING_INSTALLATION_MISMATCH`. A license with role `MASTER` is not enough by itself, and a different Windows administrator does not inherit Master access. This local binding also does not authorize remote Clients; network authorization, pairing and mTLS are separate future layers.

Run the Session Agent background lifecycle:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj -- --background
```

The Session Agent runs in the current interactive user session, acquires a per-session lock, waits for the Agent Service when it is unavailable, and reconnects automatically.

Query the Agent Service from the Session Agent through IPC:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-status
C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-ping
```

The diagnostics are one-shot commands: they print JSON and terminate without starting the background supervisor.

## Agent Publish And Install

Publish the complete Agent from the repository root:

```powershell
.\installer\windows\publish-agent.ps1
```

This calls the Service, Session and Credential Provider publish scripts and writes:

```text
artifacts\windows\agent-service\
artifacts\windows\agent-session\
artifacts\windows\credential-provider\
```

Service and Session artifacts are `Release`, `win-x64`, self-contained, folder-based, and ignored by Git. Credential Provider publish is native `Release|x64`, emits only the DLL plus manifest, and does not register the provider.

Install or update the complete Agent from an elevated PowerShell session:

```powershell
.\installer\windows\install-agent.ps1
```

The full installer installs Service, then Session Agent, then the Credential Provider product package. If Credential Provider install fails after Service/Session succeeded, the command fails and reports a partial Agent install.

Uninstall the complete Agent:

```powershell
.\installer\windows\uninstall-agent.ps1
```

The full uninstaller unregisters the Credential Provider before removing the Session Agent and Agent Service.

Normal uninstall preserves `%ProgramData%\Galtek\Classroom\`. To intentionally remove Installation Identity, Commercial License and Master Windows Binding:

```powershell
.\installer\windows\uninstall-agent.ps1 -PurgeData
```

`-PurgeData` is passed only to the Service uninstaller because ProgramData belongs to the machine-level Agent Service identity, license and Master binding.

## Agent Service

The production Windows Service uses one executable for both console development mode and service mode.

Windows Service identity:

- Service Name: `GaltekClassroomAgent`.
- Display Name: `Galtek Classroom Agent Service`.
- Account: `LocalSystem`.
- Startup Type: `Automatic`.
- Recovery: restart after failures with 5, 15 and 60 second delays.

Publish a self-contained Windows x64 artifact from the repository root:

```powershell
.\installer\windows\publish-agent-service.ps1
```

Default artifact path:

```text
artifacts\windows\agent-service\
```

Install or update from an elevated PowerShell session:

```powershell
.\installer\windows\install-agent-service.ps1
```

Installed binaries:

```text
%ProgramFiles%\Galtek\Classroom\Agent\
```

Persistent machine data:

```text
%ProgramData%\Galtek\Classroom\
```

Program Files contains binaries only. ProgramData contains `installation.json`, `license.dat`, `master-binding.json`, and future persistent Agent data. Updating Service binaries must not delete or regenerate Installation Identity, Master binding, `Session\` or `CredentialProvider\`.

Verify service state:

```powershell
Get-Service GaltekClassroomAgent
Get-CimInstance Win32_Service -Filter "Name='GaltekClassroomAgent'" |
    Select-Object Name, State, StartMode, StartName, PathName
```

Lifecycle commands:

```powershell
Stop-Service GaltekClassroomAgent
Start-Service GaltekClassroomAgent
Restart-Service GaltekClassroomAgent
```

Uninstall from an elevated PowerShell session:

```powershell
.\installer\windows\uninstall-agent-service.ps1
```

Normal Service-only uninstall removes the Windows Service registration and Service-owned binaries, but preserves `%ProgramData%\Galtek\Classroom\`, `Agent\Session\` and `Agent\CredentialProvider\`.

To intentionally remove Installation Identity, Commercial License and Master Windows Binding:

```powershell
.\installer\windows\uninstall-agent-service.ps1 -PurgeData
```

`-PurgeData` requires explicit administrator intent. The next installation will require a new activation and Master reconfiguration.

## Local IPC API v1

Local IPC v1 is documented in `protocol/local-ipc-v1.md`.

- Pipe name: `GaltekClassroom.Agent.v1`.
- Framing: 4-byte BIG ENDIAN length prefix plus UTF-8 JSON.
- Max message payload: 64 KiB.
- Operations: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`.
- Scope: read-only.

## Session Agent

Production Session Agent identity:

- Scheduled Task: `GaltekClassroomSessionAgent`.
- Display/description: `Galtek Classroom Session Agent`.
- Trigger: `AtLogon`.
- Principal: Builtin Users SID `S-1-5-32-545`.
- Run level: `Limited`.
- Task action: `GaltekClassroom.Agent.Session.exe --background`.
- Installed path: `%ProgramFiles%\Galtek\Classroom\Agent\Session\`.
- Multiple instances policy: `Parallel`.

The executable is compiled as `WinExe` so Task Scheduler autostart does not show a console window. The process still exposes one-shot diagnostics through `--ipc-ping` and `--ipc-status`; in development, invoke the `.dll` with `dotnet` to capture output directly.

Publish only the Session Agent:

```powershell
.\installer\windows\publish-agent-session.ps1
```

Install or update only the Session Agent from an elevated PowerShell session:

```powershell
.\installer\windows\install-session-agent.ps1
```

Verify the scheduled task:

```powershell
Get-ScheduledTask -TaskName GaltekClassroomSessionAgent
```

Verify the running process and its session:

```powershell
Get-CimInstance Win32_Process -Filter "Name='GaltekClassroom.Agent.Session.exe'" |
    Select-Object ProcessId, SessionId, ExecutablePath, CommandLine
```

`SessionId` should match the interactive Windows session and should not be `0` during normal operation.

Uninstall only the Session Agent:

```powershell
.\installer\windows\uninstall-session-agent.ps1
```

The Session uninstaller removes the scheduled task and `%ProgramFiles%\Galtek\Classroom\Agent\Session\`, but does not touch ProgramData.

## Credential Provider

The native Windows Credential Provider is installed machine-wide and x64 only. Product registration is handled by elevated installer scripts, never by the DLL or Agent runtime.

Publish only the Credential Provider:

```powershell
.\installer\windows\publish-credential-provider.ps1
```

Validate the package without elevation:

```powershell
.\installer\windows\test-credential-provider-package.ps1
```

Install/update and verify from elevated 64-bit PowerShell:

```powershell
.\installer\windows\install-credential-provider.ps1
.\installer\windows\test-credential-provider-installation.ps1
```

Uninstall:

```powershell
.\installer\windows\uninstall-credential-provider.ps1
```

The DLL is staged side-by-side under `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\versions\<packageId>\`, and HKLM x64 `InprocServer32` points to the active immutable DLL. Galtek never registers a Credential Provider Filter and never hides standard Windows providers. Real logon/switch validation belongs to a disposable lab PC checklist, not to development-machine install.

Diagnostic commands:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-ping
C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-status
```

## Machine Code

Print the development Machine Code and exit without starting the Worker:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --machine-code
```

Machine Code does not require an active Commercial License. It exists so a new installation can be identified before activation.

## Commercial License

Galtek Classroom does not generate commercial licenses. Licenses are JWTs issued externally by Galtek Hub and signed with RSA / RS256. The Agent Service validates them locally with a configured public key.

Required development public key variable:

```powershell
$env:GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH = "C:\path\to\galtek-hub-public-key.pem"
```

This path is a development/configuration mechanism only. It is not dynamic key download, and the Agent never trusts a public key shipped with or received beside a JWT. The production public key is expected to be distributed with the product in a future packaging step.

Query license status:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --license-status
```

Activate from STDIN, avoiding command-line history and process listings:

```powershell
cd agent
Get-Content .\dev-license.jwt -Raw | C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --activate-license
```

Activate from an explicit file:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --activate-license-file .\dev-license.jwt
```

The CLI prints JSON status and never prints the JWT. If no `license.dat` exists, status is `ACTIVATION_REQUIRED`. If a license exists but no public key is configured, status is `LICENSE_KEY_NOT_CONFIGURED`.

`features` are parsed and retained in `LicenseState`, but enforcement is intentionally deferred to the functionality that will consume each feature.
