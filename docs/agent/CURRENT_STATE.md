# Estado actual

## Ultima actualizacion

2026-08-25 - Prompt 02.

## Estado del proyecto

Installation Identity local implementada en el Agent Service. El producto todavia no tiene funciones operativas de administracion remota ni validacion comercial de licencias.

## Implementado

- Backend Master minimo en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`.
- Prueba automatica del health endpoint.
- Solucion .NET `GaltekClassroom.Agent.sln` con Service, Session y Shared.
- Agent Service con Worker Service / Generic Host.
- Installation Identity permanente del equipo en `installation.json`.
- Ubicacion de datos en `<CommonApplicationData>\Galtek\Classroom\` con override `GALTEK_CLASSROOM_DATA_DIR`.
- Hashes SHA-256 de CPU, motherboard, MAC fisicas y discos.
- Normalizacion determinista antes de hashear.
- Manejo explicito de `installation.json` corrupto o incompleto sin regeneracion silenciosa.
- Modo de desarrollo `--machine-code` que imprime Machine Code Base64 y finaliza.
- Session Agent minimo de consola.
- Proyecto xUnit `GaltekClassroom.Agent.Service.Tests`.
- Directorio `protocol/` con README de alcance.
- Documentacion base de contexto, arquitectura, reglas, decisiones e historial.

## En progreso

- Ningun desarrollo activo dejado a medias.

## Pendiente inmediato

- Definir Prompt 03 sin implementar todavia licencia, gRPC, mTLS, mDNS, pairing, IPC ni UI.
- Definir como exponer el estado local del Service a otros procesos cuando llegue el IPC confiable.

## Cambios aceptados

- Separacion Master backend / Agent / Protocol / Docs.
- Agent separado en Windows Service y Session Agent.
- Service preparado para ejecutarse en consola durante desarrollo.
- Agent Service como autoridad local de Installation Identity.
- Commercial License futura tambien centralizada en Agent Service.

## Cambios rechazados / No repetir

- No implementar ejecucion remota arbitraria.
- No crear UI React/Tauri todavia.
- No implementar licencias, JWT, activacion, gRPC, mTLS, mDNS, pairing, captura o bloqueo en Prompt 02.

## Problemas conocidos

- El `dotnet` del PATH global apunta solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- El entorno tiene `DEBUG=release`; el backend fuerza `debug=false` salvo configuracion explicita por argumentos/JVM properties para evitar logs DEBUG accidentales.

## Pruebas ejecutadas

- `dotnet build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `dotnet test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 7 pruebas superadas.
- `dotnet run --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj` en `agent` con `GALTEK_CLASSROOM_DATA_DIR` temporal: correcto, crea `installation.json` y arranca.
- Segunda ejecucion del Agent Service con el mismo directorio temporal: correcto, conserva el mismo `installationId`.
- Modo Machine Code con `--machine-code` y directorio temporal: correcto, Base64 decodifica a JSON con producto, schema, `installationId`, cuatro hashes y hostname.
- Revision manual de `installation.json`: correcto, solo contiene hashes de 64 caracteres y no contiene licencia ni IP.
- `mvn clean verify` en `master-backend`: correcto, 1 prueba ejecutada.

## Proximo paso recomendado

Implementar IPC local minimo y confiable para consultar desde otros procesos el estado de Installation Identity y Machine Code sin agregar todavia Commercial License ni red.
