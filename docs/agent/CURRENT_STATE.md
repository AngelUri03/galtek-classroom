# Estado actual

## Ultima actualizacion

2026-08-25 - Prompt 01.

## Estado del proyecto

Fundacion inicial creada en un repositorio que estaba vacio. El producto todavia no tiene funciones operativas de administracion remota.

## Implementado

- Backend Master minimo en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`.
- Prueba automatica del health endpoint.
- Solucion .NET `GaltekClassroom.Agent.sln` con Service, Session y Shared.
- Agent Service minimo con Worker Service / Generic Host.
- Session Agent minimo de consola.
- Directorio `protocol/` con README de alcance.
- Documentacion base de contexto, arquitectura, reglas, decisiones e historial.

## En progreso

- Ningun desarrollo activo dejado a medias.

## Pendiente inmediato

- Definir Prompt 02 antes de implementar protocolo, persistencia, UI o funciones remotas.
- Agregar pruebas .NET cuando exista comportamiento del Agent que validar.

## Cambios aceptados

- Separacion Master backend / Agent / Protocol / Docs.
- Agent separado en Windows Service y Session Agent.
- Service preparado para ejecutarse en consola durante desarrollo.

## Cambios rechazados / No repetir

- No implementar ejecucion remota arbitraria.
- No crear UI React/Tauri todavia.
- No implementar licencias, gRPC, mTLS, mDNS, pairing, captura o bloqueo en Prompt 01.

## Problemas conocidos

- El entorno no tenia .NET SDK instalado al inicio; se instalo .NET SDK 8.0.424 en `C:\Users\angel\.dotnet` para crear y validar la solucion.
- No existe proyecto de tests .NET todavia.
- El entorno tiene `DEBUG=release`; el backend fuerza `debug=false` salvo configuracion explicita por argumentos/JVM properties para evitar logs DEBUG accidentales.

## Pruebas ejecutadas

- `mvn clean verify` en `master-backend`: correcto, 1 prueba ejecutada.
- `mvn spring-boot:run` en `master-backend`: correcto, backend arranco en `http://localhost:8080`.
- `Invoke-RestMethod http://localhost:8080/api/system/health`: correcto, respondio `{"application":"Galtek Classroom Master","status":"UP"}`.
- `dotnet build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `dotnet test .\GaltekClassroom.Agent.sln` en `agent`: correcto; no hay proyectos de tests .NET todavia.
- `dotnet run --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj`: correcto, inicia en consola y se detiene limpiamente con Ctrl+C.
- `dotnet run --project .\src\GaltekClassroom.Agent.Session\GaltekClassroom.Agent.Session.csproj`: correcto, inicia y cierra limpiamente.

## Proximo paso recomendado

Definir el esqueleto del protocolo Protobuf minimo y los contratos internos iniciales sin implementar todavia operaciones remotas reales.
