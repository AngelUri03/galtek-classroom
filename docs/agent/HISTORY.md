# Historial

## 2026-09-06 - Prompt 19H1

### Realizado

- Agregado en Master Backend Java `POST /api/classrooms/{classroomId}/managed-accounts/switch` para dejar Devices explicitamente seleccionados en `PRIMARY` o `SECONDARY`.
- El endpoint usa `MasterAccessGuard` antes de request parsing efectivo, lectura escolar, storage de dominio, presencia, trust o dispatch remoto.
- La request es estricta: solo `targetAccountId` y `targetDeviceIds`; no acepta secretos, source account, timeout, force, group/all-classroom fanout ni payload libre.
- Cada invocacion crea una sola `BatchOperation` `SWITCH_MANAGED_ACCOUNT` y la persiste antes del primer `GET_WINDOWS_SESSION_STATE`.
- El payload durable contiene solo `schemaVersion` y `targetAccountId`.
- Agregado preflight local por target con pertenencia al aula, binding vigente, trust `PAIRED`, no `REVOKED`, conexion gRPC/mTLS autenticada `ONLINE`, `WINDOWS_SESSION_STATE_V1` y `WINDOWS_SESSION_SWITCH_V1`.
- `SESSION_AGENT_AVAILABLE`, readiness de cuenta, Credential Provider y credenciales quedan deferidos al Client.
- Agregado dispatch read-only `MasterRemoteOperationGateway.getWindowsSessionState(...)` usando la operacion Protobuf existente sin payload funcional.
- Integrado `ManagedAccountSwitchPlanner` con snapshots remotos reales: target activo produce `NO_CHANGE`, `NO_SESSION` planifica `LOGON`, opposite managed activo planifica `SWITCH`, `OTHER_SESSION_ACTIVE` bloquea como `WINDOWS_SESSION_CHANGED` y `UNKNOWN` como `WINDOWS_SESSION_UNKNOWN`.
- Para planes mutating, el Master envia siempre `SWITCH_MANAGED_ACCOUNT(target)` con operationId remoto propio; nunca encadena `LOGOFF_WINDOWS_SESSION + LOGON_MANAGED_ACCOUNT`.
- Agregada respuesta de batch con `summary.total/noChange/success/failed`, `retryable` por target y preservacion de errores estructurados del Agent.
- Agregada migracion SQLite para permitir `NO_CHANGE` en `batch_target_results`.
- Agregadas pruebas Java de controller/service batch, planner y gateway.
- Actualizados docs de arquitectura, modelo funcional, reglas, API, estado, decisiones, session switch y nuevo `docs/windows/MANAGED_ACCOUNT_SWITCH_BATCH.md`.

### Cambios descartados

- No se tocaron Agent .NET, Protobuf, C++ ni Credential Provider.
- No se agrego UI.
- No se implemento retry automatico, retry endpoint ni reconciliation de `SWITCH_MANAGED_ACCOUNT`.
- No se agrego provisioning, vault unlock, almacenamiento/envio de passwords, SID, username, sessionId, credentialId ni vault token.
- No se agrego switch por assignment/startup/recovery ni fanout implicito por grupo/aula.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=ManagedAccountSwitchPlannerTest,ManagedAccountSwitchDispatchControllerTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=MasterSqlitePersistenceIntegrationTest,BrowserDownloadPolicyPersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q test` en `master-backend`: correcto, 330 pruebas superadas.

### Commit sugerido

`feat(master): batch managed account switching`

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

## 2026-08-26 - Prompt 08

### Realizado

- Implementada persistencia SQLite local del dominio Master.
- Agregadas dependencias Spring JDBC, Flyway y SQLite JDBC.
- Agregada configuracion `galtek.classroom.master.storage.*`.
- Agregada resolucion de ruta del Master a `<CommonApplicationData>\Galtek\Classroom\Master\` con override `GALTEK_CLASSROOM_MASTER_DATA_DIR`.
- Agregada migracion `V1__create_master_domain.sql`.
- Agregadas tablas de aulas, aplicaciones, grupos, alumnos, devices, workspaces, perfiles de navegador, assignments y batch operations.
- Agregados repositories explicitos y servicios transaccionales por contexto.
- Agregado estado de almacenamiento y mapeo de errores SQLite a `ErrorCode`.
- Agregadas pruebas de integracion SQLite para migracion, reapertura, constraints, corrupcion, rollback, batch y versionado.

### Archivos principales modificados

- `master-backend/pom.xml`
- `master-backend/src/main/resources/application.yml`
- `master-backend/src/main/resources/db/migration/sqlite/V1__create_master_domain.sql`
- `master-backend/src/main/java/com/galtek/classroom/persistence/`
- `master-backend/src/main/java/com/galtek/classroom/persistence/sqlite/`
- `master-backend/src/main/java/com/galtek/classroom/*/*Repository.java`
- `master-backend/src/main/java/com/galtek/classroom/*/*Service.java`
- `master-backend/src/test/java/com/galtek/classroom/persistence/sqlite/`
- `.gitignore`
- `README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/FUNCTIONAL_MODEL.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`

### Decisiones tomadas

- Usar Spring JDBC y repositories explicitos; no JPA/Hibernate.
- Usar Flyway programatico con migraciones en `db/migration/sqlite`.
- Usar `flyway-core` administrado por Spring Boot con Xerial SQLite JDBC, sin agregar un modulo Flyway SQLite no alineado al BOM.
- IDs del dominio como `TEXT`, timestamps UTC como texto y booleans `0/1`.
- Habilitar foreign keys, WAL, `synchronous=NORMAL` y `busy_timeout`.
- Mantener pool JDBC pequeno.
- `device_assignments` queda como fuente de verdad de assignment actual e historico.
- Proteger un assignment actual por alumno y por device con indices unicos parciales.
- Preservar base corrupta y reportar `MASTER_DATABASE_CORRUPT`.
- No persistir `MasterWindowsBinding` en SQLite.
- No persistir secretos de navegador.

### Cambios descartados

- No se implementaron endpoints nuevos de gestion, UI, gRPC, mTLS, mDNS, pairing, Network Identity, filesystem real, browser automation, wallpapers reales, captura, bloqueo ni comandos remotos.
- No se agrego JPA/Hibernate.
- No se agrego cifrado at-rest ni backup automatico.
- No se creo tabla `master_windows_binding`.
- No se hizo commit.

### Pendiente

- Exponer/usar la persistencia desde endpoints/API futuros del Master.
- Persistir/verificar Master Windows Binding en Agent Service.
- Definir IPC write y autorizacion local antes de operaciones privilegiadas.
- Agregar cifrado at-rest, backup/restore y retencion/borrado seguro de PII cuando se defina el alcance.

### Validaciones

- `mvn clean verify` en `master-backend`: correcto, 50 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 59 pruebas superadas.

### Commit sugerido

`feat(master): persist classroom domain in sqlite`

## 2026-08-26 - Prompt 09

### Realizado

- Implementada autoridad real de Master Windows Binding en `GaltekClassroom.Agent.Service`.
- Agregado `master-binding.json` en `<CommonApplicationData>\Galtek\Classroom\`, separado de `installation.json`, `license.dat` y `classroom.db`.
- Agregado schema v1, validacion de SID, validacion de `installationId`, manejo de binding ausente/corrupto/mismatch y escritura atomica con verificacion.
- Agregada CLI administrativa `--bind-master-current-user`, `--bind-master-account <WINDOWS_ACCOUNT>` y `--replace-master-binding`, con elevacion obligatoria y sin autoelevacion.
- Agregada obtencion del SID real del cliente Named Pipe mediante `NamedPipeServerStream.RunAsClient(...)`.
- Agregada operacion IPC read-only `GET_MASTER_AUTHORIZATION`.
- Extendidos contratos IPC C# y Java sin cambiar `protocolVersion = 1`.
- Agregado endpoint `GET /api/master/authorization` en Master Backend.
- Agregado `MasterAccessGuard` para futuros endpoints administrativos.
- Actualizada documentacion canonica, README, decisiones, reglas y estado actual.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Service/Master/`
- `agent/src/GaltekClassroom.Agent.Service/Ipc/`
- `agent/src/GaltekClassroom.Agent.Shared/`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/`
- `master-backend/src/main/java/com/galtek/classroom/localagent/`
- `master-backend/src/main/java/com/galtek/classroom/master/`
- `master-backend/src/test/java/com/galtek/classroom/localagent/`
- `master-backend/src/test/java/com/galtek/classroom/master/`
- `protocol/local-ipc-v1.md`
- `README.md`
- `.gitignore`
- `installer/windows/`
- `docs/context/ARCHITECTURE.md`
- `docs/context/FUNCTIONAL_MODEL.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`

### Decisiones tomadas

- Agent Service es la autoridad productiva de Master authorization.
- El Master Backend Java no lee `master-binding.json` ni decide con SID recibido por payload.
- Una instalacion tiene cero o un Master Windows Binding.
- Binding se liga a `installationId`; mismatch bloquea Master.
- SID real de caller IPC se obtiene por impersonation del Named Pipe.
- Respuesta de autorizacion no expone SID completo, JWT, ruta del binding ni ACLs.
- Rebinding siempre requiere `--replace-master-binding`.
- Binding corrupto no tumba el Service; bloquea Master y preserva el archivo.
- Normal uninstall preserva binding; `-PurgeData` elimina identidad, licencia y binding.

### Cambios descartados

- No se agrego IPC write.
- No se agrego CRUD escolar ni UI.
- No se agrego gRPC, mTLS, pairing, filesystem real ni comandos remotos.
- No se agrego migration SQLite ni tabla `master_windows_binding`.
- No se hizo commit.

### Pendiente

- UI futura de diagnostico/configuracion de binding sin ser autoridad.
- IPC write futuro solo con autorizacion local disenada.
- Endpoints administrativos futuros deben usar `MasterAccessGuard`.
- Autorizacion de red, pairing y mTLS siguen separados.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 88 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 59 pruebas superadas.

### Commit sugerido

`feat(master): authorize local master via agent service`

## 2026-08-26 - Prompt 10

### Realizado

- Implementada la primera API administrativa real del Master Backend sobre SQLite.
- Agregados DTOs administrativos para aulas, grupos, alumnos, assignments, aplicaciones, operaciones, bootstrap y snapshot.
- Agregado `MasterAdminRepository` con consultas JDBC agregadas para lecturas batch-friendly, conteos, snapshots, operaciones y retryable targets.
- Agregado `MasterAdminService` con reglas de validacion, version optimista, archivado seguro, batches de alumnos y preflight de assignments.
- Agregado `MasterAdminController` bajo `/api` para endpoints administrativos protegidos.
- Reemplazado el handler especifico de device por un `RestControllerAdvice` uniforme para errores HTTP de API.
- Documentada la API en `docs/api/master-api-v1.md`.
- Agregadas pruebas MockMvc para autorizacion, endpoints publicos, CRUD/archive, batches, assignments, snapshot, operaciones y storage unavailable.
- Actualizados README, arquitectura, modelo funcional, reglas, estado actual y decisiones.

### Archivos principales modificados

- `master-backend/src/main/java/com/galtek/classroom/admin/`
- `master-backend/src/main/java/com/galtek/classroom/api/`
- `master-backend/src/main/java/com/galtek/classroom/operations/ErrorCategory.java`
- `master-backend/src/main/java/com/galtek/classroom/operations/ErrorCode.java`
- `master-backend/src/test/java/com/galtek/classroom/admin/MasterAdminControllerTest.java`
- `docs/api/master-api-v1.md`
- `README.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/FUNCTIONAL_MODEL.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- Todo endpoint administrativo del Master Backend llama a `MasterAccessGuard` antes de leer o escribir datos escolares.
- Los endpoints publicos sin guard quedan limitados a diagnostico local: health, device status, machine code y Master authorization.
- Bootstrap y snapshot son modelos de lectura agregados para la UI futura, no entidades de persistencia expuestas directamente.
- `students/batch` permite resultados independientes por fila; un alumno invalido no cancela el lote completo.
- `assignments/batch` hace preflight completo antes de escribir y registra una `batch_operation` `ASSIGN_STUDENT`.
- Assign/close Student -> Device solo modifica metadata SQLite; no mueve `StudentWorkspace` en filesystem.
- `TARGET_OCCUPIED` no reemplaza automaticamente el assignment actual.

### Cambios descartados

- No se implementaron UI, React, Tauri, gRPC, mTLS, pairing, mDNS, filesystem real, browser automation, captura, bloqueo, wallpaper real ni comandos remotos.
- No se agrego autenticacion/JWT al Master Backend.
- No se creo tabla `master_windows_binding` ni se persistio `MasterWindowsBinding` en SQLite.
- No se leyo `master-binding.json` desde Java.
- No se versionaron bases SQLite, secretos ni artefactos.
- No se hizo commit.

### Pendiente

- Prompt 11 debe elegir el siguiente alcance sin reabrir Prompt 10.
- Mantener cualquier endpoint administrativo nuevo bajo `MasterAccessGuard`.
- Implementar UI local o ampliar capacidades operacionales solo en fases posteriores.
- Implementar filesystem real de `StudentWorkspace`, Network Identity, gRPC, mTLS, pairing y discovery en prompts futuros.

### Validaciones

- `mvn clean verify` en `master-backend`: correcto, 67 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 88 pruebas superadas.

### Commit sugerido

`feat(master): add protected administrative api`

## 2026-08-26 - Prompt 9.6

### Realizado

- Formalizado el requisito futuro de cuentas Windows administradas en Clients.
- Agregados modelos puros Java para `ManagedWindowsAccount`, `ManagedWindowsAccountType`, `ManagedWindowsAccountStatus` y `WindowsSessionState`.
- Agregado `ManagedAccountSwitchPlanner` con plan batch por device para `NO_CHANGE`, `LOGON`, `SWITCH`, `PENDING` y `BLOCKED`.
- Agregadas operaciones futuras tipadas `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`.
- Agregado `TargetExecutionStatus.NO_CHANGE` como resultado exitoso no retryable para operaciones idempotentes.
- Agregados errores estructurados para cuenta/sesion Windows administrada.
- Actualizados contratos compartidos C# con tipos de cuenta, estados de sesion, acciones de switch, operaciones y errores futuros.
- Agregadas pruebas Java del planner y pruebas .NET de contratos compartidos.
- Actualizados contexto, arquitectura, modelo funcional, reglas de desarrollo, estado actual, decisiones e historial.

### Archivos principales modificados

- `master-backend/src/main/java/com/galtek/classroom/windows/`
- `master-backend/src/main/java/com/galtek/classroom/operations/OperationType.java`
- `master-backend/src/main/java/com/galtek/classroom/operations/TargetExecutionStatus.java`
- `master-backend/src/main/java/com/galtek/classroom/operations/BatchOperation.java`
- `master-backend/src/main/java/com/galtek/classroom/operations/ErrorCategory.java`
- `master-backend/src/main/java/com/galtek/classroom/operations/ErrorCode.java`
- `master-backend/src/test/java/com/galtek/classroom/windows/ManagedAccountSwitchPlannerTest.java`
- `agent/src/GaltekClassroom.Agent.Shared/OperationContracts.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/OperationContractsTests.cs`
- `docs/context/PROJECT_CONTEXT.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/FUNCTIONAL_MODEL.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- Cada Client tendra inicialmente dos cuentas administradas logicas: `PRIMARY` y `SECONDARY`.
- Los comandos futuros de cuentas administradas enviaran solo `accountId`; no enviaran passwords.
- El Master no almacenara credenciales de cuentas administradas en `classroom.db`.
- La UI futura no recibira passwords ni secretos.
- La credencial real futura pertenecera al Agent Service del Client y debera protegerse con mecanismos seguros de Windows.
- `SWITCH_MANAGED_ACCOUNT(PRIMARY)` puede producir `NO_CHANGE`, `SUCCESS` y `FAILED`; retry solo de fallidos retryable.
- `DEVICE_OFFLINE` se conserva como error para Clients no disponibles.
- `ACCOUNT_NOT_CONFIGURED` y `MANAGED_CREDENTIAL_NOT_CONFIGURED` bloquean hasta configuracion explicita.
- Productivamente, login/switch debera disenarse despues con integracion soportada por Windows, contemplando Credential Provider.

### Cambios descartados

- No se implementaron passwords reales, DPAPI, Credential Provider ni almacenamiento de credenciales.
- No se implemento login/logoff Windows real ni cambio real de usuario.
- No se agrego IPC write, gRPC, pairing, mTLS ni UI.
- No se tocaron endpoints de la API administrativa de Prompt 10.
- No se usaron SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria.
- No se hizo commit.

### Pendiente

- Disenar la custodia segura de credenciales en el Agent Service del Client.
- Disenar el mecanismo productivo soportado por Windows para logon/logoff/switch.
- Definir contratos de red, autorizacion por pairing/mTLS e IPC write cuando corresponda.
- Integrar la UI futura batch-first sin exponer secretos.

### Validaciones

- `mvn clean verify` en `master-backend`: correcto, 76 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 90 pruebas superadas.

### Commit sugerido

`feat(master): model managed windows account switching`

## 2026-08-27 - Prompt 11

### Realizado

- Implementada Network Identity criptografica permanente del Client en `GaltekClassroom.Agent.Service`.
- Agregado `network-identity.json` con metadata publica separada de `installation.json`, `license.dat`, `master-binding.json` y `classroom.db`.
- Agregado `networkIdentityId` propio, `installationId`, `keyId`, `keyName`, `publicKeyFingerprint`, `createdAtUtc` y `schemaVersion`.
- Agregado almacenamiento de private key mediante Windows CNG/KSP de maquina con Microsoft Software Key Storage Provider.
- Configurada llave inicial RSA 2048, de uso de firma y no exportable.
- Agregado resolver conservador: primera ejecucion crea metadata y llave; reapertura conserva identidad y fingerprint.
- Agregados estados `NOT_CONFIGURED`, `READY`, `INVALID`, `KEY_MISSING` e `INSTALLATION_MISMATCH`.
- Agregados errores `NETWORK_IDENTITY_INVALID`, `NETWORK_IDENTITY_KEY_MISSING` y `NETWORK_IDENTITY_INSTALLATION_MISMATCH`.
- Agregada CLI read-only `--network-identity-status`.
- Actualizado `-PurgeData` para intentar eliminar la llave CNG solo cuando `network-identity.json` contiene un `keyName` seguro con prefijo Galtek.
- Agregadas pruebas .NET de creacion, reapertura, fingerprint estable, metadata corrupta, key faltante, mismatch de instalacion, metadata sin material privado y diagnostico read-only.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Service/Network/`
- `agent/src/GaltekClassroom.Agent.Service/Program.cs`
- `agent/src/GaltekClassroom.Agent.Service/Worker.cs`
- `agent/src/GaltekClassroom.Agent.Service/AgentCommandLine.cs`
- `agent/src/GaltekClassroom.Agent.Service/Runtime/AgentRuntimeState.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/NetworkIdentityTests.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/AgentCommandLineTests.cs`
- `installer/windows/uninstall-agent-service.ps1`
- `installer/windows/README.md`
- `docs/context/PROJECT_CONTEXT.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- Network Identity pertenece al Agent Service del Client y no al Master Backend.
- La private key nunca se guarda en JSON, logs, SQLite ni archivos planos.
- `network-identity.json` contiene solo metadata publica.
- `keyId` y `keyName` se derivan deterministamente del `installationId` para detectar estados parciales y evitar adopcion silenciosa.
- Metadata corrupta, fingerprint incompatible, llave faltante o `installationId` distinto no regeneran identidad.
- Network Identity no genera confianza automatica: pairing, certificados, CA, mTLS, gRPC y discovery quedan pendientes.

### Cambios descartados

- No se implementaron pairing, certificados emitidos por Master, CA, mTLS real, gRPC, discovery, comandos remotos, rotacion automatica de claves, envio de secretos ni UI.
- No se uso Commercial License, IP, MAC ni hostname como identidad de red.
- No se agrego IPC de red ni operaciones write.
- No se hizo commit.

### Pendiente

- Prompt 12 debe usar la Client key como base para pairing, certificado/trust y mTLS.
- Definir contrato de red y confianza sin convertir discovery en autorizacion.
- Definir rotacion manual/operacional de claves en una fase posterior.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 101 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 76 pruebas superadas.
- Parser PowerShell de `installer/windows/uninstall-agent-service.ps1`: correcto.
- `--network-identity-status` con `GALTEK_CLASSROOM_DATA_DIR` temporal vacio: correcto, devuelve `NOT_CONFIGURED` y no crea directorio ni llave.

### Commit sugerido

`feat(agent): add client network identity`

## 2026-08-27 - Prompt 12 / cierre 12.1

### Realizado

- Recuperado el Prompt 12 interrumpido sin rehacer la implementacion.
- Implementada Network Identity local del Master en Java, separada de SQLite.
- Agregado `master-network-identity.json` con metadata publica del Master.
- Agregado almacenamiento privado del Master fuera de SQLite/JSON plano mediante `master-network-identity.key` cifrado y `master-network-identity.protector`.
- Extendido el keystore del Client para exportar public key y firmar payloads con su Network Identity sin exponer private key.
- Implementado pairing criptografico Master-Client con challenge/response firmado.
- Agregada expiracion de challenge de 5 minutos y proteccion contra replay.
- Agregado trust store del Master en `paired-clients.json`.
- Agregado trust store del Client en `authorized-masters.json`.
- Agregados estados `UNPAIRED`, `PAIRING_PENDING`, `PAIRED` y `REVOKED`.
- Agregada autorizacion por trust vigente y bloqueo cerrado con `MASTER_NOT_PAIRED`.
- Agregada revocacion sin borrar Installation Identity ni Network Identity.
- Cubierto soporte conceptual para multiples Clients por Master y multiples Masters por Client.
- Actualizada memoria del proyecto para dejar claro que Network Identity no es trust, discovery no es pairing y Prompt 13 sera transporte seguro usando el trust ya establecido.
- Reforzado `.gitignore` para excluir archivos reales de Network Identity, trust stores y private key/protector del Master.

### Archivos principales modificados

- `agent/src/GaltekClassroom.Agent.Service/Network/NetworkIdentityKeyStore.cs`
- `agent/src/GaltekClassroom.Agent.Service/Pairing/`
- `agent/src/GaltekClassroom.Agent.Service/Program.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/NetworkIdentityTests.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/ClientPairingServiceTests.cs`
- `master-backend/src/main/java/com/galtek/classroom/network/`
- `master-backend/src/test/java/com/galtek/classroom/network/MasterPairingServiceTest.java`
- `.gitignore`
- `docs/context/PROJECT_CONTEXT.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- Network Identity no equivale a trust.
- Discovery no equivale a pairing.
- Pairing requiere intencion/aprobacion explicita.
- Challenge/response demuestra posesion de private keys de Master y Client sin exponerlas.
- Trust se persiste en ambos lados antes de permitir administracion remota.
- `REVOKED` no puede administrar el Client.
- IP, MAC, hostname y licencia MASTER no autorizan ni crean pairing.
- Prompt 13 debe implementar transporte seguro usando el trust ya establecido.

