# Estado actual

## Ultima actualizacion

2026-08-25 - Prompt 04.

## Estado del proyecto

Local IPC API v1 quedo implementada como canal read-only sobre Windows Named Pipes. `GaltekClassroom.Agent.Service` sigue siendo la autoridad local unica de Installation Identity y Commercial License, y ahora expone estado seguro y Machine Code a `GaltekClassroom.Agent.Session` y al Master Backend Java.

El producto todavia no tiene funciones operativas de administracion remota, UI, comunicacion de red, gRPC, mTLS, mDNS, pairing, captura, bloqueo ni comandos remotos.

## Implementado

- Backend Master minimo en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code`.
- Flujo Java `controller -> service -> LocalAgentClient -> Named Pipe transport`.
- Respuesta HTTP 503 con codigo `LOCAL_AGENT_UNAVAILABLE` cuando el Agent Service no esta disponible.
- Timeout razonable en el transporte Java IPC para evitar requests HTTP congeladas indefinidamente.
- Solucion .NET `GaltekClassroom.Agent.sln` con Service, Session y Shared.
- Agent Service con Worker Service / Generic Host.
- Installation Identity permanente del equipo en `installation.json`.
- Ubicacion de datos en `<CommonApplicationData>\Galtek\Classroom\` con override `GALTEK_CLASSROOM_DATA_DIR`.
- Hashes SHA-256 de CPU, motherboard, MAC fisicas y discos.
- Machine Code Base64 generado por la implementacion existente del Agent Service.
- Commercial License local en `GaltekClassroom.Agent.Service`.
- Persistencia separada de licencia en `license.dat`.
- Validacion JWT con `System.IdentityModel.Tokens.Jwt` y llave publica RSA.
- `LicenseState` en memoria sin exponer JWT completo.
- Monitor de expiracion runtime cada 60 segundos sin recalcular WMI.
- CLI de desarrollo: `--machine-code`, `--license-status`, `--activate-license` y `--activate-license-file`.
- Local IPC API v1 read-only.
- Named Pipe server en el Agent Service con pipe `GaltekClassroom.Agent.v1`.
- Contratos IPC compartidos en C# Shared.
- Framing IPC de 4 bytes BIG ENDIAN mas JSON UTF-8.
- Limite de payload IPC de 64 KiB.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`.
- `GET_DEVICE_STATUS` expone solo campos seguros: producto, installationId, hostname, estado de licencia, roles y features.
- `GET_MACHINE_CODE` funciona aunque la licencia no este activa.
- Cliente IPC C# en Session Agent.
- Comandos Session Agent `--ipc-status` y `--ipc-ping`.
- Cliente IPC Java en Master Backend.
- Especificacion canonica `protocol/local-ipc-v1.md`.
- Proyecto xUnit `GaltekClassroom.Agent.Service.Tests`.
- Pruebas Java para health, device endpoints, framing y cliente IPC.
- Documentacion de contexto, arquitectura, decisiones e historial.

## En progreso

- Ningun desarrollo activo dejado a medias dentro del Prompt 04.

## Pendiente inmediato

- Preparar el siguiente prompt para packaging/instalacion o para la siguiente capacidad planificada, sin mezclarlo con IPC v1.
- Definir como se empaquetara la llave publica real de Galtek Hub para produccion.

## Cambios aceptados

- Separacion Master backend / Agent / Protocol / Docs.
- Agent separado en Windows Service y Session Agent.
- Agent Service como autoridad local de Installation Identity.
- Agent Service como autoridad local de Commercial License.
- Agent Service como servidor unico de IPC local.
- Master Backend no lee archivos locales del Agent.
- Master Backend no implementa JWT, hardware fingerprint, Installation Identity ni licencia.
- IPC v1 es read-only y no habilita control remoto.
- Acceso al pipe no equivale a autorizacion para operaciones privilegiadas futuras.

## Cambios rechazados / No repetir

- No implementar ejecucion remota arbitraria.
- No implementar activacion de licencia por IPC en Prompt 04.
- No implementar operaciones write por IPC en Prompt 04.
- No crear UI React/Tauri todavia.
- No implementar Galtek Hub, generacion de licencias, private keys comerciales, activacion online, revocacion online, descarga de public key, DPAPI, gRPC, mTLS, mDNS, pairing, captura, bloqueo, comandos remotos, SQLite ni WebRTC en Prompt 04.
- No agregar Commercial License ni llaves publicas al Master Backend.
- No confiar en una llave publica enviada junto con un JWT.
- No mezclar Commercial License dentro de `installation.json`.

## Problemas conocidos

- El `dotnet` del PATH global apunta solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- El entorno tiene `DEBUG=release`; el backend fuerza `debug=false` salvo configuracion explicita por argumentos/JVM properties para evitar logs DEBUG accidentales.
- No existe todavia una llave publica real de Galtek Hub empaquetada; sin `GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH`, una licencia existente queda en `LICENSE_KEY_NOT_CONFIGURED`.
- `license.dat` no se cifra localmente en esta fase. Su integridad depende de la firma RS256, `installationId` y hardware 3 de 4; DPAPI/ACL hardening queda pendiente.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 41 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 14 pruebas superadas.
- Prueba manual Agent Service con `GALTEK_CLASSROOM_DATA_DIR` temporal: correcto, arranca con `ACTIVATION_REQUIRED` y pipe `GaltekClassroom.Agent.v1`.
- Prueba manual Session Agent `--ipc-status`: correcto, obtiene estado real del Service.
- Prueba manual Session Agent `--ipc-ping`: correcto, devuelve `UP`.
- Prueba manual Master Backend `GET /api/device/status`: correcto, devuelve estado real del Agent Service por IPC.
- Prueba manual Master Backend `GET /api/device/machine-code`: correcto, devuelve Machine Code producido por el Agent Service.
- Prueba manual con Agent Service detenido: correcto, `GET /api/device/status` y `GET /api/device/machine-code` devuelven HTTP 503 con `LOCAL_AGENT_UNAVAILABLE`.
- Prueba manual con Agent Service detenido: correcto, `GET /api/system/health` sigue `UP`.

## Proximo paso recomendado

Definir el siguiente prompt para packaging/instalacion del Agent Service como Windows Service, manteniendo IPC v1 read-only sin agregar comandos write.
