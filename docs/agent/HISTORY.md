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

## 2026-08-25 - Prompt 05

### Realizado

- Convertido `GaltekClassroom.Agent.Service` en Windows Service instalable con nombre estable `GaltekClassroomAgent`.
- Conservado el modo consola de desarrollo y los modos CLI `--machine-code`, `--license-status`, `--activate-license` y `--activate-license-file`.
- Confirmada la integracion oficial `Microsoft.Extensions.Hosting.WindowsServices`.
- Agregados scripts PowerShell para publicar self-contained `win-x64`, instalar/actualizar y desinstalar.
- Separados binarios en Program Files y datos persistentes en ProgramData.
- Configurados startup `Automatic`, cuenta `LocalSystem` y recovery del servicio.
- Agregada desinstalacion que conserva ProgramData por defecto y `-PurgeData` explicito.
- Agregado versionado inicial del ejecutable en el `.csproj` del Service.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Shared/ProductInfo.cs`
- `agent/src/GaltekClassroom.Agent.Service/GaltekClassroom.Agent.Service.csproj`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/ProductInfoTests.cs`
- `installer/windows/`
- `.gitignore`
- `README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- Service Name: `GaltekClassroomAgent`.
- Display Name: `Galtek Classroom Agent Service`.
- Cuenta: `LocalSystem`.
- Startup type: `Automatic`.
- Recovery: restart con retrasos de 5, 15 y 60 segundos, reset en 86400 segundos.
- Runtime publicado inicial: `win-x64`, self-contained, sin single-file.
- Ruta de binarios: `<ProgramFiles>\Galtek\Classroom\Agent\`.
- Ruta de datos: `<CommonApplicationData>\Galtek\Classroom\`.
- Upgrade y uninstall normal conservan Installation Identity y Commercial License.

### Cambios descartados

- No se implementaron Session Agent autostart, scheduled tasks, Run registry, launcher interactivo desde Session 0, MSI, installer grafico, IPC write, activacion por IPC, gRPC, mTLS, mDNS, pairing, certificados, comandos remotos, UI, SQLite ni Galtek Hub.

### Pendiente

- Definir lifecycle/autostart del Session Agent al inicio de sesion del usuario.
- Empaquetar la public key productiva de Galtek Hub.
- Disenar autorizacion local antes de cualquier IPC write.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 42 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 14 pruebas superadas.
- Parser PowerShell de `installer/windows/*.ps1`: correcto.
- `.\installer\windows\publish-agent-service.ps1`: correcto; genero artifact `Release`, `win-x64`, self-contained.
- Artifact publicado contiene `.exe` y dependencias, y `artifacts/` esta ignorado por Git.
- `.exe` publicado ejecutado con `GALTEK_CLASSROOM_DATA_DIR` temporal: correcto, IPC responde `UP` y estado `ACTIVATION_REQUIRED`.
- CLI del `.exe` publicado: `--machine-code` y `--license-status` correctos.
- Validacion alternativa Master Backend contra `.exe` publicado en modo consola: health correcto, device status correcto tras reintento, machine-code correcto.
- `install-agent-service.ps1` y `uninstall-agent-service.ps1` validan elevacion y fallan temprano en entorno no elevado.
- Validacion real del Service Control Manager pendiente por falta de elevacion del entorno.

### Commit sugerido

`feat(agent): install service as windows service`

## 2026-08-25 - Prompt 06

### Realizado

- Implementado lifecycle background real de `GaltekClassroom.Agent.Session`.
- Agregado modo `--background`; sin argumentos tambien entra en modo background.
- Convertido Session Agent productivo a `WinExe` para autostart sin consola visible.
- Conservados `--ipc-ping` y `--ipc-status` como comandos one-shot.
- Agregado single instance por sesion con named mutex local `Local\GaltekClassroom.Agent.Session`.
- Agregado supervisor IPC con `PING`, `GET_DEVICE_STATUS`, backoff acotado y polling saludable.
- Agregada publicacion self-contained `Release/win-x64` del Session Agent.
- Agregados scripts de instalacion/desinstalacion del Session Agent con Task Scheduler.
- Agregados orquestadores thin para publicar, instalar y desinstalar el Agent completo.
- Ajustados scripts del Service para no borrar `Agent\Session\` en updates/desinstalaciones especificas del Service.
- Agregado proyecto `GaltekClassroom.Agent.Session.Tests`.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Session/`
- `agent/src/GaltekClassroom.Agent.Shared/ProductInfo.cs`
- `agent/tests/GaltekClassroom.Agent.Session.Tests/`
- `agent/GaltekClassroom.Agent.sln`
- `installer/windows/`
- `README.md`
- `installer/windows/README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- Usar Windows Task Scheduler con trigger `AtLogon` para iniciar el Session Agent en la sesion interactiva.
- Registrar la tarea como `GaltekClassroomSessionAgent`.
- Usar principal de grupo `S-1-5-32-545` (Builtin Users), `RunLevel Limited` y sin credenciales guardadas.
- No lanzar Session Agent desde el Windows Service ni usar APIs de token de Session 0.
- Usar `MultipleInstances Parallel` en Task Scheduler para no bloquear escenarios multi-session futuros.
- Delegar duplicados al mutex local por sesion.
- Mantener IPC v1 read-only sin agregar operaciones nuevas.
- Mantener el Session Agent vivo aunque el Service no este disponible o la licencia no este activa.

### Cambios descartados

- No se implementaron captura, bloqueo, overlays, tray icon, UI, comandos remotos, IPC write, gRPC, mTLS, mDNS, pairing ni Galtek Hub.
- No se instalaron binarios en AppData, Desktop, Startup folder ni repositorio.
- No se agregaron wrappers VBS ni scripts ocultos para el autostart.
- No se agrego una plataforma pesada de logging.

### Pendiente

- Validacion real de Task Scheduler en entorno elevado.
- Validacion visual real del autostart automatico sin ventana en una sesion de Windows instalada.
- Definir contrato futuro Service <-> Session para identificar `sessionId`, usuario y estado interactive/active.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln`: correcto, 56 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 14 pruebas superadas.
- Parser PowerShell de `installer/windows/*.ps1`: correcto.
- `.\installer\windows\publish-agent-session.ps1`: correcto; genero artifact `Release`, `win-x64`, self-contained.
- `.\installer\windows\publish-agent.ps1`: correcto; publico Service y Session.
- Artifact Session contiene `.exe` y dependencias; `artifacts/` esta ignorado por Git.
- `install-session-agent.ps1` y `uninstall-session-agent.ps1` fallan temprano con mensaje claro en entorno no elevado.
- Prueba manual de mutex: primera instancia `--background` queda viva, segunda termina, `SessionId = 12`.
- Prueba local de restart: Session Agent sigue vivo al detener el Service y sigue vivo tras iniciar nuevamente el Service.
- `--ipc-ping` con Service arriba devuelve `UP`.
- `--ipc-status` con Service arriba devuelve estado seguro `ACTIVATION_REQUIRED`.

### Commit sugerido

`feat(agent): add session agent lifecycle`

## 2026-08-25 - Prompt 07

### Realizado

- Formalizado el dominio funcional completo del Master Backend sin persistencia ni ejecucion remota.
- Agregados modelos de `Classroom`, `Device`, `SchoolGroup`, `Student`, `DeviceAssignment`, `StudentWorkspace`, `BrowserProfile`, `MasterBrowserProfile`, `ApplicationDefinition`, `MasterWindowsBinding` y operaciones batch.
- Agregado catalogo de acciones tipadas, conflict policies, estados de workflow, batch statuses, target statuses y preflight statuses.
- Agregado modelo central de errores operacionales con categoria y bandera retryable.
- Agregados planners puros `DeviceAssignmentPolicy`, `StudentMovePlanner`, `StudentSwapPlanner` y `BatchOperationPlanner`.
- Agregada validacion conservadora de URL para `OPEN_URL`.
- Agregado `CurrentWindowsIdentityProvider` y policy de autorizacion Master por rol `MASTER`, installationId y SID ligado.
- Agregados contratos C# Shared minimos para futuras operaciones tipadas, destinos logicos y errores, sin agregar IPC write.
- Creado `docs/context/FUNCTIONAL_MODEL.md`.

### Archivos principales modificados

- `master-backend/src/main/java/com/galtek/classroom/classroom/`
- `master-backend/src/main/java/com/galtek/classroom/device/`
- `master-backend/src/main/java/com/galtek/classroom/student/`
- `master-backend/src/main/java/com/galtek/classroom/workspace/`
- `master-backend/src/main/java/com/galtek/classroom/browser/`
- `master-backend/src/main/java/com/galtek/classroom/application/`
- `master-backend/src/main/java/com/galtek/classroom/operations/`
- `master-backend/src/main/java/com/galtek/classroom/master/`
- `master-backend/src/test/java/com/galtek/classroom/`
- `agent/src/GaltekClassroom.Agent.Shared/OperationContracts.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/OperationContractsTests.cs`
- `docs/context/FUNCTIONAL_MODEL.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/context/PROJECT_CONTEXT.md`
- `docs/context/ARCHITECTURE.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `README.md`

### Decisiones tomadas

- `Device != Student`.
- `StudentWorkspace` pertenece al alumno.
- Operaciones futuras deben ser batch-first, con `PARTIAL_SUCCESS` y retry de fallidos.
- Master Windows Binding usa Windows SID; username/admin no son seguridad.
- La autoridad final del binding debe vivir en Agent Service; Master Backend consume estado derivado por IPC futuro.
- Browser portability no copia passwords, cookies ni cache protegido.
- Operaciones de contenido usan destinos logicos, no rutas arbitrarias.
- Move/swap requieren preflight y workflow `COPY -> VERIFY -> COMMIT -> CLEANUP`.
- `TARGET_OCCUPIED` nunca sobrescribe ni borra al alumno existente.
- No se agrega IPC write en Prompt 07.

### Cambios descartados

- No se implementaron SQLite, tablas, migrations, filesystem real, transferencia de archivos, Chrome automation, cookies/passwords, wallpaper real, `OPEN_URL` remoto, `DISTRIBUTE_FILE` remoto, `CREATE_FOLDER` remoto, `MOVE_STUDENT` real, `SWAP_STUDENTS` real, locks reales, shutdown, gRPC, Protobuf, Network Identity, mTLS, pairing, mDNS, screen capture, WebRTC, React, Tauri ni UI.

### Pendiente

- Persistir el dominio funcional en SQLite.
- Definir IPC local write/autorizacion antes de operaciones privilegiadas.
- Persistir/verificar Master Windows Binding en Agent Service.
- Implementar filesystem real de StudentWorkspace y recovery en fases posteriores.

### Validaciones

- `mvn clean verify` en `master-backend`: correcto, 37 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 59 pruebas superadas.

### Commit sugerido

`feat(master): add classroom domain model`