### Cambios descartados

- No se implementaron gRPC real, mTLS real, mDNS, certificados emitidos por Master, discovery real, endpoints reales de pairing sobre red ni comandos remotos.
- No se modifico `FUNCTIONAL_MODEL.md` porque no habia contradiccion real.
- No se hizo commit.

### Pendiente

- Implementar transporte seguro en Prompt 13 usando `paired-clients.json` y `authorized-masters.json`.
- Definir `.proto`, mTLS/certificados y discovery real en fases posteriores.
- Mantener comandos remotos y operaciones privilegiadas fuera de alcance hasta existir transporte seguro y autorizacion completa.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 110 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 86 pruebas superadas.

### Commit sugerido

`feat(network): add secure master client pairing`

## 2026-08-27 - Prompt 13

### Realizado

- Implementado protocolo Protobuf versionado `protocol/network/v1/galtek-classroom-network-v1.proto`.
- Agregado servicio gRPC `NetworkConnection.Connect` para conexion persistente, `ClientHello`, estado y heartbeat.
- Implementado transporte Master con TLS/mTLS obligatorio, trust manager por `paired-clients.json` y rechazo fail-closed.
- Implementado cliente Agent saliente con pinning del certificado Master contra `authorized-masters.json`.
- Emitidos certificados self-signed de corta vida desde Network Identity, sin CA global y sin exportar private keys.
- Agregado registro Master de conexiones autenticadas con `CONNECTING`, `ONLINE`, `OFFLINE` y timeout de heartbeat.
- Agregada reconexion Client con backoff acotado.
- Agregadas pruebas .NET y Java para trust, mTLS, heartbeat, timeout, reconnect, revocacion y multiples Clients.
- Actualizada documentacion de arquitectura, contexto, reglas, estado y decisiones.

### Archivos principales modificados

- `protocol/network/v1/`
- `agent/src/GaltekClassroom.Agent.Service/NetworkTransport/`
- `agent/src/GaltekClassroom.Agent.Service/Network/NetworkIdentityKeyStore.cs`
- `agent/src/GaltekClassroom.Agent.Service/Program.cs`
- `agent/tests/GaltekClassroom.Agent.Service.Tests/MasterNetworkTransportTests.cs`
- `master-backend/src/main/java/com/galtek/classroom/network/`
- `master-backend/src/test/java/com/galtek/classroom/network/MasterNetworkTransportTest.java`
- `master-backend/pom.xml`
- `docs/context/PROJECT_CONTEXT.md`
- `docs/context/ARCHITECTURE.md`
- `docs/context/FUNCTIONAL_MODEL.md`
- `docs/context/DEVELOPMENT_RULES.md`
- `docs/agent/CURRENT_STATE.md`
- `docs/agent/DECISIONS.md`
- `docs/agent/HISTORY.md`

### Decisiones tomadas

- El Client inicia la conexion persistente hacia el Master.
- TLS/mTLS es obligatorio; no hay fallback plaintext.
- La autenticacion usa certificados ligados al trust por fingerprint SPKI de Network Identity.
- No se crea CA global que confie automaticamente en instalaciones arbitrarias.
- `PAIRED` es obligatorio y `REVOKED` bloquea conexion administrativa.
- IP, MAC, hostname, discovery y licencia MASTER no autorizan.
- Prompt 13 no incluye comandos remotos ni endpoints reales de discovery/pairing.

### Cambios descartados

- No se implementaron mDNS, discovery real, UI, comandos remotos, shell remota, bloqueo, captura, proyeccion, filesystem, wallpaper ni login/switch Windows real.
- No se copiaron private keys a nuevos archivos ni se versionaron secretos.
- No se hizo commit.

### Pendiente

- Prompt 14 debe construir registro/capabilities y framework de operaciones sobre el transporte seguro existente, sin redisenar pairing/mTLS.
- mDNS/discovery real y flujos reales de pairing/discovery quedan para fases posteriores, sin convertir discovery en trust.
- Comandos administrativos tipados se construiran sobre el framework de operaciones en una fase posterior, sin shell remota ni comandos genericos.
- Hardening productivo de ciclo de vida de certificados y almacenamiento de private key del Master.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 118 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 97 pruebas superadas.

### Commit sugerido

`feat(network): add secure grpc transport`

## 2026-08-28 - Prompt 14 / cierre 14.1

### Realizado

- Recuperado el Prompt 14 interrumpido sin rehacer la implementacion ni avanzar prompts posteriores.
- Extendidos contratos Protobuf v1 con capabilities tipadas y framework `OperationRequest`/`OperationAccepted`/`OperationResult`.
- El Agent anuncia capabilities reales (`HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`) y ya no usa `deviceId` declarado por Client como identidad.
- Agregado dispatcher de operaciones en el Agent con deduplicacion por `operationId`, timeout y resultado `OPERATION_NOT_IMPLEMENTED` para operaciones sin handler.
- Serializadas las escrituras compartidas del stream Agent con lock para convivir con heartbeat y respuestas de operacion.
- Agregada migracion SQLite `V2__add_device_network_bindings.sql` para vincular Devices generados por el Master con Network Identities paired.
- Agregados repository/servicios DTOs y endpoints protegidos para listar Clients de red y registrar Devices.
- `GET /api/network/clients` expone Clients paired/revoked, presencia viva, estado de registro, capabilities y metadata segura.
- `POST /api/classrooms/{classroomId}/devices/register` crea Device persistente y binding vigente para un Client paired, rechazando `REVOKED`, no paired y doble registro.
- `GET /api/classrooms/{id}/snapshot` superpone presencia viva en memoria para Devices registrados sin escribir SQLite en cada heartbeat.
- Actualizada documentacion de contexto, arquitectura, modelo funcional, reglas, decisiones, estado actual, historial y API.

### Decisiones tomadas

- `Network Identity != Pairing != Device != Student`.
- El Master genera y controla `deviceId`; el `deviceId` enviado por Client queda compatible pero no es autoridad.
- `device_network_bindings` no reemplaza `paired-clients.json`; el trust `PAIRED`/`REVOKED` sigue siendo autoridad de pairing.
- Un Client `PAIRED + ONLINE + sin Device` queda `AVAILABLE_FOR_REGISTRATION`.
- Un Client `PAIRED + binding vigente` queda `REGISTERED`.
- Un Client `REVOKED` no es registrable ni administrable.
- IP, MAC, hostname, display name y capabilities no autorizan administracion.
- El heartbeat mantiene presencia principalmente en memoria y no genera writes SQLite periodicos cada 15 segundos.
- El framework de operaciones no ejecuta acciones Windows reales; una operacion desconocida o sin handler responde `OPERATION_NOT_IMPLEMENTED`.
- Un `operationId` duplicado no debe provocar doble ejecucion.

### Cambios descartados

- No se implementaron acciones reales `LOCK_INPUT`, `UNLOCK_INPUT`, `SHUTDOWN`, `RESTART`, `OPEN_APPLICATION`, `OPEN_URL`, login Windows, archivos, wallpaper, captura, proyeccion, mDNS, discovery ni UI.
- No se agrego shell remota, PowerShell remoto, `cmd`, `executablePath` arbitrario ni payload generico de ejecucion.
- No se modifico `V1__create_master_domain.sql`.
- No se versionaron private keys, JWT reales, certificados privados, bases reales, trust stores reales ni artefactos generados.
- No se hizo commit.

### Pendiente

- Handlers productivos de operaciones remotas tipadas en una fase posterior, sin shell ni comandos genericos.
- Discovery/mDNS y flujos reales de discovery/pairing sobre red en una fase posterior.
- UI futura para visualizar y registrar Clients sin convertirse en autoridad de identidad o trust.
- Hardening productivo de ciclo de vida de certificados y almacenamiento de private key del Master.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 120 pruebas superadas (14 Session, 106 Service).
- `mvn clean verify` en `master-backend`: correcto, 105 pruebas superadas.

### Commit sugerido

`feat(devices): register network clients and operation framework`

## 2026-08-28 - Prompt 14.2

### Realizado

- Formalizado el modelo operativo real del aula primaria sin implementar operaciones Windows reales.
- Documentado hardware objetivo: Master i5 8a gen/8 GB/SSD 500 GB, Clients legacy lentos HDD y Clients renovados SSD.
- Documentado que el Master orquesta y conserva workspaces canonicos, sin convertirse en terminal server.
- Documentado que los Clients ejecutan localmente Windows, Chrome, Office, Scratch, RoboMind, USB futuro, working copy local y rendering/captura cuando se implemente.
- Formalizado que `PRIMARY` y `SECONDARY` son Windows normal por default, no kiosco.
- Agregados modelos puros Java para estrategias de assignment, preparacion progresiva por Device, readiness parcial del aula, residency/sync de workspace, limpieza segura, modos de proyeccion, prioridades y politica normal de cuentas administradas.
- Agregado `REMOVABLE_STORAGE` como destino logico futuro autorizado.
- Agregado `openAfterDistribution` en `DistributeFileRequest` para modelar apertura opcional posterior sin transferencia real.
- Ampliados contratos compartidos C# con nombres conceptuales de assignment, preparacion, workspace, proyeccion, prioridades y removable storage.
- Agregadas pruebas Java para estrategias validas, readiness parcial independiente, limpieza segura de workspace, recovery sin confirmacion, prioridades, proyeccion, distribucion, removable storage y politica no restringida de `PRIMARY`/`SECONDARY`.
- Actualizada documentacion de contexto, arquitectura, modelo funcional, reglas, estado, decisiones e historial.

### Decisiones tomadas

- `CLASS_TIME_TO_READY` es KPI principal.
- El aula no tiene un unico boolean `READY`; debe poder representar conteos por target y permitir iniciar con subset listo.
- Una PC lenta, offline o fallida no bloquea a las demas.
- La secuencia conceptual de preparacion es `ASSIGNED -> PREPARING_WINDOWS_SESSION -> PREPARING_WORKSPACE -> PREPARING_BROWSER -> APPLYING_CLASS_CONTEXT -> READY`.
- El workspace canonico vive en Master; la working copy local vive en Client mientras el alumno usa la PC.
- La working copy local solo puede limpiarse despues de `SYNC -> VERIFY -> COMMIT CANONICAL -> CONFIRM`.
- Si falta ACK/confirmacion, no se asume exito ni perdida; se conserva la working copy y se reporta `PENDING_SYNC` o `RECOVERY_REQUIRED`.
- `OPEN_URL`/`OPEN_WEB_CONTENT` son distintos de `SCREEN_SHARE`; YouTube debe preferir abrirse localmente en Chrome del Client.
- Las prioridades operacionales quedan como `CRITICAL > HIGH > NORMAL > LOW`.

### Cambios descartados

- No se implementaron login/logoff Windows real, Credential Provider, filesystem real, sync real, USB real, Chrome automation, captura, proyeccion, distribucion real, OPEN_APPLICATION real, OPEN_URL real, power-loss recovery tecnico, performance tuning, mDNS ni UI.
- No se agregaron migrations ni persistencia nueva.
- No se implemento Prompt 14.3.
- No se hizo commit.

### Pendiente

- Implementar en fases posteriores workflows reales de preparacion, sync/recovery, distribucion, USB, proyeccion/captura y comandos remotos tipados.
- Disenar scheduler real despues, respetando prioridades y evitando que trabajos grandes bloqueen operaciones `CRITICAL`.
- Implementar Prompt 14.3 solo cuando se solicite explicitamente.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 121 pruebas superadas (14 Session, 107 Service).
- `mvn clean verify` en `master-backend`: correcto, 117 pruebas superadas.

### Commit sugerido

`docs(domain): align classroom operational architecture`

## 2026-08-28 - Prompt 14.3

### Realizado

- Formalizados performance budgets y resource profiles como requisitos arquitectonicos medibles, sin implementar Prompt 14.4 ni 14.5.
- Agregados modelos puros Java para `DevicePerformanceProfile`, `MasterPerformanceProfile`, `ResourceWorkClass`, `ClientPerformanceBudget`, `MasterPerformanceBudget`, `LoadSheddingPolicy`, `SheddableWork`, `ResourcePressureState` y `PerformanceDiagnosticPolicy`.
- `LEGACY` queda como default conservador para Clients de perfil desconocido.
- Modelado que `LEGACY` limita una operacion pesada simultanea por Client y que `STANDARD` puede aceptar ligeramente mas sin concurrencia ilimitada.
- Documentados budgets idle: Agent Service <= aprox. 60 MB, Session Agent <= aprox. 40 MB, Client combinado <= aprox. 100 MB y revision sobre aprox. 150 MB combinado.
- Modelado `MASTER_BALANCED` con objetivo inicial de heap JVM <= 512 MB, Hikari pequeno, SQLite local y sin infraestructura distribuida pesada.
- Modelado load shedding con sacrificio de prefetch, inventario no esencial, thumbnails, calidad/FPS de preview, transferencias no urgentes y background antes de control critico.
- Modelado `DEGRADED` como presion de recursos separada de `OFFLINE`.
- Modelado diagnostico de performance on-demand, sin telemetria continua ni envio por heartbeat.
- Ampliados contratos compartidos C# con nombres de perfiles de performance, clases de trabajo, estado `DEGRADED` y trabajo sacrificable.
- Agregadas pruebas Java de politicas/modelos y prueba .NET de contratos compartidos.
- Auditoria de timers/loops actual: Session Agent PING cada 15s trivial, heartbeat gRPC cada 15s, monitor de licencia cada 60s sin WMI, monitor Master de timeout cada aprox. 15s, Worker idle con delay infinito.
- Corregido ruido claro de idle: conexion IPC, requests IPC exitosos y autorizacion Master aceptada pasan a `DEBUG`; retries repetidos de gRPC bajan a `DEBUG` tras el primer warning y heartbeat del Agent ya no relee `authorized-masters.json` en cada ciclo.
- Actualizada documentacion de contexto, arquitectura, modelo funcional, reglas, estado, decisiones e historial.

### Decisiones tomadas

- Galtek Classroom se disena primero para Clients de 4 GB RAM, HDD y CPU de gama baja.
- Cuando Galtek no realiza trabajo solicitado, el Client debe quedar casi idle: CPU cercano a 0%, sin captura, scanning continuo, WMI periodico, writes periodicos ni logs sanos repetitivos.
- Si performance compite con una funcion secundaria, se degrada la funcion secundaria antes que afectar Windows, la aplicacion educativa o el control critico.
- Los perfiles `LEGACY`, `STANDARD` y `MASTER_BALANCED` son operacionales; no son identidad, seguridad, trust, pairing ni autorizacion.
- No usar polling WMI para clasificar hardware; cualquier inferencia futura debe ser conservadora y muy rara.
- `ResourceWorkClass` clasifica consumo/shedability y se relaciona con `OperationPriority` sin reemplazarlo.
- Diagnostico de rendimiento solo bajo solicitud explicita.

