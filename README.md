# Galtek Classroom

Galtek Classroom is a LAN-first classroom and cybercafe administration product for Windows environments. The product will let one or more Master computers supervise authorized Client computers in a local network, while keeping commercial licensing, network trust, and device identity as separate concerns.

This first iteration only creates the technical foundation. It does not implement remote control, discovery, licensing, pairing, screen capture, projection, or the future desktop UI.

## Architecture

- `master-backend/`: Java 21, Spring Boot 3.x, Maven backend for the Master application.
- `agent/`: C#/.NET solution for Windows Agent components.
- `agent/src/GaltekClassroom.Agent.Service/`: Worker Service / Generic Host prepared for future Windows Service execution.
- `agent/src/GaltekClassroom.Agent.Session/`: minimal console Session Agent for the interactive user session.
- `agent/src/GaltekClassroom.Agent.Shared/`: shared constants and future shared contracts.
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
mvn test
```

## Agent

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

Run the Session Agent:

```powershell
cd agent
dotnet run --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj
```

Run .NET tests when test projects exist:

```powershell
cd agent
dotnet test .\GaltekClassroom.Agent.sln
```
