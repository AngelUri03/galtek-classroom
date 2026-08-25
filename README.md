# Galtek Classroom

Galtek Classroom is a LAN-first classroom and cybercafe administration product for Windows environments. The product will let one or more Master computers supervise authorized Client computers in a local network, while keeping commercial licensing, network trust, and device identity as separate concerns.

The current iteration implements the local Installation Identity for the Windows Agent and a development-only Machine Code output. It does not implement remote control, discovery, license validation, pairing, screen capture, projection, or the future desktop UI.

## Architecture

- `master-backend/`: Java 21, Spring Boot 3.x, Maven backend for the Master application.
- `agent/`: C#/.NET solution for Windows Agent components.
- `agent/src/GaltekClassroom.Agent.Service/`: Worker Service / Generic Host prepared for future Windows Service execution; owns local Installation Identity.
- `agent/src/GaltekClassroom.Agent.Session/`: minimal console Session Agent for the interactive user session.
- `agent/src/GaltekClassroom.Agent.Shared/`: shared constants and identity/Machine Code models.
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`: automated tests for Installation Identity and Machine Code behavior.
- `protocol/`: future Protobuf contract shared by Java and C#.
- `docs/`: project context, architecture notes, decisions, and handoff state.

## Development Requirements

- Windows development environment.
- Java 21.
- Maven 3.9+.
- .NET SDK 8.0 LTS or newer compatible LTS SDK.

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

Run tests:

```powershell
cd master-backend
mvn clean verify
```

## Agent

The Agent stores machine-level data in:

```text
<CommonApplicationData>\Galtek\Classroom\
```

For development and tests, override that directory with:

```powershell
$env:GALTEK_CLASSROOM_DATA_DIR = "$PWD\.local-agent-data"
```

Build the full Agent solution:

```powershell
cd agent
dotnet build .\GaltekClassroom.Agent.sln
```

Run the Agent Service in development console mode:

```powershell
cd agent
dotnet run --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj
```

Print the development Machine Code and exit without starting the Worker:

```powershell
cd agent
dotnet run --no-build --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --machine-code
```

Run the Session Agent:

```powershell
cd agent
dotnet run --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj
```

Run .NET tests:

```powershell
cd agent
dotnet test .\GaltekClassroom.Agent.sln
```