### Cambios descartados

- No se implementaron operaciones Windows reales.
- No se implemento power-loss recovery/boot storm de Prompt 14.4.
- No se implemento Prompt 14.5.
- No se implemento captura, proyeccion, transferencias, filesystem sync, scheduler real, deteccion agresiva de hardware ni monitoreo continuo pesado.
- No se agregaron migraciones ni persistencia nueva.
- No se hicieron tests dependientes de CPU, memoria o Working Set exacto.
- No se hizo commit.

### Pendiente

- Scheduler/backpressure real con prioridades y load shedding en una fase posterior.
- Diagnostico on-demand productivo con snapshot ligero.
- Profiling real para fijar flags/limites de packaging productivo.
- Deteccion conservadora de perfil solo si se necesita, sin polling WMI.
- Prompt 14.4 queda pendiente para recovery de apagones/boot storm.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 122 pruebas superadas (14 Session, 108 Service).
- `mvn clean verify` en `master-backend`: correcto, 126 pruebas superadas.

### Commit sugerido

`perf: define low resource performance budgets`

## 2026-08-29 - Prompt 14.4

### Realizado

- Auditado startup real de Agent, Session Agent y Master frente a power loss, red ausente y boot storm.
- Agent Service mantiene `StartupType=Automatic` y agrega startup progresivo con fases `STARTING`, `RECOVERING`, `MINIMAL_READY`, `SECURITY_READY`, `NETWORK_READY`, `OPERATION_READY` y `DEGRADED`.
- `Worker.StartAsync` queda en camino critico minimo: running marker + Installation Identity; Network Identity se resuelve en background y gRPC espera `SECURITY_READY`.
- Commercial License completa queda diferida fuera del startup critico; los handlers de operaciones futuras se rechazan si la licencia no esta activa.
- `GET_DEVICE_STATUS` expone `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive` sin exponer secretos.
- Agregados running markers `agent-service.running` y `master-backend.running` para detectar shutdown no limpio sin heartbeat de disco.
- Agregado `DurableFileWriter` en Agent y `AtomicFiles` en Master para escrituras atomicas/durables localizadas de archivos criticos.
- Conservado SQLite con WAL, `synchronous=NORMAL`, Hikari pequeno y `PRAGMA quick_check`; no se borra WAL/SHM ni se recrea DB ante shutdown no limpio.
- Documentada clasificacion futura `EPHEMERAL`, `NORMAL` y `CRITICAL_DURABLE` sin crear framework grande de durabilidad.
- Agregado jitter acotado de reconexion del Agent: initial 0-2s y retry backoff + jitter moderado.
- Agregados modelos puros Java para readiness de plano de control, politica de startup/recovery y operaciones remotas inciertas.
- Agregadas pruebas .NET y Java para clean/unclean shutdown, no writes periodicos, no regeneracion de Network Identity, readiness minima, jitter, bloqueo por licencia, Master ready con 0 Clients, visual policy y semantica de operacion incierta.
- Actualizada documentacion de contexto, arquitectura, modelo funcional, reglas, estado, decisiones e historial.

### Decisiones tomadas

- POWER LOSS IS NORMAL.
- FAST RECOVERY > VISUAL FEATURES.
- CONTROL PLANE FIRST.
- El Master no espera a todos los Clients ni a un minimo de Clients online para quedar disponible.
- No asumir `SUCCESS` ante perdida de ACK, energia o red.
- Boot/reconnect/heartbeat no inicia captura, proyeccion, thumbnails, sync ni inventario pesado.

### Cambios descartados

- No se implemento Prompt 14.5.
- No se implementaron operaciones Windows reales.
- No se implementaron captura, proyeccion, filesystem sync, UI, scheduler real ni handlers remotos productivos.
- No se cambio SQLite globalmente de `synchronous=NORMAL` a `FULL`.
- No se borro ni recreo DB/WAL/SHM como estrategia de recovery.
- No se hizo commit.

### Pendiente

- Reconciliacion real de operaciones inciertas y workflows reales de workspace/sync.
- Transferencias reales con staging, checksums, resume y commit atomico.
- Scheduler/backpressure real y diagnostico on-demand productivo.
- UI, mDNS/discovery, captura/proyeccion real y handlers remotos tipados en fases posteriores.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 134 pruebas superadas (14 Session, 120 Service).
- `mvn clean verify` en `master-backend`: correcto, 135 pruebas superadas.

### Commit sugerido

`perf: add power loss recovery and fast startup`

## 2026-08-30 - Prompt 14.5A

### Realizado

- Optimizado el runtime idle del Agent Service sin cambiar arquitectura, seguridad, heartbeat de 15 segundos, startup phases, mTLS, trust ni licencia.
- `RemoteOperationDispatcher` mantiene deduplicacion por `operationId`, pero ahora acota IDs completados por retencion/maximo y limpia de forma lazy durante dispatch, sin timers ni polling nuevo.
- Reducidas allocations sanas de Local IPC: `PING` reutiliza payload inmutable, `GET_DEVICE_STATUS` evita copias vacias de roles/features y `LocalIpcFraming.WriteJsonAsync` deja de construir un frame duplicado completo en memoria.
- Cacheados datos estaticos de proceso usados en rutas repetidas: hostname del sistema y version del Agent.
- Ajustado `MasterConnectionStateTracker` para actualizar estado/ACK con una sola seccion critica por cambio.
- Agregadas pruebas dirigidas para dedupe lazy/acotado y conservadas pruebas de IPC/transporte.

### Cambios descartados

- No se agregaron timers, polling nuevo, telemetria continua, GC tuning, dependencias ni frameworks de benchmark.
- No se cambio frecuencia de heartbeat, backoff/jitter, startup type del Service ni validaciones de seguridad.
- No se cacheo trust de forma insegura; `OperationRequest` sigue revalidando trust antes de ejecutar.
- No se implementaron Prompt 14.5B/C/D, operaciones Windows reales, UI, filesystem, captura, proyeccion, USB, mDNS ni discovery.
- No se ejecuto Maven ni suite Java.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\tests\GaltekClassroom.Agent.Service.Tests\GaltekClassroom.Agent.Service.Tests.csproj --filter "FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 16 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe test .\tests\GaltekClassroom.Agent.Service.Tests\GaltekClassroom.Agent.Service.Tests.csproj --filter "FullyQualifiedName~LocalIpc"` en `agent`: correcto, 22 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe test .\tests\GaltekClassroom.Agent.Service.Tests\GaltekClassroom.Agent.Service.Tests.csproj` en `agent`: correcto, 122 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`perf(agent): optimize service idle runtime`

## 2026-08-30 - Prompt 14.5B

### Realizado

- Optimizado el runtime idle del Session Agent sin cambiar WinExe, AtLogon, RunLevel Limited, mutex por sesion, rechazo de Session 0 ni permanencia ante Service caido.
- Eliminado el `PING` redundante antes de `GET_DEVICE_STATUS` durante recuperacion/conexion inicial: un solo status confirma Service reachable, IPC functional y estado inicial.
- Conservado polling saludable por `PING` cada 15 segundos, por ser el request mas pequeno cuando no hay trabajo interactivo.
- El supervisor usa resultados IPC no excepcionales para offline/retry normal y duerme con backoff `2s/5s/10s/30s`.
- Reducidas allocations de request IPC del Session Agent con payload vacio estatico y sin crear opciones JSON de consola durante startup background.
- Agregada prueba para asegurar que no hay `PING` redundante antes de `READY`.

### Cambios descartados

- No se agregaron timers, polling nuevo, conexiones persistentes, WMI, process scanning, filesystem scanning, UI, tray, telemetria ni funciones interactivas.
- No se cambio el protocolo IPC v1 ni se agrego IPC write.
- No se modifico Master Java, Agent Service ni Agent.Shared.
- No se ejecutaron Maven, tests Java ni suite Service.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\tests\GaltekClassroom.Agent.Session.Tests\GaltekClassroom.Agent.Session.Tests.csproj` en `agent`: correcto, 15 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`perf(session): optimize idle supervisor`

## 2026-08-30 - Prompt 14.5C

### Realizado

- Optimizado el runtime idle del Master Backend sin cambiar arquitectura, seguridad, SQLite, Hikari, Flyway, `quick_check`, timeout heartbeat de 45 segundos, TLS/mTLS ni `MasterAccessGuard`.
- `MasterNetworkHeartbeatMonitor` ya no crea scheduler al arrancar si no hay conexiones activas; el scheduler se activa con el primer Client activo y se pausa cuando todos quedan offline.
- Los beans runtime de gRPC del Master (`MasterNetworkConnectionAuthenticator`, `MasterNetworkGrpcService`, `MasterNetworkHeartbeatMonitor`, `MasterNetworkGrpcServer`) se crean solo cuando `galtek.classroom.master.network.grpc.enabled=true`.
- `ClientConnectionRegistry` evita el `Heartbeat` sintetico en `ClientHello`, usa una sola marca de tiempo por pasada de timeout, evita recrear snapshots offline ya offline y expone mapas snapshot por identidad/device sin entregar estructuras mutables internas.
- Heartbeat gRPC conserva revalidacion de trust para detectar revocacion durante streams abiertos, pero evita reparsear/rehashear la public key del Client tras un `ClientHello` ya aceptado.
- `PersistentNetworkClientConnectionService` elimina el reread del binding despues de registrar `ClientHello` de un Device ya registrado.
- Snapshot de aula y listado de Clients usan mapas directos de presencia viva y loops simples en lugar de listas/streams intermedios en el overlay.
- `WindowsNamedPipeTransport` reemplaza el cached pool de threads de plataforma por virtual threads por intercambio IPC.
- Agregada prueba dirigida para asegurar que el monitor no agenda scheduler sin conexiones activas.

### Cambios descartados

- No se implemento Prompt 14.5D.
- No se modifico Agent Service, Session Agent, Protobuf, TLS/mTLS, pairing/trust ni autorizacion.
- No se aumento `maximum-pool-size`, heap JVM ni se agregaron flags GC, profiling framework, dependencias, telemetria continua o Netty tuning especulativo.
- No se eliminaron WAL, `synchronous=NORMAL`, `busy_timeout`, Flyway ni `quick_check`.
- No se implementaron UI, filesystem, mDNS/discovery, transferencia, captura/proyeccion, scheduler/backpressure general ni comandos remotos.
- No se hizo commit.

### Validaciones

- `mvn '-Dtest=MasterNetworkHeartbeatMonitorTest,MasterNetworkTransportTest,MasterPairingServiceTest,NetworkClientControllerTest,MasterAdminControllerTest,WindowsNamedPipeLocalAgentClientTest' test` en `master-backend`: correcto, 44 pruebas superadas.
- `mvn test` en `master-backend`: correcto, 136 pruebas superadas.
- No se ejecuto .NET.

### Commit sugerido

`perf(master): optimize backend idle runtime`

## 2026-08-30 - Prompt 14.5D

### Realizado

- Cerrada formalmente la etapa de optimizacion preventiva inicial de Galtek Classroom.
- Revisada la consistencia conjunta de Prompt 14.5A, 14.5B y 14.5C en Agent Service, Session Agent y Master Backend.
- No se encontraron contradicciones reales en lifecycle, cleanup/disposal, shutdown, schedulers, caches, races claras ni debilitamiento accidental de seguridad.
- Agregado `RuntimeDiagnosticsSnapshot` compartido .NET para snapshot on-demand del proceso actual con working set aproximado, private memory aproximada, CPU acumulado, thread count, uptime y GC managed memory aproximada.
- Agregada operacion IPC read-only `GET_RUNTIME_DIAGNOSTICS` para medir el proceso real `GaltekClassroom.Agent.Service` bajo solicitud explicita.
- Agregado `GaltekClassroom.Agent.Service.exe --runtime-diagnostics` para diagnostico local del proceso actual del Service en modo consola.
- Agregado `GaltekClassroom.Agent.Session.exe --agent-runtime-diagnostics` para consultar por IPC el snapshot runtime del Agent Service.
- Agregado `GaltekClassroom.Agent.Session.exe --runtime-diagnostics` para diagnostico local del proceso Session que ejecuta el comando.
- Agregado `RuntimeDiagnosticsSnapshot` Java basado en `MemoryMXBean`, `ThreadMXBean` y `RuntimeMXBean`.
- Agregado `--runtime-diagnostics` en Master Java para snapshot local ligero sin levantar Spring.
- Creada `docs/testing/PERFORMANCE_VALIDATION.md` con escenarios manuales Client legacy idle, Master offline/online, Master 0 Clients, Master aprox. 26 Clients, startup y `CLASS_TIME_TO_READY`.
- Actualizada regla permanente: performance tuning adicional requiere medicion reproducible en hardware real.
- Prompt 14.5A CLOSED.
- Prompt 14.5B CLOSED.
- Prompt 14.5C CLOSED.
- Prompt 14.5D CLOSED.

### Cambios descartados

- No se implementaron nuevas funciones operativas.
- No se implementaron captura, filesystem, UI, mDNS, Windows handlers, scheduler general ni Prompt 15.
- No se agregaron timers, telemetry service, persistencia, SQLite, dashboard, Prometheus, Micrometer adicional, historico ni envio periodico al Master.
- No se modifico Protobuf ni se agrego IPC write.
- No se hizo tuning JVM ni .NET: sin `-Xmx`, `-Xms`, flags GC, Netty flags, GC overrides, ReadyToRun, trimming, NativeAOT, single-file ni `GCHeapHardLimit`.
- No se cambiaron packaging self-contained ni perfiles `LEGACY`, `STANDARD`, `MASTER_BALANCED`.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 17 pruebas Session y 124 pruebas Service superadas.
- `mvn clean verify` en `master-backend`: correcto, 137 pruebas superadas y jar generado.
- `C:\Users\angel\.dotnet\dotnet.exe run --project .\src\GaltekClassroom.Agent.Service\GaltekClassroom.Agent.Service.csproj -- --runtime-diagnostics` en `agent`: correcto, emitio JSON runtime.
- `C:\Users\angel\.dotnet\dotnet.exe .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --runtime-diagnostics` en `agent`: correcto, emitio JSON runtime.
- `java -jar .\target\galtek-classroom-master-backend-0.1.0-SNAPSHOT.jar --runtime-diagnostics` en `master-backend`: correcto, emitio JSON runtime.

### Commit sugerido

`perf: close initial runtime optimization phase`

## 2026-08-30 - Prompt 15A

### Realizado

- Implementadas las primeras operaciones remotas productivas del Agent: `SHUTDOWN` y `RESTART`.
- Agregados `ShutdownOperationHandler` y `RestartOperationHandler` como handlers tipados explicitos sobre `RemoteOperationDispatcher`.
- Agregada abstraccion injectable `IWindowsPowerController` para aislar pruebas de la llamada nativa real.
- Agregado `WindowsPowerController` productivo con `InitiateSystemShutdownExW`, habilitacion explicita de `SeShutdownPrivilege`, countdown fijo de 10 segundos, mensaje constante y `forceAppsClosed=false`.
- Agregada capability Protobuf `POWER_CONTROL_V1` y mapeo Java a `DeviceCapability.POWER_CONTROL_V1`.
- Agregados errores estructurados `POWER_CONTROL_UNAVAILABLE` y `POWER_CONTROL_FAILED`.
- Conservada la deduplicacion por `operationId`; una request duplicada no programa dos solicitudes de power control.
- Conservado bloqueo por Commercial License activa antes de ejecutar handlers.
- Documentada la semantica: `SUCCESS` significa que Windows acepto la solicitud, no que el equipo ya esta apagado o reiniciado.

### Cambios descartados

- No se implemento endpoint/batch de Master para enviar operaciones; queda para Prompt 15B.
- No se implementaron UI, Session Agent, IPC Service -> Session, scheduler, worker, timers ni reconciliacion Master.
- No se implementaron otras operaciones Windows ni handlers genericos.
- No se uso `shutdown.exe`, `cmd.exe`, PowerShell, scripts, WMI shell, `Process.Start`, `SendKeys`, force-close ni elevacion de procesos.
- No se ejecuto manualmente SHUTDOWN ni RESTART en la computadora de desarrollo.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\tests\GaltekClassroom.Agent.Service.Tests\GaltekClassroom.Agent.Service.Tests.csproj --filter "FullyQualifiedName~PowerOperationHandlerTests|FullyQualifiedName~MasterNetworkTransportTests|FullyQualifiedName~OperationContractsTests"` en `agent`: correcto, 31 pruebas superadas.
- `mvn -Dtest=MasterNetworkTransportTest test` en `master-backend`: correcto, 14 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`feat(agent): implement secure power control operations`

## 2026-08-31 - Prompt 15B

### Realizado

- Implementado `POST /api/classrooms/{classroomId}/power-control` para dispatch batch de `SHUTDOWN` y `RESTART` desde Master.
- Protegido el endpoint con `MasterAccessGuard` antes de preflight o lectura escolar.
- Validado body estricto: solo `type` y `targetDeviceIds`; targets obligatorios, no vacios y sin duplicados.
- Agregado preflight por Device: aula correcta, Device registrado, binding vigente, trust `PAIRED`, no `REVOKED`, conexion autenticada `ONLINE` y `POWER_CONTROL_V1`.
- Reutilizada persistencia `BatchOperation` / `BatchTargetResult` con targets `PENDING` antes del envio y reemplazo final de resultados.
- Agregado `MasterRemoteOperationGateway` para mantener sesiones gRPC autenticadas, enviar `OperationRequest`, correlacionar `OperationAccepted`/`OperationResult` por `(deviceId, operationId)` y limpiar pending state.
- Conservado el mismo `operationId` de batch para varios Agents, con correlacion por target para evitar colisiones.
- Agregados errores `CAPABILITY_NOT_SUPPORTED`, `OPERATION_REJECTED` y `OPERATION_RESULT_UNKNOWN`.
- Tratado `OperationAccepted` solo como reconocimiento, nunca como `SUCCESS`.
- Mapeado timeout/desconexion posterior al envio a `OPERATION_RESULT_UNKNOWN` no retryable.
- Documentada la API y la semantica de `SUCCESS`: Windows acepto la solicitud, no que el equipo ya se apago/reinicio.

### Cambios descartados

- No se modifico el Agent ni .NET.
- No se modifico Protobuf.
- No se agrego UI.
- No se agregaron nuevas operaciones Windows.
- No se implemento retry automatico, scheduler general, worker permanente, cleanup timer ni reconciliacion 15C.
- No se aceptaron comandos, shell, rutas, argumentos, force, timeout o payload libre por API.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=MasterNetworkTransportTest,MasterRemoteOperationGatewayTest,NetworkClientControllerTest" test` en `master-backend`: correcto.
- `mvn test` en `master-backend`: correcto, 149 pruebas superadas.

