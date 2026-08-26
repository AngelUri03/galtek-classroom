# Galtek Classroom

Galtek Classroom is a LAN-first classroom and cybercafe administration product for Windows environments. The product will let one or more Master computers supervise authorized Client computers in a local network, while keeping commercial licensing, network trust, and device identity as separate concerns.

The current iteration implements local Installation Identity, development Machine Code output, local Commercial License validation, and Local IPC API v1 for read-only status queries. It does not implement remote control, discovery, pairing, screen capture, projection, network transport, or the future desktop UI.

## Architecture

- `master-backend/`: Java 21, Spring Boot 3.x, Maven backend for the Master application.
- `agent/`: C#/.NET solution for Windows Agent components.
- `agent/src/GaltekClassroom.Agent.Service/`: Worker Service / Generic Host; owns Installation Identity and Commercial License locally, and hosts Local IPC API v1.
- `agent/src/GaltekClassroom.Agent.Session/`: console Session Agent with Local IPC status/ping commands.
- `agent/src/GaltekClassroom.Agent.Shared/`: shared constants, Installation Identity, Machine Code, Commercial License state models, and Local IPC contracts/framing.
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`: automated tests for Installation Identity, Machine Code, Commercial License, and Local IPC behavior.
- `protocol/`: cross-language protocol notes, including `protocol/local-ipc-v1.md`.
- `docs/`: project context, architecture notes, decisions, and handoff state.

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

If the Agent Service is not available, `/api/device/status` and `/api/device/machine-code` return HTTP `503` with code `LOCAL_AGENT_UNAVAILABLE`. `/api/system/health` only reports Master Backend health and does not depend on the Agent Service.

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

Run the Session Agent:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj
```

Query the Agent Service from the Session Agent through IPC:

```powershell
cd agent
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj -- --ipc-status
C:\Users\angel\.dotnet\dotnet.exe run --no-build --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj -- --ipc-ping
```

## Local IPC API v1

Local IPC v1 is documented in `protocol/local-ipc-v1.md`.

- Pipe name: `GaltekClassroom.Agent.v1`.
- Framing: 4-byte BIG ENDIAN length prefix plus UTF-8 JSON.
- Max message payload: 64 KiB.
- Operations: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`.
- Scope: read-only.

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
