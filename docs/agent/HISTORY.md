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