### Commit sugerido

`feat(master): dispatch batch power control operations`

## 2026-08-31 - Prompt 15C

### Realizado

- Agregado `OperationStatusQuery`/`OperationStatusReport` al Protobuf v1 sobre `NetworkConnection.Connect`, sin cambiar `protocolVersion` ni crear otro servicio gRPC.
- Implementada consulta read-only en el Agent: valida el stream Master/trust vigente, consulta cache/receipt y responde `KNOWN` o `UNKNOWN` sin ejecutar handlers.
- Expuesto `RemoteOperationDispatcher.TryGetCompletedResult` para leer resultados completados retenidos por dedupe/cache sin extender retencion.
- Agregado `PowerOperationReceiptStore` durable y acotado en `power-operation-receipts.json` para receipts minimos de `SHUTDOWN`/`RESTART` aceptados por Windows.
- Ajustados handlers de power control para persistir receipt despues de aceptacion Windows y antes de devolver `SUCCESS`; si falla el receipt, se conserva `SUCCESS` normal y se registra warning seguro.
- Extendida correlacion Master por `(deviceId, operationId)` para status queries, con timeout corto, limpieza de mapas y validacion del tipo persistido contra el `OperationResult`.
- Implementada reconciliacion de late `OperationResult` autentico solo sobre targets `FAILED + OPERATION_RESULT_UNKNOWN`, sin modificar `SUCCESS` ni aceptar resultados de otro Device.
- Implementada reconciliacion ligera en reconnect del mismo Device, filtrando en SQLite solo operaciones `SHUTDOWN`/`RESTART` inciertas de ese Device.
- Implementado recovery once de startup para convertir targets power `PENDING` huerfanos a `FAILED + OPERATION_RESULT_UNKNOWN`, sin resend ni espera de Clients online.
- Agregado `POST /api/operations/{operationId}/reconcile` protegido por `MasterAccessGuard`, sin body arbitrario y limitado a `SHUTDOWN`/`RESTART`.
- Reutilizadas tablas existentes `batch_operations` y `batch_target_results`; no se agrego migration.

### Cambios descartados

- No se implemento UI.
- No se agregaron operaciones funcionales nuevas.
- No se implemento retry automatico ni resend automatico de `SHUTDOWN`/`RESTART`.
- No se infiere `SUCCESS` por `OFFLINE`, reconnect, timestamps ni evidencia indirecta.
- No se agrego scheduler general, polling, timer de cleanup, telemetry, Local IPC nuevo, SQLite Agent ni tabla nueva Master.
- No se ejecuto power control fisico.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~PowerOperationHandlerTests|FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 28 pruebas Service superadas.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,PowerOperationReconciliationServiceTest,NetworkClientControllerTest" test` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 17 pruebas Session y 136 pruebas Service superadas.
- `mvn test` en `master-backend`: correcto, 162 pruebas superadas.

### Commit sugerido

`feat: reconcile uncertain remote power operations`

## 2026-09-01 - Prompt 16A

### Realizado

- Agregado `Session Command v1` como canal local separado Service -> Session, sin modificar Local IPC v1.
- Documentado el protocolo canonico en `protocol/local-session-command-v1.md`.
- Agregados contratos compartidos tipados `SessionCommandRequest`/`SessionCommandResponse`, `protocolVersion = 1`, `requestId` UUID, `commandType`, error codes y framing dedicado de 16 KiB.
- El Session Agent ahora inicia un pipe server por sesion `GaltekClassroom.Agent.SessionCommand.v1.<sessionId>` solo despues de adquirir instancia unica y validar `Process.SessionId != 0`.
- El pipe server de Session usa `WaitForConnectionAsync`, vuelve a aceptar tras cada conexion y se cancela limpiamente.
- La ACL productiva del pipe de comandos permite como cliente solo LocalSystem (`S-1-5-18`).
- El Session Agent valida el SID real del caller Named Pipe mediante impersonation y rechaza callers no LocalSystem.
- Agregado `SessionCommandClient` en el Agent Service con resolucion interna de sesion interactiva mediante `WTSGetActiveConsoleSessionId`, timeout fijo de 2 segundos y conexion on-demand no persistente.
- El Service verifica el servidor Named Pipe con PID real, proceso existente, `Process.SessionId` esperado y ruta productiva normalizada del Session Agent antes de enviar comandos.
- Implementado solo `CHANNEL_PING`, que devuelve `SUCCESS` sin acciones visibles ni efectos externos.
- Agregados tests dirigidos de protocolo, framing, correlacion `requestId`, rechazo de comandos desconocidos, Session 0, autorizacion fake de caller, cancelacion, verificacion del servidor y timeouts.

### Cambios descartados

- No se implemento `OPEN_URL`.
- No se implemento bloqueo de URLs ni bloqueo de descargas.
- No se implemento `OPEN_APPLICATION`, bloqueo de input, overlays, wallpaper, filesystem, shell, scripts, browser policies ni UI.
- No se modifico Master Backend Java.
- No se modifico API HTTP, Protobuf, gRPC, SQLite, `RemoteOperationDispatcher`, power control ni reconciliacion.
- No se agregaron comandos write a `GaltekClassroom.Agent.v1`.
- No se acepto payload generico, `command`, `arguments`, `RUN_*` ni `EXECUTE_*`.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~SessionCommand|FullyQualifiedName~SessionAgentBackgroundHostTests"` en `agent`: correcto, 11 pruebas Session y 14 pruebas Service superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`feat(agent): add trusted session command channel`

## 2026-09-01 - Prompt 16B

### Realizado

- Extendido Protobuf v1 sin cambiar `protocolVersion`: `OperationRequest` agrega parametros tipados `OpenUrlOperationParameters.url`, capability `OPEN_URL_V1` y codigos operacionales de URL/sesion.
- Extendida Session Command v1 con comando tipado `OPEN_URL` y campo `openUrl.operationId/url`, conservando framing BIG ENDIAN de 4 bytes + JSON UTF-8, limite 16 KiB, pipe por sesion, ACL LocalSystem, autenticacion bilateral y timeout existente.
- Agregada `OpenUrlSafetyPolicy` C# compartida por Service y Session Agent: solo URL absoluta `http://`/`https://`, host no vacio, maximo 4096, sin caracteres de control/CR/LF ni username/password embebidos.
- Agregado `OpenUrlOperationHandler` explicito en Agent Service: extrae parametros tipados, valida URL, usa `SessionCommandClient.OpenUrlAsync`, mapea errores estructurados y no abre navegador desde Session 0.
- `SessionCommandClient.OpenUrlAsync` crea `requestId` nuevo, valida `requestId` de respuesta, no usa Local IPC v1, no hace retry automatico y diferencia `SESSION_AGENT_UNAVAILABLE` de `SESSION_COMMAND_RESULT_UNKNOWN` despues de enviar.
- Session Agent maneja `CHANNEL_PING` y `OPEN_URL`; valida protocolo, `requestId`, `operationId`, URL y caller LocalSystem antes de llamar al launcher.
- Agregados `IUrlLauncher`, `WindowsUrlLauncher` y wrapper `IWindowsShellExecutor`; produccion usa `ShellExecuteExW` con verbo `open`, URL validada y sin parametros.
- `OPEN_URL SUCCESS` queda definido como Windows acepto la solicitud de launch; no comprueba DNS, Internet, HTTP status, carga ni render del navegador.
- `RemoteOperationDispatcher` conserva dedupe por `operationId` y ahora compara parametros tipados para detectar conflicto si el mismo operationId llega con otra URL.
- El Agent anuncia `OPEN_URL_V1`; Java agrega mapeo minimo de capability/errores para compatibilidad de generacion sin endpoint batch ni servicio funcional Master.
- Documentacion actualizada en protocolo local, README de red, contexto, arquitectura, modelo funcional, decisiones, estado e historial.

### Cambios descartados

- No se implemento endpoint batch Master para `OPEN_URL`.
- No se implemento UI.
- No se implemento bloqueo de URLs, allowlist/blocklist ni bloqueo de descargas.
- No se implemento `OPEN_APPLICATION`, browser profile selection, Chrome/Edge forzado, extension de navegador, policies, DNS/proxy/intercepcion HTTP ni monitoreo de historial/tabs.
- No se uso `cmd`, PowerShell, `explorer.exe` con argumentos arbitrarios, browser path remoto, scripts, WMI shell, `Process.Start` desde el Service ni `CreateProcessAsUser`.
- No se hizo prueba manual que abra navegador real.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~OpenUrlSafetyPolicyTests|FullyQualifiedName~SessionCommand|FullyQualifiedName~WindowsUrlLauncherTests|FullyQualifiedName~OpenUrlOperationHandlerTests"` en `agent`: correcto, 19 pruebas Session y 48 pruebas Service superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`feat(agent): open trusted urls in interactive session`

## 2026-09-01 - Prompt 16C

### Realizado

- Agregado dominio Master `browserpolicy` para policies administrativas persistentes de navegacion web.
- Modelados `BrowserAccessPolicy`, modos `UNRESTRICTED`/`BLOCKLIST`/`ALLOWLIST`, scopes `CLASSROOM`/`GROUP`/`DEVICE` y account scopes `ANY`/`PRIMARY`/`SECONDARY`.
- Agregado `BrowserUrlRule` con acciones `ALLOW`/`BLOCK` y match types `HOST_EXACT`, `HOST_SUFFIX`, `URL_PREFIX` y `EXACT_URL`, sin regex arbitraria ni wildcards libres.
- Agregado `BrowserUrlNormalizer` para safety/canonicalizacion `http`/`https`: host obligatorio, sin userinfo/control chars, host lowercase, trailing dot removido, puertos default normalizados, path vacio como `/` y fragment eliminado.
- `OpenUrlPolicy` Java reutiliza el normalizador nuevo como safety estructural, separado de la policy administrativa.
- Agregado `BrowserNavigationPolicyEvaluator` puro con decisions `ALLOW`/`BLOCK`, `policyId`, `matchedRuleId` y reason codes `NO_POLICY`, `UNRESTRICTED`, `DEFAULT_ALLOW`, `DEFAULT_BLOCK`, `EXPLICIT_ALLOW`, `EXPLICIT_BLOCK` e `INVALID_URL`.
- Agregado `BrowserPolicyPrecedenceResolver` puro con precedencia determinista de una sola policy efectiva: `DEVICE` cuenta especifica, `DEVICE ANY`, `GROUP` cuenta especifica, `GROUP ANY`, `CLASSROOM` cuenta especifica, `CLASSROOM ANY`, o `UNRESTRICTED` implicito.
- Agregada migracion SQLite `V3__add_browser_navigation_policies.sql` con tablas `browser_access_policies` y `browser_url_rules`, checks de enum/scope e indices unicos parciales para una policy activa por target/account.
- Agregado `BrowserPolicyRepository` y `SqliteBrowserPolicyRepository` con Spring JDBC explicito, sin JPA.
- Agregada API administrativa protegida por `MasterAccessGuard` para CRUD/archive de policies y rules, mas endpoint read-only de policy efectiva.
- Agregados codigos `BROWSER_POLICY_NOT_FOUND`, `BROWSER_POLICY_CONFLICT`, `BROWSER_POLICY_SCOPE_INVALID`, `BROWSER_POLICY_RULE_INVALID` y `URL_BLOCKED_BY_POLICY`.
- Documentacion actualizada en contexto, arquitectura, modelo funcional, reglas de desarrollo, decisiones, estado, historial y API Master v1.

### Cambios descartados

- No se modifico Agent .NET.
- No se modifico Protobuf/gRPC, Session Command, Local IPC ni transportes.
- No se implemento endpoint batch Master para `OPEN_URL`.
- No se aplico bloqueo real en Chrome, Edge, Windows, DNS, proxy, firewall, hosts, extension ni inspeccion de trafico.
- No se implementaron politicas de descargas.
- No se implemento UI.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=BrowserNavigationPolicyEvaluatorTest,BrowserPolicyPrecedenceResolverTest,BrowserPolicyControllerTest,MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): add browser navigation policies`

## 2026-09-02 - Prompt 16D

### Realizado

- Corregida la semantica Java de `BrowserNavigationPolicyEvaluator`: gana el filtro mas especifico por host, scheme/port, path y query; solo ante igual especificidad `ALLOW` gana a `BLOCK`.
- Extendido Protobuf v1 sin cambiar `protocolVersion`: operacion tipada `APPLY_BROWSER_NAVIGATION_POLICY`, parametros `ApplyBrowserPolicyOperationParameters`, rules tipadas, enums de browser policy, capability `BROWSER_NAVIGATION_POLICY_V1` y codigos estructurados de browser policy.
- Agregado enforcement Agent-side para Chrome/Edge mediante `URLBlocklist`/`URLAllowlist` en `HKEY_USERS\<SID>` del usuario interactivo real, sin HKLM para escritura Galtek.
- Agregado `ChromiumBrowserPolicyCompiler`: compila `HOST_EXACT`, `HOST_SUFFIX` y `URL_PREFIX`, canonicaliza/dedupe/sort, genera content hash, limita 1000 filtros por lista y rechaza `EXACT_URL` como `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`.
- Agregado evaluator C# de subset Chromium para defensa en profundidad de `OPEN_URL`.
- Agregado `WindowsInteractiveUserIdentityResolver` con `WTSGetActiveConsoleSessionId`, `WTSQueryUserToken` y `GetTokenInformation(TokenUser)`, sin `whoami`, shell, PowerShell ni WMI shell.
- Agregado registry store productivo para las cuatro subkeys Chrome/Edge, con deteccion de HKLM conflictivo, user hive unavailable, valores secuenciales `REG_SZ` y ACL LocalSystem/Admin full + usuario ReadKey.
- Agregado estado durable `browser-navigation-policy-state.json` y journal lazy `browser-navigation-policy-apply.json`; no hay timer, polling, scan de procesos, scan de browsers ni writes periodicos.
- Agregado `ApplyBrowserPolicyOperationHandler` explicito con flujo license-gated via dispatcher, account scope check, user resolution, native compile, recovery/preflight/ownership/apply/verify/state.
- `OPEN_URL` ahora aplica safety estructural primero y despues evalua la policy Galtek local aplicada; si bloquea devuelve `URL_BLOCKED_BY_POLICY` y no envia Session Command.
- Agregado documento `docs/browser/BROWSER_POLICY_ENFORCEMENT.md`.

### Cambios descartados

- No se agrego endpoint batch Master para policies ni `OPEN_URL`.
- No se agrego UI, Session Command nuevo, browser profile selection, descargas, extension, proxy, DNS, firewall, hosts, inspeccion HTTPS, traffic monitoring, history/tab monitoring, browser automation, polling ni kill/restart de navegador.
- No se implemento binding `PRIMARY`/`SECONDARY -> Windows SID`; esos scopes devuelven `BROWSER_ACCOUNT_SCOPE_UNRESOLVED`.
- No se hizo prueba manual contra Chrome/Edge personales del desarrollador.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=BrowserNavigationPolicyEvaluatorTest,BrowserPolicyPrecedenceResolverTest,BrowserPolicyControllerTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~BrowserPolicyAgentTests|FullyQualifiedName~OpenUrlOperationHandlerTests|FullyQualifiedName~MasterNetworkTransportTests|FullyQualifiedName~OperationContractsTests"` en `agent`: correcto, 55 pruebas Service superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`feat(agent): enforce browser navigation policies`

## 2026-09-02 - Prompt 16E1

### Realizado

- Agregado dominio Master `BrowserDownloadPolicy` separado de `BrowserAccessPolicy`.
- Agregado enum `BrowserDownloadRestrictionMode` con valores exactos `NO_SPECIAL_RESTRICTIONS`, `BLOCK_DANGEROUS`, `BLOCK_POTENTIALLY_DANGEROUS`, `BLOCK_ALL` y `BLOCK_MALICIOUS`.
- Reutilizados scopes `CLASSROOM`/`GROUP`/`DEVICE` y account scopes `ANY`/`PRIMARY`/`SECONDARY`.
- Agregado `BrowserDownloadPolicyPrecedenceResolver` puro con una sola policy efectiva y `NO_SPECIAL_RESTRICTIONS` implicito cuando no hay policy aplicable.
- Agregada migracion SQLite `V4__add_browser_download_policies.sql` con tabla `browser_download_policies`, checks de enum/scope, indices unicos parciales por target/account activo y triggers para impedir `GROUP`/`DEVICE` de otro classroom.
- Agregado `BrowserDownloadPolicyRepository` y `SqliteBrowserDownloadPolicyRepository` con Spring JDBC explicito, sin JPA.
- Agregada API administrativa protegida por `MasterAccessGuard` para listar, crear, patch, archivar y resolver policy efectiva de descarga.
- Agregados codigos `BROWSER_DOWNLOAD_POLICY_NOT_FOUND`, `BROWSER_DOWNLOAD_POLICY_CONFLICT` y `BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID`.
- Documentado que navegacion y descargas son dominios distintos, que la API no expone numeros Chromium como autoridad y que `NO_SPECIAL_RESTRICTIONS` no desactiva Safe Browsing.
- Documentada la limitacion Windows: no se modelan `blockedExtensions`, `allowedExtensions`, `blockedMimeTypes` ni `allowedMimeTypes` porque no hay enforcement comun garantizable Chrome/Edge Windows para bloqueo arbitrario.
- Documentada la direccion futura de descargas autorizadas por maestra: entrega Galtek tipada/controlada hacia `StudentWorkspace`, no desbloqueo temporal de browser.

### Cambios descartados

- No se modifico Agent .NET.
- No se modifico Protobuf/gRPC, Registry, C# ni transportes.
- No se implemento enforcement Agent-side de `DownloadRestrictions`.
- No se agregaron extension denylist, MIME denylist, browser extension, proxy, DNS, firewall, filesystem watcher, download monitoring ni browser automation.
- No se implemento descarga autorizada real por maestra ni `DISTRIBUTE_FILE`.
- No se implemento UI.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=BrowserDownloadPolicyPrecedenceResolverTest,BrowserDownloadPolicyPersistenceIntegrationTest,BrowserDownloadPolicyControllerTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `git diff --check`: correcto, solo advertencias de conversion CRLF esperadas.

### Commit sugerido

`feat(master): add browser download policies`

## 2026-09-02 - Prompt 16E2A

### Realizado

