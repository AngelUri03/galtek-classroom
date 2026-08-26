# Historial

## 2026-08-25 - Prompt 01

### Realizado

- Inicializada la estructura base del repositorio.
- Creado backend Master minimo con Spring Boot.
- Creada solucion .NET del Agent con Service, Session y Shared.
- Creado directorio `protocol/` con README de alcance.
- Creada memoria de agentes y README principal.

### Archivos principales modificados

- `README.md`
- `.gitignore`
- `master-backend/`
- `agent/`
- `protocol/README.md`
- `docs/context/`
- `docs/agent/`

### Decisiones tomadas

- Usar Spring Boot 3.5.16 para cumplir Spring Boot 3.x.
- Usar .NET 8 LTS para los proyectos del Agent.
- No agregar proyecto de tests .NET en Prompt 01 porque aun no hay comportamiento interno relevante fuera del arranque minimo.

### Cambios descartados

- No se implementaron licencias, gRPC, mTLS, mDNS, SQLite, React, Tauri ni funciones remotas.
- No se creo un `.proto` minimo para evitar fijar contratos prematuros.

### Pendiente

- Definir protocolo Protobuf inicial.
- Definir contratos IPC Service-Session.
- Agregar pruebas .NET cuando haya comportamiento validable.

### Validaciones

- `mvn clean verify` en `master-backend`: correcto, 1 prueba ejecutada.
- `mvn spring-boot:run` + `GET /api/system/health`: correcto.
- `dotnet build .\GaltekClassroom.Agent.sln`: correcto, 0 advertencias, 0 errores.
- `dotnet test .\GaltekClassroom.Agent.sln`: correcto; no habia proyectos de tests .NET.
- Agent Service ejecutado en modo consola y detenido limpiamente.
- Session Agent ejecutado y cerrado limpiamente.

### Commit sugerido

`chore: initialize Galtek Classroom architecture`

## 2026-08-25 - Prompt 02

### Realizado

- Implementada Installation Identity permanente en `GaltekClassroom.Agent.Service`.
- Agregado `installation.json` con `schemaVersion`, `installationId`, cuatro hashes de hardware y `createdAtUtc`.
- Agregada generacion de Machine Code Base64 para desarrollo con `--machine-code`.
- Agregado proyecto xUnit para tests del Agent Service.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Shared/`
- `agent/src/GaltekClassroom.Agent.Service/`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`
- `agent/GaltekClassroom.Agent.sln`
- `README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`

### Decisiones tomadas

- `GaltekClassroom.Agent.Service` queda como autoridad local unica de Installation Identity.
- El directorio productivo es `<CommonApplicationData>\Galtek\Classroom\` y puede reemplazarse con `GALTEK_CLASSROOM_DATA_DIR`.
- Se usa `System.Management` para CPU, motherboard y disco en Windows; MAC se obtiene con `NetworkInterface`.
- Si `installation.json` esta corrupto o incompleto no se regenera silenciosamente.

### Cambios descartados

- No se implementaron JWT, activacion, Commercial License, gRPC, mTLS, mDNS, pairing, IPC, UI ni comandos remotos.

### Pendiente

- Exponer estado local mediante IPC confiable en una iteracion futura.
- Implementar Commercial License solo cuando Installation Identity ya sea consumible por el resto del sistema.

### Validaciones

- `dotnet build .\GaltekClassroom.Agent.sln`: correcto, 0 advertencias, 0 errores.
- `dotnet test .\GaltekClassroom.Agent.sln`: correcto, 7 pruebas superadas.
- Agent Service ejecutado dos veces con `GALTEK_CLASSROOM_DATA_DIR` temporal: correcto, conserva `installationId`.
- Machine Code generado y decodificado: correcto, contiene payload esperado sin licencia ni IP.
- `mvn clean verify` en `master-backend`: correcto, 1 prueba ejecutada.

### Commit sugerido

`feat(agent): add installation identity and machine code`

## 2026-08-25 - Prompt 03

### Realizado

- Implementada Commercial License local en `GaltekClassroom.Agent.Service`.
- Agregada validacion JWT RS256 con llave publica RSA configurable para desarrollo.
- Agregada persistencia separada en `license.dat`.
- Agregados estados internos de licencia, `LicenseState`, roles y features extensibles.
- Agregada activacion/renovacion con validacion previa al reemplazo.
- Agregado monitor de expiracion runtime cada 60 segundos.
- Agregados comandos CLI `--license-status`, `--activate-license` y `--activate-license-file`.
- Ampliadas pruebas automatizadas del Agent Service.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Shared/`
- `agent/src/GaltekClassroom.Agent.Service/`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`
- `.gitignore`
- `README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`

### Decisiones tomadas

- El Agent Service queda como autoridad local unica de Commercial License.
- El Master Backend no valida JWT ni almacena llave publica.
- Solo se acepta `RS256`; otros algoritmos se bloquean.
- La licencia exige issuer `galtek-hub`, audience `galtek-classroom`, product `GALTEK_CLASSROOM` y schema 1.
- La licencia debe coincidir con `installationId` y con hardware 3 de 4.
- `license.dat` guarda solo el JWT y no se cifra todavia.
- La expiracion debe cambiar el estado en runtime sin reinicio.

### Cambios descartados

- No se implementaron Galtek Hub, generacion comercial de licencias, private keys, activacion online, revocacion online, DPAPI, IPC, red, UI ni enforcement de features.

### Pendiente

- Implementar IPC local confiable para consultar `LicenseState`.
- Empaquetar la llave publica real de Galtek Hub para produccion.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln`: correcto, 23 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 1 prueba superada.
- `--license-status` sin `license.dat`: correcto, `ACTIVATION_REQUIRED`.
- `--machine-code` sin Commercial License: correcto.
- Activacion manual con licencia de desarrollo firmada por RSA efimera: correcto, `ACTIVE`.
- Segunda consulta de estado con la licencia persistida: correcto, `ACTIVE`.
- JWT manipulado: correcto, `LICENSE_TAMPERED`.
- JWT expirado: correcto, `LICENSE_EXPIRED`.
- Activaciones invalidas no reemplazaron la licencia valida anterior.