- Extendido Protobuf v1 sin cambiar `protocolVersion`: operacion tipada `APPLY_BROWSER_DOWNLOAD_POLICY`, parametros `ApplyBrowserDownloadPolicyOperationParameters`, enum `BrowserDownloadRestrictionMode`, error minimo `BROWSER_DOWNLOAD_POLICY_INVALID` y capability reservada `BROWSER_DOWNLOAD_POLICY_V1`.
- Agregado `ChromiumDownloadPolicyCompiler` puro en C# para traducir `NO_SPECIAL_RESTRICTIONS`, `BLOCK_DANGEROUS`, `BLOCK_POTENTIALLY_DANGEROUS`, `BLOCK_ALL` y `BLOCK_MALICIOUS` a valores nativos `DownloadRestrictions` 0-4.
- Diferenciado `NO_SPECIAL_RESTRICTIONS` implicito (`RemoveGaltekPolicy = true`, valor nativo nulo) de `NO_SPECIAL_RESTRICTIONS` explicito (`RemoveGaltekPolicy = false`, valor nativo `0`) y reflejado en content hash determinista.
- Validacion del compilador: enum de modo requerido, `accountScope` requerido, `policyId`/`policyVersion` obligatorios para policies explicitas y rechazo de combinacion implicita con modo distinto de `NO_SPECIAL_RESTRICTIONS`.
- Actualizada la deduplicacion del `RemoteOperationDispatcher` para comparar los parametros tipados de la nueva operacion.
- Actualizados mapeos Java minimos para `OperationType`, `ErrorCode`, `DeviceCapability`, `ClientCapabilityMapper`, prioridad default y mapping de error de transporte.
- Reservada `BROWSER_DOWNLOAD_POLICY_V1` sin anunciarla en `ClientHello`.

### Cambios descartados

- No se modifico Registry, HKU, HKLM, ACL, ownership, journal, durable state ni recovery de descargas.
- No se implemento ni registro `ApplyBrowserDownloadPolicyOperationHandler`.
- No se anuncio capability productiva falsa.
- No se agrego endpoint Master, batch dispatch, API, UI, Session Command, extension denylist, MIME denylist, temporary unlock, browser restart, browser scan, process scan ni `DISTRIBUTE_FILE`.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~BrowserPolicyAgentTests|FullyQualifiedName~OperationContractsTests|FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 59 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(agent): add browser download policy contract`

## 2026-09-02 - Prompt 16E2B

### Realizado

- Implementado enforcement productivo Agent-side de `APPLY_BROWSER_DOWNLOAD_POLICY` para Chrome y Edge en Windows usando solo `DownloadRestrictions`.
- Reutilizado `IInteractiveUserIdentityResolver` para resolver el usuario interactivo real y aplicar en `HKEY_USERS\<SID>`, no `HKCU` desde LocalSystem ni HKLM.
- Agregado `ApplyBrowserDownloadPolicyOperationHandler` y registrado en `RemoteOperationDispatcher`.
- Agregado `IBrowserDownloadPolicyRegistryStore`/`WindowsBrowserDownloadPolicyRegistryStore` para leer, escribir, borrar y verificar `DownloadRestrictions` como `REG_DWORD`.
- Agregado state durable separado `browser-download-policy-state.json` y journal lazy `browser-download-policy-apply.json` mediante `DurableFileWriter`.
- Preservada la distincion entre `NO_SPECIAL_RESTRICTIONS` implicito, que remueve solo policy Galtek-owned, y `NO_SPECIAL_RESTRICTIONS` explicito, que escribe `REG_DWORD 0`.
- Implementado preflight conservador de ownership: conflicto ante `DownloadRestrictions` HKLM, user-level desconocido, divergencia contra state Galtek, parent values desconocidos o subkeys no demostradas como Galtek-owned.
- Agregado reader read-only de ownership de navegacion para permitir `URLBlocklist`/`URLAllowlist` solo cuando state de navegacion 16D demuestra ownership.
- Implementado hardening ACL parent-only para Chrome/Edge parent key, sin rewrite recursivo de child subkeys y evitando reescritura cuando la ACL ya es segura.
- Implementado rollback ante fallo parcial y recovery lazy tras power loss al siguiente apply.
- Anunciada capability `BROWSER_DOWNLOAD_POLICY_V1` solo despues de registrar el handler productivo.
- Agregados errores download-specific `BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT`, `BROWSER_DOWNLOAD_POLICY_APPLY_FAILED`, `BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED` y `BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED`.
- Actualizada documentacion de browser download policy, arquitectura, modelo funcional, reglas, estado, decisiones y protocolo.

### Cambios descartados

- No se implemento endpoint Master, batch dispatch, UI, Session Command, temporary unlock, teacher-authorized download delivery ni `DISTRIBUTE_FILE`.
- No se agrego extension denylist, MIME denylist, browser extension, proxy, DNS, firewall, hosts, browser automation, process monitoring, browser version polling, filesystem watcher ni DLP.
- No se implemento endpoint Master, dispatch batch ni persistence Java; solo se actualizo el mapping minimo de errores remotos de descarga.
- No se hizo prueba manual contra Chrome/Edge personales del desarrollador.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~BrowserDownloadPolicyAgentTests|FullyQualifiedName~BrowserPolicyAgentTests|FullyQualifiedName~OperationContractsTests|FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 86 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.

### Commit sugerido

`feat(agent): enforce browser download policies`

## 2026-09-02 - Prompt 16F1

### Realizado

- Agregados endpoints Master `POST /api/classrooms/{classroomId}/browser-policies/apply` y `POST /api/classrooms/{classroomId}/browser-download-policies/apply`.
- Ambos endpoints quedan protegidos por `MasterAccessGuard` y aceptan solo `targetDeviceIds` explicitos, obligatorios, no vacios, sin strings en blanco, sin duplicados y sin campos adicionales.
- Implementado `BrowserPolicyDispatchService` para resolver policies efectivas persistidas por target desde SQLite, usando `accountType = null` (`ANY` solamente) y `groupId` derivado del assignment actual `Student -> Device`.
- Reutilizado `MasterRemoteOperationGateway` para enviar parametros Protobuf tipados de `APPLY_BROWSER_NAVIGATION_POLICY` y `APPLY_BROWSER_DOWNLOAD_POLICY` sobre el transporte gRPC/mTLS existente.
- Congelados todos los parametros por target antes del fanout, persistiendo una sola `BatchOperation` con el mismo `operationId` para todos los targets.
- Preflight por target alineado con power control: pertenencia al aula, binding vigente, trust `PAIRED` no `REVOKED`, conexion autenticada `ONLINE` y capability `BROWSER_NAVIGATION_POLICY_V1` o `BROWSER_DOWNLOAD_POLICY_V1`.
- Navegacion bloquea targets con `EXACT_URL` habilitado en la policy efectiva usando `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`, sin enviar request al Agent.
- Descargas conserva la distincion entre `NO_SPECIAL_RESTRICTIONS` implicito y policy explicita con valor nativo 0.
- Agregada migracion SQLite V5 para permitir persistir batch operations de apply de browser policies.
- Actualizada documentacion de API, arquitectura, modelo funcional, reglas, estado, decisiones y browser policy/download policy.

### Cambios descartados

- No se modifico Agent .NET.
- No se modifico Protobuf/gRPC, Registry ni Session Command.
- No se agrego endpoint batch Master para `OPEN_URL`.
- No se agrego UI, browser automation, extension, proxy, DNS, firewall, hosts, traffic monitoring ni download delivery autorizada.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=BrowserPolicyDispatchControllerTest,BrowserPolicyControllerTest,BrowserDownloadPolicyControllerTest,BrowserPolicyPrecedenceResolverTest,BrowserDownloadPolicyPrecedenceResolverTest,BrowserDownloadPolicyPersistenceIntegrationTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q test` en `master-backend`: correcto, 222 pruebas superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `git diff --check`: correcto.

### Commit sugerido

`feat(master): dispatch browser policy batches`

## 2026-09-02 - Prompt 16F2

### Realizado

- Agregado endpoint Master `POST /api/classrooms/{classroomId}/open-url`.
- La request acepta solo `url` y `targetDeviceIds`; rechaza campos extra como browser/profile, `accountType`, `policyId`, rules, comandos, argumentos, shell, timeout, SID, registry paths o payload libre.
- Implementado `OpenUrlDispatchService` con `MasterAccessGuard` antes de datos escolares, safety estructural global mediante `OpenUrlPolicy` y rechazo total `INVALID_URL` sin crear batch.
- Reutilizada resolucion batch-friendly de contexto: Devices del aula, bindings vigentes, snapshots de conexion, trust paired/revoked, assignments actuales y grupo desde `Student.groupId`.
- Resuelta una sola policy efectiva por target con `BrowserPolicyPrecedenceResolver`, `accountType = null` (`ANY` solamente) y `UNRESTRICTED` implicito cuando no hay policy.
- Evaluado `OPEN_URL` en Master con `BrowserNavigationPolicyEvaluator`; `EXACT_URL` funciona para una URL concreta y no produce `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`.
- Targets bloqueados por policy quedan `FAILED URL_BLOCKED_BY_POLICY` sin enviar `OperationRequest`; otros targets continuan.
- Preflight `OPEN_URL` exige aula correcta, binding, trust `PAIRED`, no `REVOKED`, conexion gRPC/mTLS autenticada `ONLINE` y capability `OPEN_URL_V1`; no exige browser instalado ni `SESSION_AGENT_AVAILABLE`.
- Extendida `MasterRemoteOperationGateway` para enviar `OperationType.OPEN_URL` con `OpenUrlOperationParameters.url` tipado y preservar la URL original aceptada.
- Persistida una unica `BatchOperation` `OPEN_URL` antes del fanout, con el mismo `operationId` para todos los Agents y payload minimo de auditoria con la URL.
- Conservadas las semanticas `OPERATION_RESULT_UNKNOWN`, `SESSION_COMMAND_RESULT_UNKNOWN`, `SESSION_AGENT_UNAVAILABLE`, `URL_BLOCKED_BY_POLICY`, sin retry automatico ni reconciliacion de tabs.
- Confirmado que SQLite no requiere V6: V1/V5 ya permiten `OPEN_URL` en `batch_operations`.
- Actualizada documentacion de API, arquitectura, modelo funcional, reglas, estado, decisiones, historial y browser policy enforcement.

### Cambios descartados

- No se modifico Agent .NET, Session Agent, Protobuf, Registry, installer ni enforcement 16D.
- No se agrego endpoint por Device individual, UI, browser selector, Chrome/Edge forced, profile/incognito/newTab, browser automation, tab/history monitoring, process scan, automatic policy apply, retry automatico ni `OperationStatusQuery` para `OPEN_URL`.
- No se creo migracion V6 artificial.
- No se ejecutaron tests ni builds .NET.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=OpenUrlDispatchControllerTest,MasterRemoteOperationGatewayTest,BrowserNavigationPolicyEvaluatorTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=BrowserPolicyDispatchControllerTest,OpenUrlDispatchControllerTest,MasterRemoteOperationGatewayTest,BrowserNavigationPolicyEvaluatorTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): dispatch open url batches`

## 2026-09-03 - Prompt 17A

### Realizado

- Agregado en `GaltekClassroom.Agent.Service` el modelo local `ApplicationBinding` separado de `ApplicationDefinition` del Master.
- Creado `application-bindings.json` en `<CommonApplicationData>\Galtek\Classroom\` como fuente local `applicationId -> launch target`.
- Implementado `IApplicationBindingStore`/`ApplicationBindingStore` con Load/Get/List/Add/Replace/SetEnabled/Remove, escritura durable con `DurableFileWriter`, temp file, flush/fsync, replace/move atomico, ACL y verificacion posterior.
- Soportados solo launch types `APP_PATHS` y `ABSOLUTE_EXE`.
- Validado `APP_PATHS` como nombre de archivo `.exe` sin path, separadores, dos puntos, comillas, espacios, control chars ni argumentos.
- Validado `ABSOLUTE_EXE` como ruta Windows local absoluta `.exe`, no UNC, no relativa, sin `..`, ADS, control chars, comillas, argumentos, wildcards ni placeholders de entorno; create/replace exige existencia del archivo.
- Corrupcion, schema desconocido, duplicados, launch type desconocido o campos incompatibles devuelven `APPLICATION_BINDINGS_INVALID` sin regenerar ni adoptar parcialmente.
- Agregada CLI local: `--application-bind-list`, `--application-bind-exe`, `--application-bind-app-path`, `--application-bind-disable`, `--application-bind-enable`, `--application-bind-remove` y `--replace-application-binding`.
- Mutaciones por CLI requieren elevacion administrativa; list/read-only no autoeleva ni muta el archivo.
- Documentado `ApplicationDefinition != ApplicationBinding`, ausencia de launch en 17A, no Protobuf, no Session Command, no endpoint Master, no auto-discovery y no paths desde Master.

### Cambios descartados

- No se implemento `OPEN_APPLICATION` remoto.
- No se modifico Master Backend Java, Protobuf, Network Gateway, Agent dispatcher remoto, Session Agent, Session Command, installer ni UI.
- No se lanzo ningun proceso ni se resolvio realmente Windows App Paths.
- No se agregaron scans de Program Files, Start Menu, Registry, WMI, discos, procesos, watchers, timers, heartbeat data ni writes idle.
- No se exigio firma Authenticode ni SHA-256 fijo.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ApplicationBinding|FullyQualifiedName~AgentCommandLineTests"` en `agent`: correcto, 47 pruebas Service y 6 pruebas Session sin coincidencia funcional superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`feat(agent): add local application bindings`

## 2026-09-03 - Prompt 17B

### Realizado

- Implementado `OPEN_APPLICATION(applicationId)` productivo del lado Agent/Session sin endpoint ni batch Master.
- Extendidos Protobuf v1 y bindings generados con `OpenApplicationOperationParameters.applicationId`, `OPEN_APPLICATION_V1` y errores remotos de aplicacion.
- Movidos contratos puros de application bindings a `GaltekClassroom.Agent.Shared`: `ApplicationBinding`, `ApplicationLaunchType`, `ApplicationBindingCatalogDocument`, `ApplicationBindingValidator` y converter JSON.
- Conservados en Service `ApplicationBindingStore`, mutaciones, ACL y CLI administrativa.
- Agregado `OpenApplicationOperationHandler` con preflight local: valida `applicationId`, carga catalogo, exige binding existente/enabled, valida estructura y no envia Session Command ante input/catologo/binding invalido.
- Extendidos `SessionCommandClient` y `SessionCommandProtocol` con `OPEN_APPLICATION` que transporta solo `openApplication.applicationId`.
- Agregado `SessionApplicationResolver` read-only/on-demand en Session Agent: no crea, no repara, no escribe ni cachea el catalogo.
- Implementada resolucion `ABSOLUTE_EXE` con revalidacion y `File.Exists` al momento de ejecucion.
- Implementada resolucion `APP_PATHS` segura: solo HKLM App Paths, valor default, Registry64/Registry32 cuando aplica; conflicto entre vistas falla cerrado.
- Agregado `WindowsApplicationLauncher` con `CreateProcessW`, `lpApplicationName` absoluto, `lpCommandLine = null`, sin argumentos, sin handles heredados y cierre inmediato de process/thread handles tras success.
- Actualizado mapping Java minimo para conocer `OPEN_APPLICATION`, `OPEN_APPLICATION_V1`, parametros tipados y errores, sin implementar dispatch batch/controller Master.
- Agregadas pruebas dirigidas de contrato, Session Command, resolver, App Paths, launcher, handler, dedupe y mapping Java.
- Actualizada documentacion de bindings, protocolos, arquitectura, modelo funcional, reglas, estado, decisiones e historial.

### Cambios descartados

- No se implemento endpoint/batch Master `OPEN_APPLICATION`; queda para 17C.
- No se agrego UI, argumentos, documentos, URLs para aplicaciones, working directory configurable, environment configurable, shell, PowerShell, `cmd`, `runas`, UAC intencional, URI/protocol handlers, shortcuts, MSI, UWP/MSIX/AUMID ni auto-discovery.
- No se consulto HKCU App Paths, PATH, Program Files, Start Menu, WindowsApps, uninstall keys, discos, procesos ni Registry real en pruebas.
- No se lanzo ninguna aplicacion real en tests automatizados.
- No se agregaron timers, polling, watchers, heartbeat data, writes idle ni process monitoring.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ApplicationBinding|FullyQualifiedName~OpenApplication|FullyQualifiedName~SessionCommand|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 20 pruebas Session y 105 pruebas Service superadas.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Manual validation pendiente

- En PC descartable: probar binding `ABSOLUTE_EXE`, binding `APP_PATHS` HKLM real, binding disabled, executable removido y Session Agent no disponible.
- Verificar app visible en sesion del usuario, Service sin launch en Session 0, sin UAC intencional, sin path desde Master/Service command y sin retry automatico en duplicado incierto.

### Commit sugerido

`feat(agent): open authorized local applications`

## 2026-09-03 - Prompt 17C

### Realizado

- Implementado `POST /api/classrooms/{classroomId}/open-application` en el Master Backend como dispatch batch-first de `OPEN_APPLICATION(applicationId)`.
- Agregado `OpenApplicationController` y `OpenApplicationDispatchService`, reutilizando `MasterAccessGuard`, storage guard, `DeviceRepository`, `DeviceNetworkBindingRepository`, `MasterPairingService`, `ClientConnectionRegistry`, `BatchOperationService` y `MasterRemoteOperationGateway`.
- Agregado lookup activo `ApplicationDefinitionRepository.findActiveById` para rechazar `ApplicationDefinition` inexistente o archivada/inactiva antes de crear batch.
- La request acepta solo `applicationId` y `targetDeviceIds`; se rechazan paths, executable names, launch types, argumentos, command line, working directory, shell, PowerShell, `cmd`, scripts, URI, shortcut, Registry, account/student/group context, force, timeout y payload libre.
- Se exige que la `ApplicationDefinition` activa este asociada al Classroom mediante `classroom_applications`; app global existente pero no autorizada en esa aula rechaza la request completa con `APPLICATION_NOT_ALLOWED`, sin batch ni sends.
- El batch persiste una unica `BatchOperation` `OPEN_APPLICATION` antes del primer send, con payload minimo `{"schemaVersion":1,"applicationId":"..."}` y targets `PENDING` o `FAILED` por preflight.
- El gateway envia `OpenApplicationOperationParameters.applicationId` tipado y conserva el mismo `operationId` del batch para todos los Agents.
- El preflight por target exige Device del aula, binding de red vigente, trust `PAIRED`, no `REVOKED`, conexion autenticada `ONLINE` y capability `OPEN_APPLICATION_V1`; no exige `SESSION_AGENT_AVAILABLE`.
- Se preservan por target errores Agent-side de bindings/aplicacion, `SESSION_AGENT_UNAVAILABLE`, `SESSION_COMMAND_RESULT_UNKNOWN` y `OPERATION_RESULT_UNKNOWN`.
- Confirmado que SQLite no requiere migracion V6 porque V1/V5 ya aceptan `OPEN_APPLICATION` en `batch_operations.operation_type`.
- Actualizada documentacion de API, arquitectura, modelo funcional, reglas, application bindings, estado, decisiones e historial.

### Cambios descartados

- No se modifico Agent .NET, Session Agent, Protobuf, Session Command, `ApplicationBinding`, resolucion App Paths, Registry, filesystem, `CreateProcessW`, installer ni UI.
- No se agrego inventario remoto de aplicaciones, query de bindings por Device, discovery, scans, capabilities por app, paths desde Master, argumentos, documents, retry automatico ni reconciliacion/status query para `OPEN_APPLICATION`.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=OpenApplicationDispatchControllerTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): dispatch open application batches`

## 2026-09-03 - Prompt 18A

### Realizado

- Implementado `LOCK_INPUT` y `UNLOCK_INPUT` productivo del lado Client sin endpoint/batch Master.
- Actualizado Protobuf v1 con `INPUT_CONTROL_V1`, `INPUT_LOCK_FAILED` e `INPUT_UNLOCK_FAILED`; `LOCK_INPUT`/`UNLOCK_INPUT` conservan `protocolVersion = 1` y no llevan payload funcional.
- Agregada `RemoteOperationLicensePolicy`: todas las operaciones requieren Commercial License activa por default; unica excepcion vigente `UNLOCK_INPUT`.
- Registrados `LockInputOperationHandler` y `UnlockInputOperationHandler` en el Agent Service; ambos envian Session Command tipado y nunca llaman APIs de input.
- Extendidos `SessionCommandClient` y `SessionCommandProtocol` con `LOCK_INPUT`/`UNLOCK_INPUT` sin payload funcional.
- Agregado `WindowsInputBlockCoordinator` en Session Agent con `IWindowsInputBlockApi` testeable y worker dedicado lazy para cumplir same-thread ownership de `BlockInput(TRUE)`/`BlockInput(FALSE)`.
- Implementada API productiva `WindowsInputBlockApi` con `User32.dll BlockInput(BOOL)` exclusivamente.
- Session Agent cleanup solicita unlock desde el coordinator en shutdown y espera de forma acotada.
- `LOCK_INPUT` repetido reasserta en el mismo owner thread; `UNLOCK_INPUT` sin lock activo es success idempotente.
- Si native unlock falla se reporta `INPUT_UNLOCK_FAILED`, pero el worker termina para favorecer recovery por salida de thread/proceso.
- Actualizado mapping Java minimo para `INPUT_CONTROL_V1`, `LOCK_INPUT`, `UNLOCK_INPUT`, `INPUT_LOCK_FAILED` e `INPUT_UNLOCK_FAILED`.
- Documentado input control, thread ownership, estado efimero, `CTRL+ALT+DEL` como escape, excepcion de licencia de unlock, incertidumbre sin retry y validacion manual pendiente.

### Cambios descartados

- No se implemento endpoint Master, batch Master, UI, overlay, mensaje en pantalla, timeout/lease/duration de lock, keyboard-only, mouse-only, hooks globales, drivers, filtros, Raw Input interception, `SendInput`, `SendKeys`, shell, PowerShell, `cmd`, WMI, bloqueo de `CTRL+ALT+DEL`, Task Manager ni persistence de lock.
- No se agrego `LOCK_INPUT` ni `UNLOCK_INPUT` a Local IPC v1.
- No se hicieron pruebas reales con `BlockInput(TRUE)` sobre la PC de desarrollo.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~InputBlock|FullyQualifiedName~InputControl|FullyQualifiedName~SessionCommand|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 35 pruebas Session y 84 pruebas Service superadas.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Manual validation pendiente

- En PC descartable: `LOCK_INPUT` bloquea teclado/mouse en aplicaciones normales.
- `UNLOCK_INPUT` restaura input.
- `LOCK_INPUT -> Agent Service restart -> UNLOCK_INPUT` conserva desbloqueo posible porque Session Agent conserva owner thread.
- `LOCK_INPUT -> Session Agent terminate` libera input por fail-safe de Windows.
- `LOCK_INPUT -> CTRL+ALT+DEL` libera input por escape nativo.
- `LOCK_INPUT` posterior a `CTRL+ALT+DEL` reasserta en owner thread.
- Con licencia Client expirada: `LOCK_INPUT` falla por licencia y `UNLOCK_INPUT` llega al handler.

### Commit sugerido

`feat(agent): add recoverable input control`

## 2026-09-03 - Prompt 18B1

### Realizado

- Agregada operacion Local IPC v1 `GET_MASTER_UNLOCK_AUTHORIZATION`, manteniendo `protocolVersion = 1` y el pipe existente `GaltekClassroom.Agent.v1`.
- Agregado contrato C# `LocalMasterUnlockAuthorization` con respuesta minima `status`, `authorized` y `configured`.
- Agregada evaluacion Agent-side `MasterAuthorizationService.GetUnlockAuthorizationAsync` / `EvaluateUnlock`, separada de `GET_MASTER_AUTHORIZATION`.
- La autorizacion unlock exige Installation Identity disponible, binding existente y estructuralmente valido, `installationId` coincidente y SID real del caller Named Pipe obtenido por el contexto IPC.
- La autorizacion unlock no exige Commercial License Master `ACTIVE` y no lee claims/roles de una licencia invalida para autorizar recovery.
- Binding ausente/corrupto/schema desconocido, mismatch de instalacion, SID distinto, otro administrador o fallo al obtener SID real fallan cerrado con `authorized=false`.
- La respuesta unlock no expone SID, JWT, `LicenseState`, roles, raw claims, `installationId`, rutas, ACLs, username ni key material.
- Java agrega `LocalAgentClient.getMasterUnlockAuthorization()`, `MasterUnlockAuthorizationResponse` y `MasterUnlockAccessGuard.requireUnlockAuthorized()`.
- `MasterUnlockAccessGuard` queda como guard interno para acciones que reducen control y hayan sido declaradas recovery-safe; actualmente solo el futuro `UNLOCK_INPUT`.
- `MasterAccessGuard.requireAuthorized()` y `GET_MASTER_AUTHORIZATION` conservan la semantica administrativa normal con licencia activa.
- Agregados tests dirigidos .NET y Java para autorizacion unlock, independencia de licencia, binding/SID, Local IPC, campos sensibles, no escritura y ausencia de fallback.
- Actualizada documentacion de Local IPC, arquitectura, modelo funcional, reglas, input control, estado, decisiones e historial.

### Cambios descartados

- No se implemento endpoint HTTP de input control, endpoint publico de unlock authorization, `BatchOperation LOCK_INPUT/UNLOCK_INPUT`, dispatch gRPC Master, UI, cache, timers, polling, heartbeat, writes, Protobuf network, Session Command, `RemoteOperationDispatcher`, Session Agent ni cambios de `BlockInput`.
- No se convirtio licencia expirada en permiso para cualquier administrador local.
- No se agrego fallback entre guards ni framework generico de permisos.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~MasterAuthorizationServiceTests|FullyQualifiedName~LocalIpcRequestHandlerTests|FullyQualifiedName~SessionCommandProtocolTests|FullyQualifiedName~LocalIpcFramingTests|FullyQualifiedName~LocalIpcServerTests"` en `agent`: correcto, 58 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `mvn -q "-Dtest=MasterUnlockAccessGuardTest,MasterAccessGuardTest,WindowsNamedPipeLocalAgentClientTest,LocalIpcFramingTest" test` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): add unlock recovery authorization`

## 2026-09-03 - Prompt 18B2

### Realizado

- Agregados endpoints separados `POST /api/classrooms/{classroomId}/input-control/lock` y `POST /api/classrooms/{classroomId}/input-control/unlock`.
- `lock` usa `MasterAccessGuard`; `unlock` usa `MasterUnlockAccessGuard`; ambos corren antes de leer datos escolares.
- Agregado `InputControlDispatchService` con entradas explicitas para `dispatchLock` y `dispatchUnlock`, reutilizando preflight/fanout privado solo para `LOCK_INPUT` y `UNLOCK_INPUT`.
- Request estricta solo con `targetDeviceIds`; se rechazan campos extra y targets vacios, blank o duplicados.
- Preflight por target: Device del aula, binding vigente, trust `PAIRED`, no `REVOKED`, conexion autenticada `ONLINE` e `INPUT_CONTROL_V1`.
- No se exige `SESSION_AGENT_AVAILABLE`, no se consulta Student/Group/assignment y no se evalua licencia comercial del Client en Master.
- Cada request persiste una unica `BatchOperation` antes del primer send, con payload minimo `{"schemaVersion":1}`.
- Gateway reutilizado con `OperationType.LOCK_INPUT`/`UNLOCK_INPUT` sin parametros funcionales; mismo `operationId` por batch.
- Se preservan errores Agent y estados inciertos por target, sin retry automatico ni reconciliacion/status query.
- SQLite no requirio migration nueva porque V1/V5 ya permiten `LOCK_INPUT` y `UNLOCK_INPUT`.
- Documentada semantica efimera de lock, `CTRL+ALT+DEL`, ausencia de current lock state y cierre de Fase 18.

### Cambios descartados

- No se modifico Agent, Session Agent, Protobuf, Local IPC, `BlockInput`, input coordinator, C# ni .NET.
- No se agrego UI, overlay, mensaje en pantalla, status endpoint, heartbeat/polling de lock, scheduler, retry automatico, reconciliacion, lease, timeout, duration, keyboard-only ni mouse-only.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=InputControlDispatchControllerTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): dispatch input control batches`

## 2026-09-03 - Prompt 19A

### Realizado

- Implementado Credential Vault interno Java-only en el Master Backend, sin UI, endpoints HTTP, Protobuf, gRPC ni SQLite migration.
- Agregado `credential-vault.dat` en el Master data directory, separado de `classroom.db`, usando el resolver/override existente.
- Agregado envelope JSON versionado con header tecnico minimo y documento logico completo cifrado.
- Agregado modelo `CredentialVaultEntry` con `WINDOWS_ACCOUNT` y `GOOGLE_ACCOUNT`, metadata cifrada y `password` como secreto.
- Crypto: PBKDF2-HMAC-SHA256 para KEK con salt/work factor versionados, DEK aleatorio de 256 bits, AES-256-GCM para wrapped DEK y AES-256-GCM para el vault.
- Agregado `initialize`, `unlock`, `lock`, `list`, `reveal`, `add`, `update`, `remove` y `changeMasterPassword`.
- Sesion de vault unica, token aleatorio en memoria, expiracion lazy de 5 minutos, invalidacion por nuevo unlock, lock, restart o cambio de master password.
- `list` devuelve metadata sin password; `reveal` devuelve solo el password de la credencial solicitada.
- Cambio de master password re-wrappea el DEK y deja intacto el ciphertext de entries si no cambian.
- Escritura durable con `AtomicFiles`, temp file mismo directorio, fsync, move atomico y ACL best-effort encapsulado.
- Agregados errores operacionales de Credential Vault y tests dirigidos de crypto, init, sesiones, entries, cambio de password, corrupcion y no secret leak.
- Actualizada documentacion de arquitectura, modelo funcional, reglas, decisiones, estado y `docs/security/CREDENTIAL_VAULT.md`.

### Cambios descartados

- No se implemento UI, Tauri/React, endpoint HTTP de reveal, clipboard, export masivo, reset destructivo, Client credential store, PRIMARY/SECONDARY binding, Windows login/switch, Google auto-login, Chrome password extraction, cookies/tokens, browser automation, Protobuf, gRPC ni migrations SQLite.
- No se guardaron passwords en `classroom.db`, BatchOperation, logs, BrowserProfile ni StudentWorkspace metadata.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=CredentialVaultServiceTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): add encrypted credential vault`

## 2026-09-04 - Prompt 19B

### Realizado

- Implementado binding local seguro Client `PRIMARY`/`SECONDARY` -> Windows SID en `managed-windows-accounts.json`.
- Agregado modelo `ManagedWindowsAccountBinding` con `accountId`, `windowsSid`, `accountReference`, `createdAtUtc` y `updatedAtUtc`.
- El documento queda ligado al `installationId` actual y falla cerrado ante mismatch, schema desconocido, JSON corrupto, slots desconocidos/duplicados, SID invalido, SID compartido o campos de secreto.
- Archivo ausente equivale a ambos slots `NOT_CONFIGURED`; list/status no crea ni modifica el archivo.
- Agregado store `IManagedWindowsAccountBindingStore`/`ManagedWindowsAccountBindingStore` con Load/List/Get/Add/Replace/Remove, escritura durable con `DurableFileWriter`, ACL local y verificacion posterior.
- Agregado ACL de archivo con `LocalSystem` y `Builtin Administrators` `FullControl`, sin read/write explicito para usuarios normales.
- Actualizado `IWindowsAccountResolver`/`WindowsAccountResolver` para usar APIs nativas `LookupAccountNameW`, `LookupAccountSidW`, `ConvertSidToStringSidW` y `ConvertStringSidToSidW`.
- Bind/replace acepta solo `SidTypeUser`, canonicaliza `accountReference` por lookup inverso y normaliza nombres cortos como `<MACHINE>\Nombre`.
- Agregado status local de ambos slots con `configured`, `accountReference`, `credentialConfigured=false` y estados `NOT_CONFIGURED`, `CREDENTIAL_NOT_CONFIGURED` o `ACCOUNT_NOT_FOUND`.
- Agregada CLI local: `--managed-account-list`, `--managed-account-bind <PRIMARY|SECONDARY> <WINDOWS_ACCOUNT>`, `--managed-account-remove <PRIMARY|SECONDARY>` y `--replace-managed-account-binding`.
- Mutaciones requieren consola elevada, no autoelevan y no aceptan parametros de password/credential/secret/token/PIN.
- Agregados tests dirigidos de store, resolver/normalizacion, bind/replace/remove, rename/delete/recreate, status, secretos y CLI.
- Documentado `docs/windows/MANAGED_WINDOWS_ACCOUNTS.md` y actualizados arquitectura, modelo funcional, reglas, decisiones y estado.

### Cambios descartados

- No se implemento password, password hash, DPAPI, Client credential store, Credential Vault integration, Master HTTP API, Java productivo, Protobuf, gRPC, Local IPC, Session Agent, browser policy integration, Windows Session State, login, logout, switch, Credential Provider, creacion/borrado/renombre de cuentas Windows, scans, WMI, timers ni polling.
- No se mostro SID por default en salida CLI.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ManagedWindowsAccount|FullyQualifiedName~WindowsAccountResolver|FullyQualifiedName~AgentCommandLineTests|FullyQualifiedName~OperationContractsTests|FullyQualifiedName~MasterBindingConfigurationServiceTests"` en `agent`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Commit sugerido

`feat(agent): bind managed windows accounts`

## 2026-09-04 - Prompt 19C

### Realizado

- Implementado `GET_WINDOWS_SESSION_STATE` productivo del lado Agent Service como operacion remota read-only y on-demand.
- Agregado `WindowsConsoleSessionResolver` con autoridad en `WTSGetActiveConsoleSessionId()`: `0xFFFFFFFF` y Session 0 devuelven `UNKNOWN`.
- `WTSUserName` se usa solo como senal interna de presencia; username vacio produce `NO_SESSION` y nunca mapea identidad.
- Agregado manejo acotado de LocalSystem + `SeTcbPrivilege` antes de `WTSQueryUserToken`.
- El token de usuario se usa solo para leer `TokenUser`, convertir el SID y cerrar/liberar handles y buffers nativos.
- Agregado `WindowsSessionStateService` para comparar SID activo contra `managed-windows-accounts.json`.
- Mapping implementado: `NO_SESSION`, `PRIMARY_ACTIVE`, `SECONDARY_ACTIVE`, `OTHER_SESSION_ACTIVE` y `UNKNOWN`.
- Archivo de bindings ausente + usuario real produce `OTHER_SESSION_ACTIVE`; catalogo corrupto/schema/mismatch falla cerrado como `MANAGED_ACCOUNT_BINDINGS_INVALID`.
- Agregado handler `GetWindowsSessionStateOperationHandler` registrado en `RemoteOperationDispatcher`.
- Protobuf v1 agrega `WindowsSessionState`, `WindowsSessionStateResult`, result oneof tipado, `WINDOWS_SESSION_STATE_V1`, `WINDOWS_SESSION_UNKNOWN` y `MANAGED_ACCOUNT_BINDINGS_INVALID`, manteniendo `protocolVersion = 1`.
- `ClientCapabilityProvider` anuncia `WINDOWS_SESSION_STATE_V1`.
- Java actualiza mapping minimo de capability, operation type, error codes y resultado tipado interno del gateway; no agrega endpoint ni batch 19C.
- Agregados tests dirigidos de resolver, SID mapping, locked/disconnected conceptual mediante fake, Protobuf/result privacy, handler, dedupe/licencia y Java mapping.
- Documentado `docs/windows/WINDOWS_SESSION_STATE.md` y actualizados protocolo, arquitectura, modelo funcional, reglas, decisiones, estado y managed accounts.

### Cambios descartados

- No se implemento password, credential store, DPAPI, provisioning, login, logoff, switch, `CreateProcessAsUser`, Credential Provider, Session Agent changes, Session Command changes, Local IPC changes, heartbeat session state, polling, WMI, process scans, browser policy account integration, Master HTTP endpoint, BatchOperation Master, UI, RDP management, token duplication ni token persistence.
- No se exponen SID, username, domain, accountReference, sessionId, token handle ni profile path en el resultado remoto.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionState|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport|FullyQualifiedName~ManagedWindowsAccountBindingStore"` en `agent`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Validacion manual pendiente

- En PC descartable: sin usuario `NO_SESSION`; `PRIMARY` `PRIMARY_ACTIVE`; bloqueo de Windows conserva `PRIMARY_ACTIVE`; cambio manual a `SECONDARY` produce `SECONDARY_ACTIVE`; usuario no administrado produce `OTHER_SESSION_ACTIVE`; rename por mismo SID conserva mapping; historica/disconnected de `PRIMARY` no reemplaza consola `SECONDARY`; Session Agent detenido no bloquea la consulta.

### Commit sugerido

`feat(agent): observe managed windows session state`

## 2026-09-04 - Prompt 19D

### Realizado