### Commit sugerido

`feat(agent): add commercial license validation`

## 2026-08-25 - Prompt 04

### Realizado

- Implementada Local IPC API v1 read-only sobre Windows Named Pipes.
- Agregados contratos IPC compartidos, errores, operaciones y framing en C# Shared.
- Agregado Named Pipe server en `GaltekClassroom.Agent.Service`.
- Agregado estado runtime para exponer Installation Identity segura al handler IPC.
- Agregado handler IPC para `PING`, `GET_DEVICE_STATUS` y `GET_MACHINE_CODE`.
- Agregado cliente IPC en `GaltekClassroom.Agent.Session`.
- Agregados comandos Session Agent `--ipc-status` y `--ipc-ping`.
- Agregado cliente IPC Java en Master Backend sin duplicar JWT, hardware, Installation Identity ni licencia.
- Agregados endpoints `GET /api/device/status` y `GET /api/device/machine-code`.
- Agregado mapeo HTTP 503 `LOCAL_AGENT_UNAVAILABLE` cuando el Agent Service no esta disponible.
- Creada especificacion canonica `protocol/local-ipc-v1.md`.
- Ampliadas pruebas .NET y Java de framing, protocolo, resiliencia, cliente y endpoints.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Shared/LocalIpcProtocol.cs`
- `agent/src/GaltekClassroom.Agent.Shared/LocalIpcContracts.cs`
- `agent/src/GaltekClassroom.Agent.Shared/LocalIpcFraming.cs`
- `agent/src/GaltekClassroom.Agent.Service/Ipc/`
- `agent/src/GaltekClassroom.Agent.Service/Runtime/AgentRuntimeState.cs`
- `agent/src/GaltekClassroom.Agent.Session/Ipc/LocalAgentIpcClient.cs`
- `master-backend/src/main/java/com/galtek/classroom/localagent/`
- `master-backend/src/main/java/com/galtek/classroom/device/`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`
- `master-backend/src/test/java/com/galtek/classroom/`
- `protocol/local-ipc-v1.md`
- `README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/PROJECT_CONTEXT.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`

### Decisiones tomadas

- Local IPC v1 usa Windows Named Pipes.
- El Agent Service es el servidor unico del pipe `GaltekClassroom.Agent.v1`.
- El framing es prefijo de longitud de 4 bytes BIG ENDIAN mas JSON UTF-8.
- `protocolVersion = 1`.
- El limite de payload JSON es 64 KiB.
- IPC v1 es read-only.
- Las unicas operaciones permitidas son `PING`, `GET_DEVICE_STATUS` y `GET_MACHINE_CODE`.
- Master Backend no lee archivos locales del Agent ni valida JWT.
- La ACL actual del pipe permite consultas locales desde usuarios autenticados, pero no autoriza operaciones privilegiadas futuras.

### Cambios descartados

- No se implementaron activacion por IPC, comandos write, gRPC, Protobuf, mDNS, mTLS, pairing, Network Identity, UI, bloqueo, apagado, apertura de aplicaciones, proyeccion, captura, WebRTC, SQLite ni ejecucion remota arbitraria.

### Pendiente

- Empaquetar la llave publica real de Galtek Hub para produccion.
- Instalar/empaquetar el Agent Service como Windows Service en una fase posterior.
- Disenar autorizacion local antes de cualquier operacion IPC write.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln`: correcto, 41 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 14 pruebas superadas.
- Agent Service con `GALTEK_CLASSROOM_DATA_DIR` temporal: correcto, pipe iniciado y estado `ACTIVATION_REQUIRED`.
- Session Agent `--ipc-status`: correcto, obtuvo estado real del Agent Service.
- Session Agent `--ipc-ping`: correcto, `UP`.
- Master Backend `GET /api/device/status`: correcto, devolvio estado real por IPC.
- Master Backend `GET /api/device/machine-code`: correcto, devolvio Machine Code producido por el Agent Service.
- Agent Service detenido con Spring Boot activo: correcto, `GET /api/device/status` y `GET /api/device/machine-code` devolvieron HTTP 503 con `LOCAL_AGENT_UNAVAILABLE`.
- Master Backend `GET /api/system/health` con Agent Service detenido: correcto, `UP`.

### Commit sugerido

`feat(ipc): add local ipc api v1`