- Implementado Client secure credential store para passwords Windows administradas `PRIMARY`/`SECONDARY`.
- Agregado `managed-windows-credentials.dat` en el data directory del Agent, separado de `managed-windows-accounts.json` y `credential-vault.dat`.
- El envelope externo versionado conserva solo `schemaVersion`, `installationId`, `accountId`, `protectedData` y timestamps.
- Agregado payload binario protegido con `accountId`, `windowsSid` y password UTF-16LE; SID y password no quedan en plaintext fuera del DPAPI blob.
- Agregada abstraccion `IManagedWindowsCredentialProtector` y protector productivo DPAPI con `CryptProtectData`/`CryptUnprotectData`.
- DPAPI productivo exige Windows + LocalSystem, usa scope de usuario actual, `CRYPTPROTECT_UI_FORBIDDEN`, optional entropy por instalacion/slot y no usa LocalMachine ni fallback plaintext.
- Agregado `IManagedWindowsCredentialStore`/`ManagedWindowsCredentialStore` con GetStatus/Add/Replace/Remove/Acquire.
- `Add`/`Replace` exigen binding vigente, catalogo valido, SID valido y resoluble como `SidTypeUser`; sin binding devuelve `ACCOUNT_NOT_CONFIGURED` y cuenta borrada `ACCOUNT_NOT_FOUND`.
- `Acquire` compara el SID del payload protegido contra el binding vigente; rebind a otro SID deja la credencial vieja logicamente no usable.
- Agregado `ManagedWindowsCredentialLease` disposable con buffer mutable UTF-16LE y limpieza por `Dispose`.
- Agregado ACL especifico para `managed-windows-credentials.dat` con `LocalSystem` y `Builtin Administrators` `FullControl`, sin read/write explicito para usuarios normales.
- Agregados error codes compartidos internos para `MANAGED_CREDENTIAL_STORE_INVALID` y `MANAGED_CREDENTIAL_PROTECTION_FAILED`.
- Agregados tests dirigidos de store, envelope, corrupcion, plaintext, binding/SID, rebind, entropy, LocalSystem/flags DPAPI, memoria sensible, ACL y API sin reveal.
- Creado `docs/windows/MANAGED_WINDOWS_CREDENTIALS.md` y actualizados arquitectura, modelo funcional, reglas, decisiones, estado, managed accounts, session state y Credential Vault.

### Cambios descartados

- No se implemento provisioning remoto, Protobuf credential operation, gRPC password transport, Local IPC credential methods, Credential Vault integration, Master API, BatchOperation, Client capability, login, logoff, switch, Credential Provider, `LogonUser`, password verification, password change, account creation, autologon, Session Agent password handling, CLI password args, reveal, export, browser automation, heartbeat credential state, startup scan, polling ni timers.
- `--managed-account-list` sigue siendo binding-only y no intenta descifrar DPAPI desde consola administrativa normal.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ManagedWindowsCredential|FullyQualifiedName~ManagedWindowsAccount|FullyQualifiedName~AgentCommandLineTests|FullyQualifiedName~OperationContractsTests"` en `agent`: correcto, 112 pruebas Service y 6 pruebas Session superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Validacion manual pendiente

- Service corriendo como LocalSystem; provisionar `PRIMARY` desde futura operacion 19E; confirmar que `managed-windows-credentials.dat` no contiene password/SID plaintext; reiniciar Service y confirmar credencial usable; copiar store a otra instalacion y confirmar fail closed; rebind a otro SID y confirmar credentialConfigured false; re-provisionar y confirmar READY; verificar que usuario Windows estandar no puede leer el archivo.

### Commit sugerido

`feat(agent): protect managed windows credentials`

## 2026-09-04 - Prompt 19E1

### Realizado

- Implementada operacion remota tipada `PROVISION_MANAGED_CREDENTIAL` para provisionar/reemplazar la credencial almacenada por Galtek Client para `PRIMARY`/`SECONDARY`.
- Protobuf v1 agrega `ManagedWindowsAccountId`, `ProvisionManagedCredentialOperationParameters(account_id, password_utf16le)`, capability `MANAGED_CREDENTIAL_PROVISIONING_V1` y errores estructurados de binding/store/proteccion.
- `password_utf16le` viaja como bytes UTF-16LE sin BOM/NUL; no se agrega `string password`.
- Agregado `ProvisionManagedCredentialOperationHandler` en Agent Service/LocalSystem, registrado en `RemoteOperationDispatcher`.
- El handler valida parametros, accountId, password no vacio, bytes pares y maximo vigente, copia el secreto a buffer mutable y lo limpia con `CryptographicOperations.ZeroMemory` en `finally`.
- `ManagedWindowsCredentialStore` agrega ruta `AddUtf16LittleEndianAsync`/`ReplaceUtf16LittleEndianAsync` para construir payload DPAPI desde bytes sin decodificar a string.
- Provisioning reutiliza binding local, validacion SID `SidTypeUser`, `ManagedWindowsCredentialStore` y `WindowsDpapiManagedWindowsCredentialProtector`.
- `SUCCESS` significa DPAPI protect, persistencia durable y verificacion del store; no valida password contra Windows ni cambia la password real.
- `ClientCapabilityProvider` anuncia `MANAGED_CREDENTIAL_PROVISIONING_V1`.
- El dedupe del Agent deja de conservar `OperationRequest` completo; retiene una firma no secreta y el `OperationResult`.
- Para provisioning, la firma de dedupe usa solo metadata no secreta: operationId, tipo, targetDeviceId, protocolVersion y accountId.
- Mismo `operationId + accountId` devuelve resultado cacheado sin reaplicar el secreto, incluso con bytes distintos; mismo operationId con otro accountId conserva conflicto.
- Java agrega `PROVISION_MANAGED_CREDENTIAL`, capability, mapping de capability, errores remotos y metodo explicito `MasterRemoteOperationGateway.provisionManagedCredential(...)`.
- Agregados tests dirigidos .NET de contrato, handler, store por bytes, memoria sensible, dedupe secret-safe, capability y licencia.
- Agregados tests dirigidos Java de gateway/proto, bytes, accountId tipado, ausencia de campos SID/username/credentialId/vault token, no persistencia/logging en gateway y timeout unknown.
- Creado `docs/windows/MANAGED_CREDENTIAL_PROVISIONING.md` y actualizados protocolo, arquitectura, modelo funcional, reglas, decisiones, estado, managed accounts, session state, managed credentials y Credential Vault.

### Cambios descartados

- No se implemento Credential Vault bridge real, endpoint HTTP Master, BatchOperation, SQLite migration, Local IPC credential op, Session Command password, Session Agent password handling, login, logoff, switch, Credential Provider, `LogonUser`, LSA, Winlogon, `CreateProcessAsUser`, password verification, Windows password change, password hash/fingerprint/checksum, receipt, reconciliation, status query nuevo, auto retry, UI, clipboard ni reveal.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ProvisionManagedCredential|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~ManagedWindowsCredential|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 100 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Validacion manual pendiente

- En PC descartable: provisionar `PRIMARY` y `SECONDARY` desde Master sobre mTLS real, verificar que `managed-windows-credentials.dat` no contiene password/SID plaintext, confirmar `READY` cuando binding/SID siguen validos, reiniciar Service y confirmar persistencia, re-provisionar para rotacion, y confirmar que duplicados por operationId no reaplican el secreto.

### Commit sugerido

`feat(agent): provision managed credentials securely`

## 2026-09-04 - Prompt 19E2

### Realizado

- Agregado `ManagedCredentialProvisioningBridge` en el Master Backend como primitive interna Java-only.
- El bridge conecta Credential Vault -> `MasterRemoteOperationGateway.provisionManagedCredential(...)` para un solo `deviceId` explicito.
- La entrada queda limitada a `vaultSessionToken`, `credentialId`, `deviceId`, `operationId` y `PRIMARY`/`SECONDARY`; no acepta password ni master password desde el caller.
- `MasterAccessGuard.requireAuthorized()` corre antes de tocar el vault; `MasterUnlockAccessGuard` no participa.
- Se valida sesion/metadata del vault antes de extraer secreto, se exige `WINDOWS_ACCOUNT` y `GOOGLE_ACCOUNT` se rechaza con `CREDENTIAL_NOT_PROVISIONABLE`.
- `credentialId` y vault token permanecen dentro del Master; no se envian al gateway/protobuf ni se persisten.
- La password se obtiene exclusivamente del vault, se codifica con `StandardCharsets.UTF_16LE` sin BOM/NUL y el `byte[]` controlado se limpia en `finally`.
- Agregado `CredentialVaultService.readInternal(...)` package-private para lectura interna sin audit de reveal humano.
- Agregado error interno `CREDENTIAL_NOT_PROVISIONABLE` sin tocar Protobuf.
- Agregados tests dirigidos de autorizacion, vault locked/session expirada/missing, enforcement `WINDOWS_ACCOUNT`, rechazo Google, UTF-16LE exacto/Unicode/no BOM/no NUL, limpieza de bytes en success/failure/timeout, propagation de success/failure/unknown y no retry.
- Actualizados docs de arquitectura, modelo funcional, reglas, estado, decisiones, Credential Vault y managed credentials.

### Cambios descartados

- No se agrego endpoint HTTP, UI, BatchOperation, fanout, provisioning masivo, startup provisioning, SQLite migration, Protobuf, Local IPC, Agent C#, Session Agent, login, logoff, switch, reveal humano, retry automatico ni status query nuevo.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=ManagedCredentialProvisioningBridgeTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=ManagedCredentialProvisioningBridgeTest,CredentialVaultServiceTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): bridge vault credential provisioning`

## 2026-09-04 - Prompt 19F

### Realizado

- Implementada operacion remota tipada `LOGOFF_WINDOWS_SESSION` para cerrar solo una sesion Windows administrada esperada en la consola fisica del Client.
- Protobuf v1 agrega `LogoffWindowsSessionOperationParameters(account_id)`, capability `WINDOWS_SESSION_LOGOFF_V1` y errores `WINDOWS_SESSION_CHANGED`/`WINDOWS_LOGOFF_FAILED`.
- `account_id` acepta solo `PRIMARY`/`SECONDARY`; `UNSPECIFIED` se rechaza.
- El contrato no transporta username, domain, SID, `accountReference`, password, sessionId, force, timeout, command, args, shell ni payload arbitrario.
- Agregado `WindowsSessionLogoffService`, `LogoffWindowsSessionOperationHandler`, `IWindowsSessionLogoffController` y `WindowsSessionLogoffController`.
- El Agent Service valida Installation Identity y binding local `managed-windows-accounts.json`; sin binding devuelve `ACCOUNT_NOT_CONFIGURED`, binding invalido devuelve `MANAGED_ACCOUNT_BINDINGS_INVALID`.
- La identidad se observa por `WTSGetActiveConsoleSessionId()` + `WTSQueryUserToken` + `GetTokenInformation(TokenUser)` y se compara por SID real contra el binding esperado.
- Si `NO_SESSION`, devuelve `SUCCESS` idempotente y no llama WTS logoff.
- Si otra sesion esta activa, devuelve `WINDOWS_SESSION_CHANGED` y no la cierra.
- Antes del logoff destructivo se repite la observacion y se exige mismo `sessionId` y mismo SID esperado.
- La implementacion productiva llama `WTSLogoffSession(WTS_CURRENT_SERVER_HANDLE, sessionId, FALSE)`.
- `SUCCESS` significa solicitud WTS aceptada, no cierre observado; no hay polling posterior.
- `WTSLogoffSession` false devuelve `WINDOWS_LOGOFF_FAILED`.
- El dedupe del Agent usa firma `operationId + accountId`; duplicados iguales no repiten WTS logoff y mismo operationId con otro accountId conserva conflicto.
- `ClientCapabilityProvider` anuncia `WINDOWS_SESSION_LOGOFF_V1`.
- Java agrega `MasterRemoteOperationGateway.logoffWindowsSession(...)`, mapping `OperationType.LOGOFF_WINDOWS_SESSION`, mapping de capability y errores.
- Agregados tests dirigidos .NET de contrato, capability, handler registrado, licencia, session safety, race, WTS y dedupe.
- Agregados tests dirigidos Java de gateway, accountId, ausencia de campos sensibles, capability mapping, errores e incertidumbre por timeout.
- Creado `docs/windows/WINDOWS_SESSION_LOGOFF.md` y actualizados protocolo, arquitectura, modelo funcional, reglas, estado, decisiones, managed accounts y session state.

### Cambios descartados

- No se agrego endpoint HTTP, BatchOperation, fanout, planner Master, UI, `LOGON_MANAGED_ACCOUNT`, `SWITCH_MANAGED_ACCOUNT`, Credential Provider, password usage, DPAPI, Credential Vault, Session Agent, RDP control, force flag, timeout configurable, polling, heartbeat status, status query nuevo ni reconciliation.
- No se uso shell, PowerShell, `cmd`, `logoff.exe`, WMI, `ExitWindowsEx` ni SendKeys.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionLogoff|FullyQualifiedName~WindowsSessionState|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~OperationContracts|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 93 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(agent): log off managed windows sessions`

## 2026-09-05 - Prompt 19G1

### Realizado

- Agregado proyecto nativo `agent/native/GaltekClassroom.CredentialProvider/` para Credential Provider V2.
- Implementado COM plumbing minimo con CLSID `{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}`.
- Implementadas interfaces `ICredentialProvider`, `ICredentialProviderCredential` e `ICredentialProviderCredential2`.
- `SetUsageScenario` acepta solo `CPUS_LOGON`; otros escenarios fallan seguro.
- El provider no implementa `ICredentialProviderFilter` y no oculta Password/PIN/Windows Hello/otros providers.
- Sin activation, y aun con activation 19G1, el provider devuelve 0 credentials productivas.
- `GetSerialization` queda seguro con `CPGSR_NO_CREDENTIAL_NOT_FINISHED`, sin password ni serialization autenticable.
- Agregado bridge local dedicado `GaltekClassroom.CredentialProvider.v1`, separado de Local IPC v1 y Session Command v1.
- El Agent Service actua como servidor y el provider como cliente local; no hay red, HTTP, gRPC local ni Protobuf.
- El pipe tiene ACL solo `LocalSystem`.
- El Service valida PID real del pipe con `GetNamedPipeClientProcessId`, proceso real, path canonico `%SystemRoot%\System32\LogonUI.exe`, sesion interactiva y token real LocalSystem.
- El contrato JSON UTF-8 versionado soporta solo `PING` y `GET_PENDING_ACTIVATION_METADATA`, con framing big-endian y limite de 8 KiB.
- El request rechaza hints de PID/SID/username/process y no puede autorizarse por payload.
- Agregado modelo `CredentialProviderActivation` efimero, in-memory, una activation por Client, `PRIMARY`/`SECONDARY`, TTL default 45s, maximo 60s, expiracion lazy y perdida por restart.
- No se agregaron campos password, protectedData, credentialId, vault token ni master password.
- No se llama `ManagedWindowsCredentialStore.Acquire()`.
- Agregados scripts lab-only `register-credential-provider-dev.ps1` y `unregister-credential-provider-dev.ps1`.
- El registro lab apunta a Program Files y no registra Credential Provider Filter.
- Actualizados docs de Credential Provider, arquitectura, modelo funcional, reglas, estado, decisiones, installer, managed accounts, managed credentials, session state y logoff.

### Cambios descartados

- No se implemento `LOGON_MANAGED_ACCOUNT`, `SWITCH_MANAGED_ACCOUNT`, remote `OperationRequest`, endpoint Master, BatchOperation, planner, UI, Protobuf ni gRPC.
- No se implemento transporte de password Service -> Provider, credential lease, `ManagedWindowsCredentialStore.Acquire()`, `KERB_INTERACTIVE_UNLOCK_LOGON`, LSA call ni serialization real.
- No se uso autologon Registry, `DefaultPassword`, `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogonW`, SendKeys, UI Automation, PowerShell, `cmd`, scripts, RDP ni APIs WinStation no documentadas.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~CredentialProviderBridge"` en `agent`: correcto, 24 pruebas Service superadas; Session sin coincidencias.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider/GaltekClassroom.CredentialProvider.vcxproj`: correcto, 0 advertencias, 0 errores.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj`: correcto, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.
- `rg` dirigido sobre provider/bridge/scripts para APIs prohibidas/filtro/provider network APIs: sin coincidencias en codigo productivo 19G1.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.

### Validacion manual pendiente

- En PC descartable: copiar DLL a `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\`, confirmar ACL sin write para usuarios estandar, registrar con script lab, bloquear/cerrar sesion, confirmar providers estandar visibles, confirmar que Galtek no autentica sin activation, detener Agent Service y verificar login estandar, desregistrar y confirmar desaparicion del provider.

### Commit sugerido

`feat(agent): add credential provider foundation`

## 2026-09-05 - Prompt 19G2

### Realizado

- Agregada identity local para activations existentes mediante `GET_PENDING_ACTIVATION_IDENTITY` en `GaltekClassroom.CredentialProvider.v1`.
- El Agent Service deriva identity desde activation `accountId`, Installation Identity, binding local `managed-windows-accounts.json`, SID valido y resolver Windows `SidTypeUser`.
- La respuesta identity incluye `activationId`, `accountId`, `userSid`, `domain` y `username`; no usa `accountReference` como autoridad.
- La primera identity exitosa fija `expectedWindowsSid` efimero en la activation.
- Agregado estado efimero `PENDING` / `IDENTITY_RESOLVED` / `CONSUMED`.
- Agregado `ACQUIRE_PENDING_CREDENTIAL(activationId)` como operacion one-time secreta local, sin aceptar accountId, SID, username, domain, password, credentialId, token, PID, proceso, sessionId ni payload arbitrario.
- Antes de acquire, el Service relee binding vigente y exige match con `expectedWindowsSid`; rebind entre identity y acquire falla cerrado.
- La activation se marca `CONSUMED` antes de llamar `ManagedWindowsCredentialStore.AcquireForWindowsSidAsync(...)`; segundo acquire no llama DPAPI.
- Agregado `AcquireForWindowsSidAsync` al Client credential store para reforzar el expected SID dentro de la ruta DPAPI.
- La respuesta secreta es binaria, versionada y acotada dentro del framing big-endian: magic/version/status/activationId/passwordByteLength/password UTF-16LE.
- Password Service -> Provider no viaja como JSON, Base64, hexadecimal, XML, Protobuf ni string de contrato.
- El provider nativo enumera 0 credentials sin activation/identity valida y 1 credential Galtek con activation + identity valida.
- La tile Galtek no contiene field password visible/editable y `SetSelected` no solicita auto-logon.
- `GetUserSid` devuelve el SID obtenido desde el Agent Service con ownership COM.
- `GetSerialization` adquiere password una sola vez, valida activationId, usa buffers RAII, protege password con `CredProtectW`, construye `KERB_INTERACTIVE_UNLOCK_LOGON` con `KerbInteractiveLogon`, resuelve `Negotiate` via LSA y entrega serialization con CLSID Galtek vigente.
- Fallos despues de acquire no restauran activation y no reintentan.
- El provider limpia receive buffer, plaintext y buffers intermedios propios con `SecureZeroMemory`; el Service dispone leases y zeroiza payload binario propio despues del write.
- `Advise`/`UnAdvise` del provider y credential manejan ownership COM sin background listener.
- Actualizados docs de arquitectura, modelo funcional, reglas, estado, decisiones, Credential Provider, managed accounts y managed credentials.

### Cambios descartados

- No se agrego `LOGON_MANAGED_ACCOUNT` remoto, capability remota, Protobuf, OperationRequest remoto, endpoint HTTP, BatchOperation, fanout, planner, Master Java, Session Agent, SWITCH, notification remoto, auto-logon remoto, UI ni background polling.
- No se agrego Credential Provider Filter ni ocultamiento de providers estandar.
- No se agrego CLI de password, Local IPC reveal, Session Command credential, Registry autologon, `DefaultPassword`, `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogonW`, shell, PowerShell, `cmd`, WinHTTP/WinINet ni sockets.
- No se ejecuto login real ni registro automatico del DLL en esta maquina.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~CredentialProviderBridge|FullyQualifiedName~CredentialProviderActivation|FullyQualifiedName~ManagedWindowsCredential"` en `agent`: correcto, 73 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider/GaltekClassroom.CredentialProvider.vcxproj`: correcto, 0 advertencias, 0 errores.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj`: correcto, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.
- `rg` dirigido sobre provider/bridge/scripts para APIs prohibidas/filtro/provider network APIs: sin coincidencias productivas; solo aparece la asercion del self-test que confirma que Galtek no implementa `ICredentialProviderFilter`.

### Validacion manual pendiente

- En PC descartable/laboratorio: instalar provider lab, confirmar ACL sin write para usuarios estandar, crear activation solo cuando exista mecanismo seguro en 19G3, confirmar tile Galtek solo con activation, confirmar providers estandar visibles, intentar login controlado con password correcta/incorrecta, confirmar que segundo intento exige nueva activation, detener Agent Service y verificar login estandar.

### Commit sugerido

`feat(agent): serialize managed logon credentials`

## 2026-09-05 - Prompt 19G3

### Realizado

- Agregado `LOGON_MANAGED_ACCOUNT` remoto tipado con `LogonManagedAccountOperationParameters(account_id)` solo para `PRIMARY`/`SECONDARY`.
- Agregada capability especifica `WINDOWS_SESSION_LOGON_V1` en Agent y Master.
- El Agent registra `LogonManagedAccountOperationHandler` y `WindowsSessionLogonService` sobre el `RemoteOperationDispatcher` normal.
- Preflight de sesion: target ya activo devuelve `SUCCESS` sin activation/DPAPI; `NO_SESSION` continua; otra sesion activa devuelve `WINDOWS_SESSION_CHANGED`; estado no confiable devuelve `WINDOWS_SESSION_UNKNOWN`.
- Preflight de cuenta antes de activation: binding local, SID `SidTypeUser` y credential DPAPI usable ligada al mismo SID.
- Revalidacion inmediata de `NO_SESSION` antes de crear activation remota.
- Activation remota efimera in-memory con `operationId`, `accountId`, timestamps, TTL y `autoSubmitRequested=true`.
- Concurrencia remota fail-closed: una activation remota de otro `operationId` devuelve `WINDOWS_LOGON_BUSY`; duplicados de misma operacion/cuenta conservan idempotencia.
- Agregado generation counter in-memory y listener count en `CredentialProviderActivationStore`.
- Agregado pipe op `WAIT_FOR_ACTIVATION_CHANGE(observedGeneration)` para notification event-driven sin polling sano.
- Agregado pipe op `REPORT_LOGON_RESULT(activationId,outcome)` con outcomes cerrados `SUCCESS`, `FAILED` y `LOCAL_SERIALIZATION_FAILED`.
- El Provider nativo inicia worker cancellable en `Advise`, usa COM marshaling inter-thread para `ICredentialProviderEvents` y llama `CredentialsChanged` ante cambios de generation.
- El Agent espera brevemente listener LogonUI validado antes de activation y devuelve `CREDENTIAL_PROVIDER_UNAVAILABLE` si no existe.
- Auto-submit remoto: `GetCredentialCount` devuelve default credential 0 y `pbAutoLogonWithDefault=TRUE`; `SetSelected` auto-submit exactly once.
- `GetSerialization` reporta `LOCAL_SERIALIZATION_FAILED` si falla localmente despues de acquire.
- `ReportResult(STATUS_SUCCESS)` reporta `SUCCESS`; auth rejection reporta `FAILED`; no hay retry/reacquire.
- `WindowsSessionLogonService` mapea `SUCCESS` a `OperationResult SUCCESS`, `FAILED`/`LOCAL_SERIALIZATION_FAILED` a `WINDOWS_LOGON_FAILED` y timeout a `WINDOWS_LOGON_NOT_CONFIRMED`.
- El Master Java agrega `MasterRemoteOperationGateway.logonManagedAccount(...)`, mappings de capability/errores y timeout fijo de resultado de 55s solo para logon.
- Actualizados docs de protocolo, arquitectura, modelo funcional, reglas, estado, decisiones, Credential Provider, managed accounts, managed credentials, session state y nuevo `WINDOWS_SESSION_LOGON.md`.

### Cambios descartados

- No se agrego endpoint HTTP, BatchOperation, fanout, planner, UI ni `SWITCH_MANAGED_ACCOUNT`.
- No se envio password desde Master ni username, domain, SID, sessionId, credentialId, vault token, command, args, shell ni payload arbitrario.
- No se agrego capability generica de session-control.
- No se implemento unlock, switch ni logoff implicito antes de logon.
- No se agrego SendKeys, UI Automation, Registry autologon, `DefaultPassword`, `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogonW`, PowerShell, `cmd`, scripts ni APIs WinStation no documentadas.
- No se cambio el timeout global de operaciones remotas del Master.
- No se agrego retry automatico ni reconciliation para logon.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionLogon|FullyQualifiedName~CredentialProviderBridge|FullyQualifiedName~CredentialProviderActivation|FullyQualifiedName~WindowsSessionState|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~OperationContracts|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 136 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj` con Visual Studio BuildTools: correcto, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.

### Validacion manual pendiente

- En PC descartable: instalar provider lab, provisionar credential, dejar consola sin usuario, ejecutar `LOGON_MANAGED_ACCOUNT PRIMARY/SECONDARY`, confirmar auto-submit once, confirmar resultado con password correcta/incorrecta, confirmar timeout si se bloquea confirmacion y confirmar que providers estandar siguen disponibles con Service detenido.

### Commit sugerido

`feat(agent): add remote managed account logon`

## 2026-09-05 - Prompt 19G4

### Realizado

- Agregado `SWITCH_MANAGED_ACCOUNT` remoto tipado con `SwitchManagedAccountOperationParameters(account_id)` solo para target `PRIMARY`/`SECONDARY`.
- Agregada capability especifica `WINDOWS_SESSION_SWITCH_V1` en Agent y Master.
- Agregado error estructurado `WINDOWS_SWITCH_NOT_CONFIRMED` para logoff aceptado sin confirmacion de `NO_SESSION`.
- El Agent registra `SwitchManagedAccountOperationHandler` y `WindowsSessionSwitchService` sobre el `RemoteOperationDispatcher` normal.
- Source se deriva solo desde `WindowsSessionState` local (`PRIMARY_ACTIVE`/`SECONDARY_ACTIVE`); Master no puede enviarlo.
- Target ya activo devuelve `SUCCESS` idempotente sin logoff, logon, DPAPI ni activation.
- `NO_SESSION` reutiliza `WindowsSessionLogonService`.
- Opposite managed activo ejecuta preflight target estructural antes de cerrar source: binding, SID `SidTypeUser` y credential DPAPI usable.
- Despues del preflight, el Agent relee estado y exige que source siga siendo exactamente la derivada.
- El tramo destructivo reutiliza `WindowsSessionLogoffService` expected-account con double-check de `sessionId + SID` y WTS logoff productivo.
- Agregada espera local/acotada post-logoff solo durante SWITCH explicito, sin timer permanente, polling idle, WMI ni writes periodicos.
- SWITCH no inicia target logon hasta confirmar `NO_SESSION`; target activo durante la espera produce `SUCCESS`; otra sesion aborta sin tocarla.
- El target logon reutiliza LOGON 19G3 con espera real de LogonUI/Credential Provider despues de `NO_SESSION`, revalidacion de `NO_SESSION`, activation, auto-submit one-shot y `ReportResult` authority.
- `CREDENTIAL_PROVIDER_UNAVAILABLE` despues de `NO_SESSION` es un posible efecto parcial: source pudo quedar cerrada y no hay rollback automatico.
- Agregado timeout Master especifico de 75s para SWITCH sin cambiar el timeout global ni el timeout de LOGON.
- Agregado `MasterRemoteOperationGateway.switchManagedAccount(...)` y mappings Java de operation, capability y error.
- Agregadas pruebas .NET de matriz, target preflight, carreras, fallos parciales, cancelacion y dedupe.
- Agregadas pruebas Java de gateway PRIMARY/SECONDARY, request sin source/secrets, capability mapping, error mapping, timeout especifico y timeout/transporte unknown.
- Actualizados docs de protocolo, arquitectura, modelo funcional, reglas, estado, decisiones, session state/logon/logoff, managed accounts y nuevo `WINDOWS_SESSION_SWITCH.md`.

### Cambios descartados

- No se agrego endpoint HTTP, BatchOperation, fanout, planner, UI ni SQLite.
- No se orquesto desde Master como `LOGOFF` + wait + `LOGON`; SWITCH es una unica operacion Agent-side.
- No se enviaron password, username, domain, SID, source account, sessionId, credentialId, vault token, force, timeout configurable, command, args, shell ni payload arbitrario.
- No se cerro `OTHER_SESSION_ACTIVE`.
- No se asumio que WTS logoff accepted equivale a `NO_SESSION`.
- No se exigio listener LogonUI/Credential Provider antes de cerrar la source.
- No se agrego rollback automatico a source ni retry automatico.
- No se agrego journal, receipt ni ampliacion de `OperationStatusQuery`.
- No hubo cambios C++ ni build nativo.
- No se hizo commit.

### Validaciones

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionSwitch|FullyQualifiedName~WindowsSessionLogon|FullyQualifiedName~WindowsSessionLogoff|FullyQualifiedName~WindowsSessionState|FullyQualifiedName~CredentialProviderActivation"` en `agent`: correcto, 104 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(agent): switch managed windows accounts`

## 2026-09-06 - Prompt 19H2

### Realizado

- Agregado `POST /api/operations/{operationId}/retry` para retry administrativo explicito y selectivo solo de `BatchOperation` `SWITCH_MANAGED_ACCOUNT`.
- La request de retry acepta solo `targetDeviceIds`, obligatorio, no vacio, sin blanks, sin duplicados y maximo 100.
- Se rechazan campos extra como `targetAccountId`, source account, password, username, SID, sessionId, credentialId, vault token, force, allFailed, groupId, studentId, allDevices, timeout, command, args y payload generico.
- `MasterAccessGuard.requireAuthorized()` corre antes de leer storage o iniciar trabajo remoto; no se usa `MasterUnlockAccessGuard`.
- El retry conserva `operationId`, `createdAtUtc`, `requestedBy`, `targetCount` y payload del batch original.
- `targetAccountId` se obtiene solo del payload durable original `schemaVersion = 1`.
- Elegibilidad por target: pertenecer al batch original, estado actual `FAILED`, `errorCode` no nulo y `ErrorCode.retryable() == true`.
- `SUCCESS`, `NO_CHANGE`, `PENDING`, targets ajenos, errores no retryable y `OPERATION_RESULT_UNKNOWN` de switch rechazan toda la request antes del fanout.
- Agregado claim transaccional all-or-nothing `FAILED -> PENDING` con `attempt + 1` y versionado optimista; conflictos devuelven `CONCURRENT_MODIFICATION` sin trabajo remoto.
- Cada retry ejecuta preflight tecnico fresco y snapshot fresco `GET_WINDOWS_SESSION_STATE` con operationId remoto nuevo.
- Si el snapshot fresco ya coincide con target, se persiste `NO_CHANGE` con attempt incrementado y no se envia mutation.
- Si requiere mutation, el Master envia solo `SWITCH_MANAGED_ACCOUNT(targetAccountId original)` con operationId remoto nuevo.
- El resultado final actualiza solo targets seleccionados, recalcula summary/status del batch completo y actualiza `retryable-targets` segun estado actual.
- `OPERATION_RESULT_UNKNOWN` de `SWITCH_MANAGED_ACCOUNT` queda defensivamente excluido de retryable targets.
- Actualizados docs de API, arquitectura, modelo funcional, reglas, estado, decisiones, historial, batch de managed account switch y session switch.

### Cambios descartados

- No se agrego retry automatico, scheduler, polling, startup auto-retry ni reconciliation para `SWITCH_MANAGED_ACCOUNT`.
- No se creo una `BatchOperation` nueva durante retry.
- No se cambio target account desde la request de retry.
- No se hizo retry parcial de requests malformadas o con targets inelegibles.
- No se exigio `SESSION_AGENT_AVAILABLE`.
- No se encadeno `LOGOFF_WINDOWS_SESSION + LOGON_MANAGED_ACCOUNT` desde Master.
- No se agregaron Agent changes, Protobuf changes, C++ changes, UI, provisioning, vault unlock ni Credential Provider changes.
- No se hizo commit.

### Validaciones

- `mvn -q "-Dtest=ManagedAccountSwitchDispatchControllerTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=ManagedAccountSwitchDispatchControllerTest,MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -Ddebug=false "-Dtest=ManagedAccountSwitchDispatchControllerTest,ManagedAccountSwitchPlannerTest,BatchOperationPlannerTest,MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Validacion completa

- `mvn -q test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.

### Commit sugerido

`feat(master): retry managed account switch targets`

## 2026-09-06 - Prompt 19I1

### Realizado

- Integrado el Credential Provider nativo al lifecycle productivo normal del Agent sin ejecutar registro real ni prueba de logon/switch en la maquina de desarrollo.
- Agregado `installer/windows/credential-provider-common.ps1` con constantes Galtek, validacion de Windows/PowerShell x64, PE x64, manifest, SHA-256, Authenticode, ACL y Registry HKLM x64.
- Agregado `publish-credential-provider.ps1` para localizar MSBuild, compilar `GaltekClassroom.CredentialProvider` como `Release|x64`, no registrar nada y crear artifact limpio.
- Agregado artifact productivo default `artifacts/windows/credential-provider/` con solo `GaltekClassroom.CredentialProvider.dll` y `credential-provider.manifest.json`.
- El manifest del package contiene `schemaVersion`, `product`, `component`, CLSID fijo, `architecture=x64`, filename, SHA-256 y `packageId` deterministico `sha256-<hash-prefix>`.
- Agregado `test-credential-provider-package.ps1` read-only para validar artifact sin elevacion, Program Files, HKLM ni SCM.
- Agregado `install-credential-provider.ps1` productivo con elevacion, PowerShell x64, manifest/hash/PE/firma, Agent Service instalado, absence de filter Galtek, ACL y rollback in-memory de registration Galtek.
- El install stagea versiones inmutables bajo `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\versions\<packageId>\` y registra `InprocServer32` directo a la DLL activa.
- Registration productiva usa HKLM x64 machine-wide, `SOFTWARE\Classes\CLSID\{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}` y `Authentication\Credential Providers\{...}`, con `ThreadingModel=Apartment`.
- El installer falla cerrado con `CREDENTIAL_PROVIDER_REGISTRATION_CONFLICT` si el CLSID Galtek apunta fuera del root Galtek esperado.
- Agregado `uninstall-credential-provider.ps1` para remover primero provider registration, luego COM registration y despues cleanup best-effort de paquetes.
- El uninstall no mata LogonUI/Winlogon, no fuerza unload, no agenda `PendingFileRenameOperations` manualmente y no reinicia Windows automaticamente; si quedan locks reporta `UNREGISTERED_REBOOT_CLEANUP_REQUIRED`.
- Agregado `test-credential-provider-installation.ps1` read-only para verificar registration, path, manifest/hash, `ThreadingModel`, ausencia de filter Galtek, ACL y Authenticode.
- `publish-agent.ps1` ahora publica Service, Session Agent y Credential Provider por default; el skip del provider es explicito de desarrollo.
- `install-agent.ps1` instala Service, Session Agent y Credential Provider en orden seguro y falla como partial install si el provider falla.
- `uninstall-agent.ps1` desregistra Credential Provider antes de uninstall de Session Agent y Agent Service.
- `install-agent-service.ps1` y `uninstall-agent-service.ps1` preservan `Agent\Session\` y `Agent\CredentialProvider\` en lifecycles Service-only.
- Scripts dev `register/unregister-credential-provider-dev.ps1` quedan marcados claramente como `LAB / DEV ONLY`.
- Release x64 del provider y tests nativos usan `RuntimeLibrary=MultiThreaded` (`/MT`).
- Actualizados README raiz, installer README, Credential Provider docs, arquitectura, modelo funcional, reglas, estado y decisiones.

### Cambios descartados

- No se registro el Credential Provider en esta PC.
- No se ejecuto `install-credential-provider.ps1`, `uninstall-credential-provider.ps1` ni scripts dev de register/unregister.
- No se hizo prueba real de Windows logon, switch, lock screen ni LogonUI en esta maquina.
- No se cambio LOGON/SWITCH, Protobuf, Master Backend Java, Agent Service behavior, Session Agent behavior, capabilities, bridge Service <-> Provider, secret handling, Vault, SQLite ni UI.
- No se implemento `ICredentialProviderFilter` ni se tocaron providers estandar de Windows.
- No se copio DLL a System32/SysWOW64/Windows/Desktop/AppData/ProgramData/Temp.
- No se invento certificado, PFX, thumbprint, CA ni signing productivo.
- No se agrego MSI/MSIX, setup grafico, self-update, remote installer, registry polling, heartbeat de provider instalado ni reboot automatico.
- No se hizo commit.

### Validaciones

- Parser PowerShell sobre scripts nuevos/modificados de Credential Provider y orchestrators: correcto.
- `.\installer\windows\publish-credential-provider.ps1`: correcto; MSBuild 17.14, `Release|x64`, `/MT`, 0 advertencias, 0 errores.
- `.\installer\windows\test-credential-provider-package.ps1`: correcto; manifest, CLSID, x64, SHA-256, packageId y Authenticode verificados.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj`: correcto, `/MT`, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.
- `dumpbin /dependents` sobre el DLL final: `ole32.dll`, `ADVAPI32.dll`, `Secur32.dll`, `KERNEL32.dll`; sin `VCRUNTIME*.dll`, `MSVCP*.dll` ni dependencia .NET.
- `.\installer\windows\publish-agent.ps1`: correcto; publica artifacts de Service, Session Agent y Credential Provider.
- Package final de Credential Provider contiene solo DLL y manifest.

### Validacion manual pendiente 19I2

- En PC descartable con Windows 10/11 x64, password administrativa conocida, acceso fisico, recovery y Password Provider estandar funcional: fresh install, verifier, registry/path/ACL/providers estandar, fail-open con Service detenido, no activation espontanea, logon real PRIMARY, wrong password sin loop, switch real PRIMARY/SECONDARY, batch/partial/retry, update side-by-side y uninstall con ProgramData preservado.

### Commit sugerido

`feat(installer): deploy credential provider safely`
