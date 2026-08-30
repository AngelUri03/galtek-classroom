# Arquitectura

## Estado general

Prompt 14.4 convierte perdida de energia, reinicio abrupto y boot storm en condiciones normales de diseno. El Agent separa startup minimo de validaciones pesadas: marca arranque con `agent-service.running`, llega a `MINIMAL_READY` tras Installation Identity y expone por IPC `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive`. La validacion comercial completa queda diferida fuera del camino critico de IPC/red. El Master agrega `master-backend.running`, helpers de escritura atomica/durable y modelos puros de recovery para readiness, politicas de arranque y semantica de operaciones inciertas. La reconexion del Client agrega jitter acotado para evitar thundering herd sin reemplazar el backoff.

Prompt 14.3 convierte performance y bajo consumo en requisitos arquitectonicos medibles. Agrega modelos puros Java para perfiles `LEGACY`/`STANDARD`, `MASTER_BALANCED`, clases de trabajo de recursos, budgets de memoria/concurrencia, diagnostico on-demand y load shedding. Tambien agrega constantes compartidas C# para esos nombres y corrige ruido claro de idle: requests IPC exitosos y conexion IPC pasan a `DEBUG`, los retries repetidos de gRPC bajan a `DEBUG` y el heartbeat del Agent ya no relee `authorized-masters.json` en cada ciclo.

Prompt 14.2 fija el modelo operativo real del aula primaria y la arquitectura Master/Client sin implementar operaciones Windows reales. Agrega modelos/enums/planners puros para estrategias de asignacion, preparacion progresiva por Device, estados de workspace canonico/local, prioridad operacional, modos de proyeccion, politica normal de `PRIMARY`/`SECONDARY` y reglas de limpieza segura de working copies. No agrega migraciones ni persistencia nueva.

Prompt 14 registra Clients paired como Devices persistentes del Master sin redisenar pairing ni mTLS. El Master conserva la autoridad sobre `deviceId`, persiste el vinculo vigente en `device_network_bindings`, expone `GET /api/network/clients` y `POST /api/classrooms/{classroomId}/devices/register`, acepta capabilities tipadas reportadas por `ClientHello` y superpone presencia viva en memoria sobre Devices registrados. El framework de operaciones remotas queda tipado en Protobuf y en el Agent, pero ninguna operacion funcional real se ejecuta todavia; toda operacion sin handler devuelve `OPERATION_NOT_IMPLEMENTED`.

Prompt 13 implementa el primer transporte real y seguro Master-Client sobre el trust de Prompt 12. El Client inicia una conexion persistente saliente hacia el Master mediante gRPC/Protobuf v1 sobre TLS/mTLS obligatorio. Los certificados son self-signed de corta vida y se validan por pinning del fingerprint `SubjectPublicKeyInfo` ya persistido por pairing; no existe CA global que autorice instalaciones arbitrarias.

El alcance de red vigente incluye `ClientHello`, estado de conexion, heartbeat, capabilities tipadas y mensajes de framework `OperationRequest`/`OperationAccepted`/`OperationResult` sin comandos remotos reales. No hay mDNS ni discovery real.

Prompt 12 implementa pairing criptografico Master-Client sobre las Network Identities ya existentes. El Master tiene una Network Identity propia con metadata publica en `master-network-identity.json` y private key cifrada fuera de SQLite/JSON plano; el Client conserva su `network-identity.json` publico y private key en Windows CNG/KSP de maquina. El pairing usa challenge/response firmado, requiere intencion explicita, persiste trust en ambos lados y permite revocacion.

Prompt 9.6 formaliza el dominio futuro de cuentas Windows administradas en Clients. Cada Client podra tener dos cuentas logicas, `PRIMARY` y `SECONDARY`, y el Master podra planificar una sola accion masiva para dejar PCs en la cuenta objetivo, clasificando `NO_CHANGE`, `LOGON`, `SWITCH`, `PENDING` y bloqueos. Solo se agregan modelos/enums/planners puros y contratos compartidos; no hay passwords, Credential Provider, login/logoff real, IPC write, gRPC, mTLS ni UI.

Prompt 10 agrega la primera API administrativa real del Master Backend sobre SQLite. Los endpoints de aulas, grupos, alumnos, assignments, aplicaciones, operaciones, bootstrap y snapshot pasan por `MasterAccessGuard` antes de tocar datos escolares. La UI React/Tauri futura puede iniciar con `GET /api/master/bootstrap`, elegir aula y cargar `GET /api/classrooms/{id}/snapshot` sin N+1.

Prompt 09 implementa la autoridad real de Master Windows Binding en `GaltekClassroom.Agent.Service`. El binding local se persiste en `master-binding.json`, se liga al `installationId`, se evalua contra `LicenseState` activo con rol `MASTER` y se compara contra el SID real del cliente conectado al Named Pipe mediante impersonation. El Master Backend Java solo consume el resultado derivado por IPC y expone diagnostico.

Prompt 08 agrega persistencia SQLite local del dominio Master mediante Spring JDBC, Flyway programatico y repositories explicitos. El Master Backend ya puede crear, migrar y reabrir una base `classroom.db` con aulas, catalogo de aplicaciones, grupos, alumnos, devices, assignments, workspaces, perfiles de navegador y operaciones batch.

Prompt 07 agrega el modelo funcional completo de Galtek Classroom en el Master Backend: aula, devices, alumnos, grupos, workspaces de alumno, perfiles de navegador, binding local del Master por Windows SID, catalogo de acciones, errores operacionales y planners puros para assignment, move, swap y batch preflight.

Prompt 06 deja `GaltekClassroom.Agent.Service` como Windows Service real y agrega el ciclo de vida productivo de `GaltekClassroom.Agent.Session`. El Service se ejecuta en Session 0 como `LocalSystem`; el Session Agent arranca al logon mediante Windows Task Scheduler, se ejecuta con el token del usuario interactivo, usa privilegio limitado, permanece en background sin UI y se reconecta al Service por Local IPC.

Local IPC API v1 sigue siendo read-only sobre Windows Named Pipes. `GaltekClassroom.Agent.Service` expone estado seguro de dispositivo, Machine Code y autorizacion Master local al Master Backend Java sin duplicar Installation Identity ni Commercial License. El Session Agent continua usando `PING` y `GET_DEVICE_STATUS`.

Las capacidades operativas de administracion remota siguen planificadas. Prompt 14 no ejecuta transferencia real, Chrome, wallpapers, proyeccion, login/logoff Windows, cambio real de usuario, mDNS, UI, captura, bloqueo ni comandos remotos.

## Modelo operativo Master/Client

El Master de la profesora debe absorber orquestacion, SQLite, workspaces canonicos, metadata escolar, manifests/checksums futuros, planeacion de distribucion, coordinacion batch, recuperacion y estado del aula. No debe convertirse en terminal server ni ejecutar interactivamente las aplicaciones de los alumnos.

Los Clients ejecutan localmente Windows, aplicaciones interactivas, Chrome, USB, working copy local de workspace, captura cuando se solicite, rendering de proyeccion y acciones fisicas Windows futuras. El diseno debe seguir funcionando sobre los Clients legacy mas lentos.

Hardware objetivo documentado:

- Master: Intel Core i5 8a generacion aprox., 16 GB DDR4, SSD 256 GB.
- Clients legacy: aprox. 16 equipos con hardware heterogeneo muy limitado, principalmente 4 GB RAM + HDD y CPUs Core 2 Duo / Celeron / AMD antiguos.
- Clients renovados: aprox. 10 equipos Core i5 6a generacion, 8 GB DDR4, SSD 256 GB.

Flujo real de primaria:

1. La maestra llega.
2. Enciende PCs.
3. Abre Galtek en el Master.
4. Selecciona aula/grupo.
5. Asigna alumnos a PCs.
6. Galtek prepara cada PC independientemente.
7. Los primeros equipos `READY` pueden empezar sin esperar a los lentos.

El aula no tiene un unico boolean `READY`; la arquitectura debe poder expresar conteos por target como `18 READY`, `4 PREPARING`, `2 OFFLINE`, `1 RECOVERY_REQUIRED` y `1 FAILED`. Una PC fallida o lenta no cancela la preparacion de otras.

`CLASS_TIME_TO_READY` queda como KPI principal de producto: minimizar el tiempo desde que la maestra llega/enciende equipos hasta que los alumnos pueden iniciar actividad. Performance, preview FPS y features deben subordinarse a ese objetivo cuando compitan por recursos.

## Performance budgets y resource profiles

VIGENTE DESDE PROMPT 14.3:

- Galtek Classroom se disena primero para Clients de 4 GB RAM, HDD y CPU de gama baja.
- Cuando Galtek no realiza trabajo solicitado, el Client debe quedar casi idle.
- Orden de prioridad de recursos:
  - Windows y aplicacion educativa del alumno.
  - Control critico de Galtek.
  - Preparacion de clase.
  - Operaciones normales.
  - Observabilidad/funciones visuales.
  - Tareas background no esenciales.
- Perfiles operacionales de Client:
  - `LEGACY`: perfil conservador para hardware heterogeneo muy limitado, principalmente 4 GB RAM + HDD y equipos extremadamente lentos.
  - `STANDARD`: perfil para Clients renovados con 8 GB DDR4, SSD 256 GB y mayor margen operativo.
- El perfil desconocido de Client se trata como `LEGACY`.
- El perfil del Master queda modelado como `MASTER_BALANCED`.
- Los perfiles de rendimiento no son identidad, seguridad, autorizacion ni trust.
- No se implementa deteccion agresiva de hardware. Si una fase futura infiere perfil, debe hacerlo una sola vez o muy raramente y nunca por polling WMI.

Budgets de Client:

- Agent Service idle: CPU practicamente 0%, sin actividad sostenida de disco, sin WMI periodico, sin enumeracion constante de procesos, sin captura y sin filesystem scanning continuo.
- Session Agent idle: sin captura, overlays, UI, process scanning, filesystem scanning, WMI costoso, inventario periodico ni polling rapido.
- Objetivo de memoria idle: Agent Service <= aprox. 60 MB, Session Agent <= aprox. 40 MB, combinado <= aprox. 100 MB.
- Un Client combinado que supere aprox. 150 MB idle requiere justificacion y revision.
- Estos numeros son budgets de ingenieria, no garantias contractuales ni unit tests de Working Set.

Budgets del Master:

- Hardware objetivo: i5 8a gen aprox., 16 GB DDR4, SSD 256 GB.
- Backend Java deliberadamente pequeno, con objetivo inicial de heap <= 512 MB salvo profiling real que justifique mas.
- SQLite local, WAL, Hikari pequeno y queries batch-friendly siguen siendo la direccion.
- No introducir Redis, Kafka, Elasticsearch, RabbitMQ, DB server separado ni infraestructura distribuida pesada para el producto local.
- No convertir el Master en terminal server: Word/Chrome/apps de alumnos se ejecutan localmente en Clients.

Concurrencia y load shedding:

- `ResourceWorkClass` clasifica trabajo como `CONTROL_CRITICAL`, `CLASS_PREPARATION`, `INTERACTIVE`, `TRANSFER`, `VISUAL` o `BACKGROUND` y se relaciona con `OperationPriority` sin reemplazarlo.
- `LEGACY` limita una operacion pesada simultanea por Client. `STANDARD` puede aceptar ligeramente mas, pero nunca concurrencia ilimitada.
- El Master debe usar fanout limitado, colas, backpressure y prioridades cuando existan schedulers reales.
- Una operacion `CRITICAL` nunca espera detras de thumbnails, transferencias grandes, inventario o prefetch.
- El orden conceptual de sacrificio es `PREFETCH`, `NON_ESSENTIAL_INVENTORY`, `THUMBNAILS`, `PREVIEW_QUALITY_OR_FPS`, `NON_URGENT_TRANSFER`, `BACKGROUND_JOB`.
- `DEGRADED` existe como estado de presion de recursos y no equivale a `OFFLINE`; un Client puede seguir controlable aunque suspenda previews.
- No se implementa monitoreo continuo pesado para detectar degradacion.

Politicas futuras:

- Visual plane en boot/idle: 0 capturas. Thumbnails futuros deben ser pequenos, de baja frecuencia, solo para Devices visibles y con concurrencia limitada; nunca 26 PCs x 30 FPS siempre.
- Transferencias futuras en `LEGACY`: una transferencia pesada por vez, chunks moderados y rate/concurrency limitada; el Master limita fanout global y prioriza HIGH/CRITICAL.
- Workspace futuro nunca debe escanear recursivamente todos los `StudentWorkspace` de todas las PCs; sync debe ser incremental, por cambios o por workflow/evento.
- Logging de produccion: `INFO` solo eventos significativos; sin logs por heartbeat sano, PING sano ni conexion saludable repetitiva; errores repetidos deben rate-limitarse o coalescer conceptualmente.
- Diagnostico de performance: on-demand, snapshot ligero, sin recoleccion constante, sin persistir telemetria y sin enviarla por heartbeat.

## Power-loss resilience y startup rapido

VIGENTE DESDE PROMPT 14.4:

- Power loss is normal: un corte de energia, kill del proceso, reboot o arranque masivo del aula debe producir recovery conservador, no corrupcion silenciosa ni perdida asumida.
- Fast startup tiene prioridad sobre funciones visuales o pesadas. El plano de control local debe estar disponible antes de licencia comercial completa, WMI costoso, inventario, thumbnails, captura, sync o transferencias.
- El Agent declara fases de startup: `STARTING`, `RECOVERING`, `MINIMAL_READY`, `SECURITY_READY`, `NETWORK_READY`, `OPERATION_READY` y `DEGRADED`.
- `MINIMAL_READY` significa que Local IPC y la Installation Identity ya pueden responder de forma segura; no implica licencia activa ni autorizacion remota completa.
- `previousShutdownWasUnclean` se deriva de un marker persistente que solo se escribe al inicio y se elimina en detencion limpia; no hay writes periodicos de heartbeat para ese marker.
- `recoveryActive` indica que el proceso esta resolviendo identidad/trust/red despues de un shutdown no limpio o durante startup temprano.
- El Master puede quedar control-plane ready con proceso vivo y SQLite listo aunque haya 0 Clients online; nunca espera a que todos los Clients arranquen.
- SQLite conserva WAL/SHM y usa su propio recovery; Galtek no borra ni recrea `classroom.db`, `classroom.db-wal` o `classroom.db-shm` ante marker de apagado no limpio.
- Archivos criticos JSON/dat se escriben via temp file en el mismo directorio, flush/fsync y move/replace atomico cuando corresponde.
- Boot, `ClientHello`, pairing, registration, reconnect y heartbeat no disparan captura, proyeccion, thumbnails, filesystem sync, inventario pesado ni operaciones visuales automaticas.
- Operacion remota sin ACK o sin resultado confirmado no se considera `SUCCESS`; queda como incertidumbre recuperable que exige reconciliacion.
- Boot storm se mitiga con reconexion Client saliente, backoff acotado y jitter inicial/retry pequeno para evitar reconexiones perfectamente sincronizadas.

Clasificacion futura de durabilidad:

- `EPHEMERAL`: heartbeat, `ONLINE`/`OFFLINE`, preview state y telemetria ligera. Debe vivir en memoria y reconstruirse.
- `NORMAL`: metadata reconstruible o recuperable, persistida con transacciones/escrituras crash-safe razonables.
- `CRITICAL_DURABLE`: cambios que no deben reconocerse como confirmados antes de durabilidad suficiente; requieren flush/fsync/commit atomico segun el backend concreto.

## Master

IMPLEMENTADO:

- Backend minimo en `master-backend/`.
- Java 21.
- Spring Boot 3.x.
- Maven.
- Package base `com.galtek.classroom`.
- Endpoint `GET /api/system/health`.
- Endpoint `GET /api/device/status`, delegado al Agent Service mediante IPC local.
- Endpoint `GET /api/device/machine-code`, delegado al Agent Service mediante IPC local.
- Endpoint `GET /api/master/authorization`, delegado al Agent Service mediante IPC local.
- Endpoints administrativos protegidos por `MasterAccessGuard`:
  - `GET /api/master/bootstrap`.
  - `GET /api/classrooms`.
  - `POST /api/classrooms`.
  - `PATCH /api/classrooms/{id}`.
  - `POST /api/classrooms/{id}/archive`.
  - `GET /api/classrooms/{id}/groups`.
  - `POST /api/classrooms/{id}/groups`.
  - `PATCH /api/groups/{id}`.
  - `POST /api/groups/{id}/archive`.
  - `GET /api/classrooms/{id}/students`.
  - `GET /api/students/{id}`.
  - `POST /api/classrooms/{id}/students`.
  - `POST /api/classrooms/{id}/students/batch`.
  - `PATCH /api/students/{id}`.
  - `POST /api/students/{id}/archive`.
  - `POST /api/students/archive-batch`.
  - `GET /api/classrooms/{id}/assignments`.
  - `POST /api/assignments`.
  - `POST /api/assignments/batch`.
  - `POST /api/assignments/{id}/close`.
  - `GET /api/applications`.
  - `GET /api/classrooms/{id}/applications`.
  - `GET /api/operations`.
  - `GET /api/operations/{id}`.
  - `GET /api/operations/{id}/retryable-targets`.
  - `GET /api/classrooms/{id}/snapshot`.
  - `GET /api/network/clients`.
  - `POST /api/classrooms/{classroomId}/devices/register`.
- `RestControllerAdvice` uniforme para errores operacionales HTTP.
- API documentada en `docs/api/master-api-v1.md`.
- Cliente `LocalAgentClient` con transporte Windows Named Pipe y framing IPC v1.
- Mapeo de Agent Service no disponible a HTTP 503 con codigo `LOCAL_AGENT_UNAVAILABLE`.
- `MasterAccessGuard` para endpoints administrativos; consulta al Agent Service y falla cerrado cuando `authorized=false`.
- Prueba automatica del health endpoint.
- Pruebas de framing IPC, cliente local, endpoints de dispositivo, autorizacion Master, guard y salud.
- Dominio funcional puro en paquetes por contexto:
  - `classroom`: `Classroom`, configuracion y relacion de devices/students/groups.
  - `device`: `Device`, estados operacionales y capacidades.
  - `student`: `Student`, `SchoolGroup`, `DeviceAssignment`, policies y planners de move/swap.
  - `workspace`: `StudentWorkspace`, destinos logicos y recovery planificado.
  - `browser`: perfiles de alumno/Master y validacion conservadora de URL.
  - `application`: catalogo de aplicaciones por `applicationId`.
  - `operations`: catalogo de acciones, batch, preflight, resultados, errores, conflict policy y workflows.
  - `master`: `MasterWindowsBinding`, proveedor de SID actual y politica de autorizacion.
  - `windows`: cuentas administradas `PRIMARY`/`SECONDARY`, estado de sesion Windows y preflight batch para cambio de cuenta.
  - `network`: Network Identity del Master, Client descriptors, pairing challenge/response, trust store y revocacion.
  - `performance`: perfiles `LEGACY`/`STANDARD`, `MASTER_BALANCED`, budgets, clases de trabajo, diagnostico on-demand, estado `DEGRADED` y load shedding conceptual.
  - `recovery`: marker de ejecucion Master, readiness de plano de control, politicas de startup y semantica de operaciones remotas inciertas.
- Modelos puros Prompt 14.2:
  - `StudentAssignmentStrategy`: `LIST_ORDER`, `RANDOM`, `PREVIOUS`, `MANUAL`.
  - `StudentPreparationStage` y `StudentPreparationState` para `ASSIGNED -> PREPARING_WINDOWS_SESSION -> PREPARING_WORKSPACE -> PREPARING_BROWSER -> APPLYING_CLASS_CONTEXT -> READY`, con estados `PENDING`, `IN_PROGRESS`, `READY`, `PARTIAL_READY`, `RECOVERY_REQUIRED` y `FAILED`.
  - `ClassroomReadinessPlan` para readiness progresiva por target.
  - `WorkspaceResidencyState`, `WorkspaceSyncState`, `WorkspaceCommitStage` y `WorkspaceSyncSafetyPlanner` para canonico Master, working copy Client y limpieza solo tras confirmacion.
  - `ProjectionMode`: `SCREEN_SHARE`, `WHITEBOARD`, `POINTER`, `LOCAL_MEDIA`, `OPEN_WEB_CONTENT`.
  - `OperationPriority`: `CRITICAL`, `HIGH`, `NORMAL`, `LOW`.
  - `ManagedWindowsAccountOperatingPolicy` para documentar que `PRIMARY` y `SECONDARY` son Windows normal por default.
- Modelos puros Prompt 14.3:
  - `DevicePerformanceProfile`: `LEGACY`, `STANDARD`; desconocido -> `LEGACY`.
  - `MasterPerformanceProfile`: `MASTER_BALANCED`.
  - `ResourceWorkClass`: `CONTROL_CRITICAL`, `CLASS_PREPARATION`, `INTERACTIVE`, `TRANSFER`, `VISUAL`, `BACKGROUND`.
  - `ClientPerformanceBudget`: concurrencia pesada por perfil, budgets idle y regla de no autorizacion por perfil.
  - `MasterPerformanceBudget`: heap objetivo inicial 512 MB, Hikari pequeno y no terminal server/infraestructura distribuida pesada.
  - `LoadSheddingPolicy`, `SheddableWork`, `ResourcePressureState.DEGRADED` y `PerformanceDiagnosticPolicy.onDemandOnly()`.
- Modelos puros Prompt 14.4:
  - `MasterStartupReadiness` permite `controlPlaneReady()` con proceso vivo y storage listo, sin esperar Clients online.
  - `StartupWorkPolicy` permite solo `CONTROL_CRITICAL` durante startup de control y difiere `VISUAL`, `TRANSFER` y `BACKGROUND` durante recovery.
  - `StartupEvent` documenta que boot, Session Agent startup, `ClientHello`, pairing, registration, reconnect, heartbeat y online no inician captura automatica.
  - `RemoteOperationRecoveryPolicy` evita modelar como exito una operacion remota sin ACK/resultado confirmado.
- `DeviceAssignmentPolicy` para detectar alumno ya asignado y equipo ocupado.
- `StudentMovePlanner` para preflight de `MOVE_STUDENT` sin mover archivos.
- `StudentSwapPlanner` para preflight de `SWAP_STUDENTS` sin transferencias ni cambios de assignment.
- `ManagedAccountSwitchPlanner` para preflight de `SWITCH_MANAGED_ACCOUNT` sin iniciar ni cerrar sesiones reales.
- `BatchOperationPlanner` para clasificar targets `READY`, `WARNING`, `BLOCKED`.
- Modelo central `ErrorCode` con categorias y bandera retryable, separado de mensajes para usuario.
- Operaciones futuras tipadas `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`.
- Estado de resultado por target `NO_CHANGE` tratado como exito no retryable.
- `BatchOperation` con estados `SUCCESS`, `PARTIAL_SUCCESS`, `FAILED`, `CANCELLED`, `ROLLED_BACK` y retry solo de fallidos retryable.
- `OpenUrlPolicy` que permite `http`/`https` y rechaza esquemas inseguros como `file`, `javascript` y `data`.
- `DistributeFileRequest` modela apertura opcional posterior mediante `openAfterDistribution` sin transferencia real.
- `LogicalWorkspaceDestination.REMOVABLE_STORAGE` formaliza USB futuro como destino logico autorizado, no como ruta arbitraria.
- Pruebas Java de assignment, move, swap, batch, URL y autorizacion Master.
- Pruebas Java de Prompt 14.2 para estrategias de assignment, readiness parcial, limpieza segura de workspace, prioridades, proyeccion, removable storage, distribucion y politica normal de `PRIMARY`/`SECONDARY`.
- Pruebas Java de Prompt 14.3 para default `LEGACY`, concurrencia por perfil, precedencia `CRITICAL`, idle policy, load shedding, diagnostico on-demand y no autorizacion por perfil.
- Persistencia SQLite local del dominio Master con Spring JDBC.
- Dependencias `spring-boot-starter-jdbc`, `flyway-core` y `sqlite-jdbc` en el backend Master.
- Flyway programatico para migraciones SQLite desde `classpath:db/migration/sqlite`.
- Migracion `V1__create_master_domain.sql` con tablas del dominio Master.
- Migracion `V2__add_device_network_bindings.sql` con `device_network_bindings` e indices unicos parciales para un Network Identity vigente por Device y un Device vigente por Network Identity.
- Configuracion local `galtek.classroom.master.storage.*`.
- Resolucion de datos del Master a `<CommonApplicationData>\Galtek\Classroom\Master\` con override `GALTEK_CLASSROOM_MASTER_DATA_DIR`.
- Base local `classroom.db` ignorada por Git, junto con archivos WAL/SHM.
- `PRAGMA foreign_keys=ON`, WAL, `synchronous=NORMAL` y `busy_timeout` configurado.
- Pool Hikari pequeno para SQLite local.
- `MasterDatabaseInitializer` con `PRAGMA quick_check` antes/despues de migrar cuando corresponde.
- `MasterRunMarker` crea `master-backend.running` al arrancar y lo elimina en cierre limpio para detectar apagado no limpio sin tocar SQLite/WAL/SHM.
- `AtomicFiles` centraliza escrituras atomicas/durables de archivos criticos del Master con temp file, flush/fsync y move atomico.
- Estado de almacenamiento `MasterStorageState` con `READY`, `UNAVAILABLE`, `CORRUPT` y `MIGRATION_FAILED`.
- Repositories explicitos para `Classroom`, `ApplicationDefinition`, `SchoolGroup`, `Student`, `Device`, `DeviceNetworkBinding`, `DeviceAssignment`, `StudentWorkspace`, `BrowserProfile`, `MasterBrowserProfile` y `BatchOperation`.
- Servicios transaccionales de administracion de aula, alumnos, devices, assignments, workspaces, perfiles, catalogo y batch.
- Repositorio administrativo JDBC con modelos de lectura para bootstrap, snapshot, conteos, operaciones y batches sin exponer entidades de persistencia a controllers.
- Control de version optimista mediante columna `version` y error `CONCURRENT_MODIFICATION`.
- `device_assignments` como fuente de verdad de asignaciones actuales e historicas.
- Endpoints de assignment solo modifican metadata SQLite; no mueven `StudentWorkspace`.
- `students/batch` devuelve resultado independiente por fila y no cancela todo el lote por un alumno invalido.
- `assignments/batch` hace preflight completo del lote, detecta conflictos internos y registra una `batch_operation` de tipo `ASSIGN_STUDENT`.
- `GET /api/classrooms/{id}/snapshot` devuelve aula, grupos, alumnos activos, devices, current assignments, aplicaciones y resumen en una sola respuesta batch-friendly.
- Indices unicos parciales para un assignment actual por alumno y por device.
- Persistencia de batch operations con targets, estados, errores, attempts y retry de fallidos retryable.
- Mapeo de errores SQLite a `MASTER_DATABASE_UNAVAILABLE`, `MASTER_DATABASE_CORRUPT`, `MASTER_DATABASE_MIGRATION_FAILED`, `MASTER_DATABASE_BUSY`, `MASTER_STORAGE_FULL`, `PERSISTENCE_CONSTRAINT_VIOLATION` y `CONCURRENT_MODIFICATION`.
- Pruebas de integracion SQLite para creacion, migracion, reapertura, constraints, historial de assignments, batch retry, rollback, versionado y base corrupta.
- Master Network Identity local con metadata publica en `master-network-identity.json`.
- Private key del Master cifrada fuera de SQLite/JSON plano en `master-network-identity.key`, con protector separado en `master-network-identity.protector`.
- `MasterPairingService` crea challenges con intencion explicita, firma con la private key del Master y persiste challenges pendientes.
- `MasterPairingService` completa pairing validando la respuesta firmada del Client, expiracion y replay.
- `paired-clients.json` persiste trust del lado Master, incluyendo estado `PAIRING_PENDING`, `PAIRED` o `REVOKED`.
- `MasterClientAuthorization` falla cerrado con `MASTER_NOT_PAIRED` cuando el Client no esta emparejado o fue revocado.
- Pruebas Java para Master Network Identity, challenge/response, expiracion, replay, persistencia, revocacion y multiples Clients.
- Contrato Protobuf versionado `protocol/network/v1/galtek-classroom-network-v1.proto`.
- Dependencias gRPC Java con generacion Protobuf desde el contrato compartido.
- `MasterNetworkGrpcService` acepta un stream bidireccional `NetworkConnection.Connect` para `ClientHello`, `Heartbeat` y respuestas tipadas de framework de operaciones.
- `MasterNetworkConnectionAuthenticator` valida `ClientHello` contra Network Identity, trust `PAIRED`, no `REVOKED` y fingerprint del certificado mTLS.
- `MasterTlsPeerTrustManager` rechaza certificados de Client que no correspondan a un trust `PAIRED` vigente en `paired-clients.json`.
- `MasterNetworkGrpcServer` usa Netty gRPC con TLS/mTLS obligatorio, sin reflection ni fallback plaintext, y queda deshabilitado por defecto hasta configurar `galtek.classroom.master.network.grpc.enabled=true`.
- `ClientConnectionRegistry` mantiene presencia viva por Client autenticado y distingue Client paired sin Device de Device registrado (`CONNECTING`, `ONLINE`, `OFFLINE`).
- `ClientHello` reporta capabilities tipadas conocidas: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1` y `SESSION_AGENT_AVAILABLE`.
- Las capabilities son informacion operativa y no autorizacion.
- `NetworkClientAdminService` lista Clients known/paired con estado seguro y registra Devices solo tras verificar trust `PAIRED`, no `REVOKED` y ausencia de doble registro.
- `GET /api/classrooms/{id}/snapshot` superpone presencia viva para Devices registrados sin escribir SQLite en cada heartbeat.
- `MasterNetworkHeartbeatMonitor` marca `OFFLINE` tras timeout de heartbeat configurado.
- Certificado TLS del Master emitido en memoria desde su Network Identity, ligado al fingerprint ya persistido por pairing.
- Pruebas Java para conexion PAIRED, rechazo no paired, rechazo REVOKED, mismatch de certificado/fingerprint, peer desconocido, heartbeat, timeout offline, reconnect, multiples Clients concurrentes, capabilities, registro de Devices, no writes persistentes por heartbeat y trust/binding persistente tras reinicio.

PLANIFICADO:

- React + Tauri para UI de escritorio, sin Vite.
- Exponer pairing mediante flujos reales sobre el transporte seguro existente.
- Visualizacion de equipos, miniaturas y estado.
- UI batch-first para grupos, alumnos y equipos con partial success y retry de fallidos.
- Consulta y cambio masivo de sesion Windows administrada por `accountId` logico.
- Integracion real de workspaces, navegador, transferencia, wallpaper, proyeccion y auditoria.
- Auditoria administrativa.

NO IMPLEMENTADO:

- UI.
- Autenticacion.
- Descubrimiento.
- mDNS real.
- Comandos remotos hacia Clients.
- Commercial License en Java.
- Llaves publicas o JWT dentro del Master Backend.
- Ejecucion real de `OPEN_APPLICATION`, `OPEN_URL`, `DISTRIBUTE_FILE`, `CREATE_FOLDER`, `SET_WALLPAPER`, `MOVE_STUDENT` o `SWAP_STUDENTS`.
- Ejecucion real de `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` o `SWITCH_MANAGED_ACCOUNT`.
- Almacenamiento de passwords o credenciales Windows administradas en `classroom.db`.
- Login/logoff Windows real, Credential Provider, filesystem/sync real, USB real, automatizacion Chrome, captura, proyeccion, UI, reconciliacion productiva de operaciones/workflows inciertos, performance tuning y mDNS.

## Almacenamiento local del Master

IMPLEMENTADO:

- SQLite local en archivo `classroom.db`.
- Ruta productiva por defecto: `<CommonApplicationData>\Galtek\Classroom\Master\classroom.db`.
- Override de desarrollo/tests: `GALTEK_CLASSROOM_MASTER_DATA_DIR` o `galtek.classroom.master.storage.data-dir`.
- Nombre de archivo configurable con `galtek.classroom.master.storage.database-file-name`.
- Migraciones en `master-backend/src/main/resources/db/migration/sqlite/`.
- Spring Boot Flyway autoconfiguration deshabilitada; el Master ejecuta Flyway programaticamente para controlar health checks y mapping de errores.
- `V1__create_master_domain.sql` crea:
  - `classrooms`.
  - `application_definitions`.
  - `classroom_applications`.
  - `school_groups`.
  - `students`.
  - `devices`.
  - `student_workspaces`.
  - `browser_profiles`.
  - `master_browser_profiles`.
  - `device_assignments`.
  - `batch_operations`.
  - `batch_target_results`.
- `V2__add_device_network_bindings.sql` crea:
  - `device_network_bindings`.
- `MasterWindowsBinding` no se persiste en SQLite por decision de seguridad; la autoridad final vive en Agent Service.
- `device_network_bindings` guarda solo referencias publicas de Device, Installation Identity, Network Identity, fingerprint publico, version de Agent, capabilities, registro y ultimo conectado; no guarda private keys, certificados privados, passwords, JWT ni trust secreto.
- IDs del dominio como `TEXT`, generados por la aplicacion; no usar `AUTOINCREMENT` para identidades funcionales.
- Timestamps como `TEXT` UTC producido desde `Instant.toString()`.
- Booleans como `INTEGER` `0/1` con `CHECK`.
- Enum sets serializados como JSON textual de nombres de enum ordenados; no hay serializacion binaria Java.
- Foreign keys habilitadas por conexion.
- WAL habilitado para mejorar lectura local concurrente.
- `busy_timeout` default 5000 ms.
- `maximum-pool-size` default 4.
- Corruption check con `PRAGMA quick_check`.
- Archivos de Network Identity y trust del Master viven junto al data directory del Master, separados de `classroom.db`.
- `master-network-identity.json` guarda solo metadata publica del Master.
- `master-network-identity.key` contiene private key cifrada; `master-network-identity.protector` contiene el material local de proteccion.
- `paired-clients.json` persiste Clients emparejados, challenges pendientes/consumidos y revocaciones.

NO IMPLEMENTADO:

- Cifrado at-rest de `classroom.db`.
- Backup/restore automatico.
- Auditoria persistente de acciones administrativas reales.
- Borrado seguro/retencion configurable de PII.
- DPAPI/keystore del sistema para private key del Master.

## Agent

El Agent se divide en dos procesos para separar privilegios de sistema y trabajo dentro de la sesion interactiva.

### Galtek Classroom Service

IMPLEMENTADO:

- Proyecto C# `GaltekClassroom.Agent.Service`.
- Worker Service / Generic Host en .NET 8.
- Puede arrancarse desde consola para desarrollo.
- Usa la integracion oficial `Microsoft.Extensions.Hosting.WindowsServices`.
- Puede ejecutarse como Windows Service instalado.
- Service Name estable: `GaltekClassroomAgent`.
- Display Name: `Galtek Classroom Agent Service`.
- Description: `Servicio local de Galtek Classroom para identidad, licencia y administracion segura del equipo.`
- Cuenta de servicio: `LocalSystem`.
- Startup type: `Automatic`.
- Recovery configurado por instalador: reiniciar ante fallos con retrasos de 5, 15 y 60 segundos; reset de contador cada 86400 segundos.
- `ServiceRunMarker` crea `agent-service.running` al inicio y lo elimina al cierre limpio para detectar shutdown no limpio sin escribir periodicamente.
- Startup rapido por fases: Installation Identity habilita `MINIMAL_READY`, Network Identity habilita `SECURITY_READY`, conexion autenticada habilita `NETWORK_READY` y licencia/operacion puede elevar a `OPERATION_READY`.
- La validacion comercial completa, incluida la parte que puede consultar hardware/WMI, queda fuera del camino critico inicial de Local IPC y conexion de red.
- Hosted services arrancan el Worker minimo, luego Local IPC, luego monitor de licencia y despues conexion al Master, para exponer control local antes que trabajo diferible.
- `Worker.StartAsync` devuelve tras resolver Installation Identity; Network Identity se resuelve en background y la conexion gRPC espera a `SECURITY_READY` antes de usar trust/red.
- Publicacion productiva inicial: `Release`, `win-x64`, self-contained, carpeta no single-file.
- Binarios instalados en `<ProgramFiles>\Galtek\Classroom\Agent\`.
- Scripts PowerShell en `installer/windows/` para publicar, instalar/actualizar y desinstalar.
- `uninstall-agent-service.ps1` conserva ProgramData por defecto y solo borra identidad/licencia/binding/Network Identity con `-PurgeData`.
- Version inicial del ejecutable controlada en `agent/src/GaltekClassroom.Agent.Service/GaltekClassroom.Agent.Service.csproj`.
- Registra inicio, estado activo y detencion limpia.
- Es autoridad local de Installation Identity.
- Es autoridad local de Commercial License.
- Es autoridad local de Master Windows Binding.
- Es autoridad local de Network Identity criptografica del Client.
- Resuelve `installation.json` al arrancar.
- Crea una identidad permanente cuando no existe.
- Reutiliza el mismo `installationId` cuando el archivo existe y es valido.
- Falla de forma controlada si `installation.json` esta corrupto o incompleto.
- Resuelve `network-identity.json` al arrancar despues de Installation Identity.
- Crea una Network Identity permanente cuando no existen metadata ni llave CNG previa.
- Reutiliza el mismo `networkIdentityId`, `keyId`, `keyName` y fingerprint cuando metadata y llave siguen validas.
- Falla de forma controlada ante metadata corrupta, llave faltante, fingerprint incompatible o `installationId` distinto.
- Expone CLI read-only `--network-identity-status`.
- `ClientPairingService` acepta challenges de pairing solo con aprobacion explicita.
- Valida que el challenge apunte al `installationId`, `networkIdentityId`, public key y fingerprint locales del Client.
- Verifica la firma del Master sobre el challenge y firma la respuesta con la private key del Client.
- Rechaza challenges expirados, malformados, con fingerprint inconsistente o ya consumidos.
- Persiste trust de Masters autorizados en `authorized-masters.json`, separado de `installation.json`, `license.dat`, `master-binding.json` y `network-identity.json`.
- `authorized-masters.json` conserva estados `PAIRING_PENDING`, `PAIRED` y `REVOKED`, y challenges consumidos para bloquear replay.
- Revocar un Master cambia su trust a `REVOKED` sin borrar Installation Identity ni Network Identity.
- Un Master `REVOKED` o no emparejado no puede administrar el Client; la autorizacion falla cerrado con `MASTER_NOT_PAIRED`.
- Dependencias gRPC .NET con generacion Protobuf desde `protocol/network/v1/galtek-classroom-network-v1.proto`.
- `MasterConnectionHostedService` inicia la conexion saliente hacia el Master solo cuando `Galtek:Classroom:Agent:MasterConnection:Enabled=true`.
- `MasterGrpcConnectionClient` exige `https`, `MasterNetworkIdentityId` configurado y trust local `PAIRED` antes de abrir el canal.
- El Client crea un certificado mTLS self-signed de corta vida desde su Network Identity local; la private key sigue en CNG/KSP y no se exporta a archivo.
- La validacion del certificado del Master usa pinning de public key contra `authorized-masters.json`, no CA global, IP, MAC ni hostname.
- `ClientHello` transporta `networkIdentityId`, `installationId`, fingerprint, public SPKI, version de Agent y capabilities tipadas; no transporta secretos.
- `ClientHello.device_id` queda como campo compatible pero el Master no lo usa como identidad; el `deviceId` persistente lo genera el Master al registrar el Device.
- `ClientCapabilityProvider` anuncia solo `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1` y `SESSION_AGENT_AVAILABLE`.
- Heartbeat periodico default cada 15 segundos y reconexion con backoff `2s, 5s, 10s, 30s`.
- Reconexion con jitter acotado: jitter inicial default hasta 2 segundos y jitter por retry default hasta 1 segundo, sin quitar el backoff base.
- El heartbeat del Agent conserva el stream TLS/mTLS persistente y ya no relee `authorized-masters.json` en cada ciclo; los `OperationRequest` revalidan trust antes de cualquier accion.
- Los retries repetidos de conexion gRPC se registran en `DEBUG` tras el primer warning para evitar spam de retry.
- `MasterConnectionStateTracker` mantiene estado local `CONNECTING`, `ONLINE` y `OFFLINE` derivado del stream autenticado.
- `RemoteOperationDispatcher` del Agent deduplica por `operationId`, aplica timeout y devuelve `OperationResult` estructurado; sin handlers productivos, toda operacion conocida o futura devuelve `OPERATION_NOT_IMPLEMENTED`.
- `RemoteOperationDispatcher` rechaza ejecucion de handlers cuando Commercial License todavia no esta activa, preservando el bloqueo comercial aunque la validacion completa se difiera fuera del startup critico.
- Genera Machine Code Base64 en modo de desarrollo con `--machine-code`.
- Valida licencias JWT firmadas con RSA / RS256.
- Rechaza algoritmos distintos a RS256 antes de confiar en el token.
- Exige issuer `galtek-hub`, audience `galtek-classroom`, product `GALTEK_CLASSROOM` y `schemaVersion = 1`.
- Exige `sub == installationId` sin modificar ni adoptar `installation.json`.
- Valida hardware actual 3 de 4 contra `cpuHash`, `motherboardHash`, `macHash` y `diskHash`.
- Valida expiracion en UTC y mantiene el Service vivo si la licencia esta vencida.
- Persiste el JWT comercial en `license.dat`, separado de `installation.json`, con escritura temporal y reemplazo/movimiento.
- Expone CLI de desarrollo para `--license-status`, `--activate-license` por STDIN y `--activate-license-file <ruta>`.
- Expone CLI administrativa para `--bind-master-current-user`, `--bind-master-account <WINDOWS_ACCOUNT>` y `--replace-master-binding`.
- Exige elevacion administrativa para crear o reemplazar `master-binding.json`; no autoeleva ni evade UAC.
- Persiste un unico Master binding en `master-binding.json`, separado de `installation.json` y `license.dat`.
- Valida schema v1, SID, `installationId` y campos obligatorios al leer el binding.
- No regenera silenciosamente binding ausente, corrupto, incompleto, con schema desconocido o SID invalido.
- Escribe el binding mediante archivo temporal, flush y replace/move atomico; verifica lectura posterior.
- Endurece `master-binding.json` para escritura por `LocalSystem` y `BuiltinAdministrators`.
- Mantiene `LicenseState` en memoria sin exponer el JWT completo.
- Monitor ligero de expiracion runtime cada 60 segundos, sin recalcular WMI.
- Local IPC API v1 read-only mediante Windows Named Pipes.
- Named Pipe server versionado `GaltekClassroom.Agent.v1`.
- Framing IPC con prefijo de longitud de 4 bytes BIG ENDIAN mas JSON UTF-8.
- Limite maximo de mensaje de 64 KiB.
- Operaciones IPC permitidas: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`.
- `GET_DEVICE_STATUS` expone solo estado seguro y no expone JWT ni hashes de hardware.
- `GET_DEVICE_STATUS` expone `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive` para diagnostico local de recovery.
- `GET_MACHINE_CODE` reutiliza la implementacion existente de Machine Code y no exige licencia activa.
- `GET_MASTER_AUTHORIZATION` deriva el SID real del cliente Named Pipe y no acepta SID en el payload.
- Requests IPC exitosos y conexion IPC saludable se registran en `DEBUG`, no en `INFO`, para evitar logs periodicos durante idle.
- ACL actual del pipe: `LocalSystem` y `BuiltinAdministrators` con `FullControl`; `Authenticated Users` con `ReadWrite | Synchronize`.

PLANIFICADO:

- Empaquetar la llave publica real de Galtek Hub como recurso/mecanismo productivo.
- Revalidacion completa explicita invocable por IPC.
- Handlers productivos para operaciones remotas tipadas.
- Operaciones privilegiadas.
- Custodia futura de credenciales de cuentas Windows administradas de Client, protegidas con mecanismos seguros de Windows.
- Integracion futura soportada por Windows para logon/switch, contemplando Credential Provider.
- Coordinacion con Session Agent.
- Coordinacion funcional con Session Agent para operaciones futuras.

NO IMPLEMENTADO:

- Galtek Hub.
- Generacion de licencias comerciales.
- Private keys comerciales.
- Activacion online.
- Revocacion online.
- Descarga runtime de llaves publicas.
- DPAPI o endurecimiento avanzado de ACL.
- Clock rollback.
- Enforcements de features.
- Passwords reales de cuentas Windows administradas.
- DPAPI aplicado a secretos de cuentas administradas.
- Credential Provider.
- Login/logoff Windows real.
- Cambio real de usuario Windows.
- Autologon inseguro, SendKeys, scripts de automatizacion Windows o shell arbitraria para iniciar sesion.
- Endpoints/IPC de pairing reales expuestos a UI/transporte.
- Comandos MASTER protegidos por autorizacion de red.
- Comandos remotos.
- Comunicacion de red.
- Lanzamiento de procesos de sesion interactiva desde el Windows Service.

### Galtek Classroom Session Agent

IMPLEMENTADO:

- Proyecto C# `GaltekClassroom.Agent.Session`.
- Ejecutable productivo `WinExe` para no mostrar consola al autoarrancar.
- Modo background explicito `--background`.
- Sin argumentos equivale a modo background para conservar un entrypoint productivo sencillo.
- Arranque automatico mediante Scheduled Task `GaltekClassroomSessionAgent`.
- Trigger de tarea `AtLogon` para usuarios interactivos del equipo.
- Principal de tarea por grupo `S-1-5-32-545` (Builtin Users), sin credenciales guardadas.
- Run level `Limited`; no corre como `LocalSystem` ni eleva artificialmente privilegios.
- Configuracion de tarea sin requerimiento de red ni corriente AC, con ejecucion prolongada y restart acotado.
- `MultipleInstances Parallel` en Task Scheduler para no bloquear futuras sesiones Windows multiples.
- Single instance por sesion mediante named mutex local `Local\GaltekClassroom.Agent.Session`.
- Validacion de `Process.SessionId`; el modo background no debe ejecutarse normalmente en `SessionId = 0`.
- Supervisor IPC local con estados internos `STARTING`, `WAITING_FOR_SERVICE`, `CONNECTED`, `READY` y `STOPPING`.
- Reconexion automatica al Agent Service con backoff acotado `2s`, `5s`, `10s`, `30s`.
- Polling saludable por `PING` cada 15 segundos.
- Permanece activo si el Service no esta disponible temporalmente.
- Permanece activo aunque la Commercial License no este `ACTIVE`.
- Cliente IPC local para consultar al Agent Service.
- Comandos de desarrollo `--ipc-status` y `--ipc-ping`.
- Los comandos `--ipc-status` y `--ipc-ping` son one-shot, imprimen JSON seguro y terminan sin iniciar background.
- Publicacion productiva `Release`, `win-x64`, self-contained, carpeta normal, sin single-file.
- Artifact publicado en `artifacts/windows/agent-session/`, ignorado por Git.
- Instalacion en `<ProgramFiles>\Galtek\Classroom\Agent\Session\`.
- Scripts PowerShell para publicar, instalar/actualizar y desinstalar el Session Agent.
- Orquestadores PowerShell para publicar, instalar/actualizar y desinstalar el Agent completo.

PLANIFICADO:

- Captura de pantalla.
- Recepcion de proyeccion.
- Interaccion con escritorio.
- Bloqueo de entrada.
- Ejecucion controlada de aplicaciones.
- Overlays.
- Contrato futuro Service <-> Session que identifique `sessionId`, contexto de usuario y estado active/interactive sin tratarlos como seguridad por si solos.

NO IMPLEMENTADO:

- UI.
- Captura de pantalla.
- Bloqueo de teclado/mouse.
- Proyeccion.
- IPC Service -> Session write.
- Registro IPC de sesiones.
- Comandos remotos.

### GaltekClassroom.Agent.Shared

IMPLEMENTADO:

- Proyecto C# `GaltekClassroom.Agent.Shared`.
- Constantes minimas de producto.
- Modelos compartidos de Installation Identity.
- Modelo de Machine Code.
- Constantes de `schemaVersion` y archivo `installation.json`.
- Constantes de Commercial License.
- Modelo `LicenseState`, estados internos, roles conocidos y features extensibles.
- Contratos IPC v1.
- Framing IPC v1.
- Constantes de operaciones, errores, nombre de pipe y limite de mensaje.
- Contratos futuros minimos para operaciones tipadas, estados batch, estados por target, preflight, destinos logicos, conflict policies y errores operacionales.
- Constantes futuras para cuentas administradas `PRIMARY`/`SECONDARY`, estados de sesion Windows y acciones `NO_CHANGE`, `LOGON`, `SWITCH`, `PENDING`, `BLOCKED`.
- Constantes futuras para perfiles de performance, clases de trabajo de recursos, estado `DEGRADED` y trabajo sacrificable.

PLANIFICADO:

- Contratos de red cuando se definan los `.proto`.
- Contratos concretos Service <-> Session para ejecucion controlada, cuando exista autorizacion local e IPC write disenado.

## Comunicacion de red

IMPLEMENTADO:

- Modelo local de pairing criptografico Master-Client con challenge/response firmado.
- `PAIRING_PENDING`, `PAIRED`, `REVOKED` y error funcional `MASTER_NOT_PAIRED`.
- Persistencia bilateral de trust: `paired-clients.json` en Master y `authorized-masters.json` en Client.
- Proteccion contra replay mediante challenges pendientes/consumidos y nonce.
- Expiracion de challenge de 5 minutos.
- Soporte conceptual para multiples Clients por Master y multiples Masters por Client.
- Revocacion bilateral conceptual: un registro `REVOKED` bloquea administracion y no se reutiliza silenciosamente.
- Protocolo gRPC/Protobuf v1 en `protocol/network/v1/galtek-classroom-network-v1.proto`.
- Servicio `NetworkConnection.Connect` con stream persistente iniciado por el Client.
- TLS/mTLS obligatorio; no hay fallback plaintext.
- Certificados self-signed de corta vida ligados al trust existente por fingerprint de public key `SubjectPublicKeyInfo`.
- El Master valida certificados de Client contra `paired-clients.json`.
- El Client valida el certificado del Master contra `authorized-masters.json`.
- `ClientHello` identifica al Client por Network Identity, installation id, fingerprint y public SPKI, nunca por secreto, y reporta version/capabilities operativas tipadas.
- Capabilities tipadas vigentes: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1` y `SESSION_AGENT_AVAILABLE`.
- Capabilities desconocidas se ignoran y no otorgan permisos.
- `device_network_bindings` vincula un Client paired con un Device persistente generado por el Master; SQLite no reemplaza `paired-clients.json`.
- Clients `PAIRED + ONLINE` sin Device se exponen como `AVAILABLE_FOR_REGISTRATION`.
- Heartbeat periodico del Client con `HeartbeatAck` del Master.
- Heartbeat pequeno, sin polling HTTP, sin telemetria pesada, sin logs sanos y sin writes persistentes por ciclo.
- Estado real de conexion `CONNECTING`, `ONLINE` y `OFFLINE` derivado de streams autenticados.
- Framework Protobuf compatible para `OperationRequest`, `OperationAccepted` y `OperationResult`, con `operationId`, `operationType`, `targetDeviceId`, `protocolVersion`, timeout y `ErrorCode` tipado.
- El Agent deduplica `OperationRequest` por `operationId`; una operacion no implementada devuelve `OPERATION_NOT_IMPLEMENTED` y no toca Windows.
- Timeout de heartbeat default 45 segundos en el Master.
- Reconexión del Client con backoff acotado.

PLANIFICADO:

- El descubrimiento usara mDNS/DNS-SD en una fase posterior.
- Exponer pairing/discovery mediante flujos reales de red sin confundir discovery con trust, en una fase posterior.
- Handlers reales de comandos administrativos remotos tipados sobre el framework de operaciones, en una fase posterior.

NO IMPLEMENTADO:

- Descubrimiento real.
- APIs reales de discovery/pairing sobre red.
- Comandos remotos.

Nota de seguridad: descubrir un equipo no significa confiar en el. Network Identity tampoco equivale a trust; el trust aparece solo tras pairing explicito y puede revocarse.

## IPC local

IMPLEMENTADO:

- Windows Named Pipe `GaltekClassroom.Agent.v1`.
- `GaltekClassroom.Agent.Service` es el servidor IPC.
- `GaltekClassroom.Agent.Session` consume `PING` y `GET_DEVICE_STATUS` en CLI one-shot y en supervisor background.
- Master Backend Java consume `GET_DEVICE_STATUS`, `GET_MACHINE_CODE` y `GET_MASTER_AUTHORIZATION`.
- Protocolo documentado en `protocol/local-ipc-v1.md`.
- `protocolVersion = 1`.
- Mensajes JSON UTF-8 con prefijo de longitud de 4 bytes BIG ENDIAN.
- Limite de payload de 64 KiB.
- Operaciones permitidas en v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`.
- IPC v1 es read-only.
- El SID de autorizacion Master se deriva del token real del cliente conectado al Named Pipe mediante impersonation; no viene del payload.
- Version desconocida devuelve `IPC_PROTOCOL_UNSUPPORTED`.
- Operacion desconocida devuelve `IPC_OPERATION_NOT_SUPPORTED`.
- JSON malformado, longitud invalida y desconexiones de clientes se manejan sin detener el Service.

NO IMPLEMENTADO:

- Activacion de licencia por IPC.
- Operaciones write por IPC.

## Identidades

### Installation Identity

IMPLEMENTADO:

- Identidad permanente de la instalacion.
- Persistencia en `installation.json`.
- `schemaVersion = 1`.
- `installationId` como GUID permanente.
- Componentes: `cpuHash`, `motherboardHash`, `macHash`, `diskHash`.
- Hash local con SHA-256.
- Normalizacion previa con trim, mayusculas, colapso de espacios, filtro de placeholders sencillos y orden determinista.
- Solo se guardan hashes de hardware, no seriales crudos.
- CPU, motherboard y discos se obtienen por WMI mediante `System.Management`.
- MAC fisicas se obtienen con `NetworkInterface`, filtrando adaptadores virtuales, loopback y tuneles en la medida razonable.
- Si hay multiples valores validos se normalizan, ordenan y hashean como un solo componente determinista.
- Machine Code Base64 contiene producto, schema, `installationId`, cuatro hashes y hostname informativo.

NO IMPLEMENTADO:

- Reparacion automatica de identidades corruptas.

### Commercial License

IMPLEMENTADO:

- Validacion local en `GaltekClassroom.Agent.Service`.
- JWT firmado por Galtek Hub con RSA / RS256.
- Abstraccion `ILicensePublicKeyProvider`.
- Proveedor actual por ruta explicita de desarrollo `GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH`.
- Estado `LICENSE_KEY_NOT_CONFIGURED` cuando no hay llave publica configurada.
- Claims estructurales obligatorios: `iss`, `aud`, `sub`, `jti`, `product`, `schemaVersion`, `cpuHash`, `motherboardHash`, `macHash`, `diskHash`, `roles`, `iat`, `exp`.
- `organizationId` opcional.
- `features` opcional como objeto extensible.
- Roles conocidos `CLIENT` y `MASTER`; el Agent exige al menos `CLIENT`.
- Estados explicitos para licencia activa, faltante, vencida, alterada, malformada, invalida, mismatch de instalacion/producto/hardware, schema no soportado, issuer/audience y rol no operativo.
- Activacion/renovacion valida antes de reemplazar `license.dat`.
- Si una activacion nueva falla, no se borra ni reemplaza una licencia valida anterior.
- La validacion completa obtiene el fingerprint de hardware actual al arrancar y al activar/renovar.
- Expiracion runtime de `ACTIVE` a `LICENSE_EXPIRED` sin reinicio.

NO IMPLEMENTADO:

- Galtek Hub.
- Llave publica productiva real embebida.
- Private key comercial.
- Generacion local de licencias.
- Activacion online.
- Revocacion online.
- Descarga o confianza en llaves enviadas junto con JWT.
- Enforcement de features.

### Master Windows Binding

IMPLEMENTADO:

- Modelo Java `MasterWindowsBinding` con `installationId`, `windowsSid`, `accountDisplayName` y `boundAtUtc`, conservado como modelo funcional y para tests.
- Modelo `MasterAuthorizationState` con `NOT_CONFIGURED`, `AUTHORIZED`, `CURRENT_ACCOUNT_NOT_AUTHORIZED`, `MASTER_LICENSE_REQUIRED`, `MASTER_BINDING_INVALID` e `INSTALLATION_MISMATCH`.
- Politica Java de Prompt 07 conservada como modelo puro; no es autoridad productiva.
- Abstraccion `CurrentWindowsIdentityProvider`.
- Implementacion Java `JdkCurrentWindowsIdentityProvider` que obtiene SID mediante API del JDK por reflexion, sin comandos shell ni JNA.
- Pruebas puras que no dependen de la cuenta Windows real.
- Prompt 08 confirma que `classroom.db` no persiste `MasterWindowsBinding` ni crea tabla `master_windows_binding`.
- Prompt 09 persiste y verifica el binding final en el Agent Service, autoridad local de Installation Identity, Commercial License y Master Windows Binding.
- Archivo `master-binding.json` con schema v1 en `<CommonApplicationData>\Galtek\Classroom\`.
- Unica cuenta autorizada por instalacion: exactamente cero o un binding.
- Binding ligado a `installationId`; un archivo copiado desde otra instalacion produce `MASTER_BINDING_INSTALLATION_MISMATCH`.
- Binding corrupto, incompleto, schema desconocido o SID invalido produce `MASTER_BINDING_INVALID` sin tumbar el Service.
- `GET_MASTER_AUTHORIZATION` expone al Master Backend solo estado derivado seguro, sin SID completo ni rutas internas.
- `MasterAuthorizationService` en Agent Service evalua Installation Identity, `LicenseState`, binding y SID real del caller.
- CLI administrativa elevada crea/reemplaza binding con `--bind-master-current-user`, `--bind-master-account <WINDOWS_ACCOUNT>` y `--replace-master-binding`.
- Otro administrador Windows no hereda permiso Master si su SID no esta ligado.

PLANIFICADO:

- UI futura para configurar/diagnosticar binding, sin ser autoridad.
- IPC write futuro de binding solo si se disena una autorizacion local adecuada.

NO IMPLEMENTADO:

- Reemplazo de Network Identity o mTLS.
- Autorizacion remota por transporte real entre equipos.

### Network Identity

IMPLEMENTADO:

- Identidad criptografica de red separada de la licencia comercial.
- `GaltekClassroom.Agent.Service` es la autoridad local de Network Identity del Client.
- `network-identity.json` vive en `<CommonApplicationData>\Galtek\Classroom\`.
- `network-identity.json` contiene solo metadata publica: `schemaVersion`, `networkIdentityId`, `installationId`, `keyId`, `keyName`, `publicKeyFingerprint` y `createdAtUtc`.
- `networkIdentityId` es un GUID propio y estable, distinto de `installationId` y de cualquier licencia comercial.
- `installationId` queda asociado en metadata y se valida contra la Installation Identity actual.
- `keyId` y `keyName` se derivan deterministamente del `installationId` para detectar estados parciales sin adoptar datos de otra instalacion.
- La llave privada se genera localmente en el Client y no se guarda en JSON.
- La llave privada usa Windows CNG/KSP de maquina con Microsoft Software Key Storage Provider.
- La llave inicial es RSA 2048, de uso de firma y no exportable.
- El fingerprint es SHA-256 de la public key en formato `SubjectPublicKeyInfo`, serializado como hexadecimal minuscula.
- Primera ejecucion sin metadata ni llave previa crea par de claves y metadata.
- Reapertura conserva `networkIdentityId`, `keyId`, `keyName`, public key y fingerprint.
- Metadata corrupta, schema desconocido, `keyId`/`keyName` incompatible o fingerprint que no coincide produce `NETWORK_IDENTITY_INVALID`.
- Metadata valida con llave CNG faltante produce `NETWORK_IDENTITY_KEY_MISSING`.
- Metadata de otro `installationId` produce `NETWORK_IDENTITY_INSTALLATION_MISMATCH`.
- Si falta metadata pero ya existe la llave CNG esperada, no se regenera silenciosamente y se reporta estado invalido.
- CLI read-only `--network-identity-status` muestra estado, `networkIdentityId` y `publicKeyFingerprint`, nunca llave privada.
- `-PurgeData` intenta eliminar la llave CNG solo cuando puede leer un `keyName` valido con prefijo de Galtek desde `network-identity.json`; no borra llaves a ciegas.
- El Master Backend tiene una Network Identity local propia, separada de la del Client.
- `master-network-identity.json` contiene metadata publica del Master: `schemaVersion`, `masterNetworkIdentityId`, `keyId`, `publicKeyFingerprint`, `publicKeySubjectPublicKeyInfoBase64` y `createdAtUtc`.
- La private key del Master se guarda fuera de SQLite y fuera de JSON plano, cifrada en `master-network-identity.key`.
- `master-network-identity.protector` guarda el material local que protege la private key del Master.
- El Master no usa `classroom.db` para private keys ni trust.
- Network Identity no equivale a trust: una identidad valida solo permite firmar/verificar challenge y response.
- El transporte gRPC/mTLS emite certificados self-signed de corta vida desde las claves de Network Identity y valida el peer por fingerprint SPKI persistido en trust.
- No existe CA global que confie automaticamente en cualquier instalacion.

PLANIFICADO:

- Rotacion manual/operacional de claves.
- Hardening productivo adicional para la private key del Master y ciclo de vida de certificados.

NO IMPLEMENTADO:

- mDNS/discovery real.
- Comandos remotos.
- Autorizacion de comandos Master-Agent sobre transporte real.
- Confianza automatica entre equipos.
- Rotacion automatica de claves.

### Pairing y Trust

IMPLEMENTADO:

- Pairing criptografico Master-Client basado en Network Identity de ambos lados.
- El Master crea un `PairingChallenge` con `challengeId`, nonce, timestamps UTC, fingerprints y public keys de Master y Client.
- El challenge expira a los 5 minutos.
- El challenge se firma con la private key del Master.
- El Client acepta el challenge solo con aprobacion explicita de pairing.
- El Client valida formato, expiracion, destino local, fingerprints y firma del Master.
- El Client responde con `PairingResponse` firmado con su private key.
- El Master valida que la respuesta coincida con un challenge pendiente, no expirado y no consumido.
- El Master verifica la firma del Client usando la public key incluida y fijada en el challenge.
- La proteccion contra replay usa challenges pendientes/consumidos, `challengeId` y nonces.
- El trust se persiste en ambos lados:
  - Master: `paired-clients.json`.
  - Client: `authorized-masters.json`.
- Estados de trust: `UNPAIRED`, `PAIRING_PENDING`, `PAIRED`, `REVOKED`.
- `REVOKED` bloquea administracion y no se reutiliza silenciosamente.
- IP, MAC, hostname, discovery y licencia MASTER no crean pairing ni autorizan Clients.
- Soporte conceptual para multiples Clients por Master y multiples Masters por Client.

NO IMPLEMENTADO:

- Endpoints HTTP/gRPC reales de pairing.
- CA global o certificados que otorguen confianza automatica.
- mDNS/discovery real.
- Comandos remotos.

## Almacenamiento local del Agent

IMPLEMENTADO:

- Usar `<CommonApplicationData>\Galtek\Classroom\`.
- Obtener la ruta mediante APIs de .NET, sin hardcodear `C:\ProgramData`.
- Permitir override de desarrollo y tests con `GALTEK_CLASSROOM_DATA_DIR`.
- Archivo `installation.json` para Installation Identity.
- Archivo `license.dat` para Commercial License.
- Archivo `master-binding.json` para Master Windows Binding.
- Archivo `network-identity.json` para metadata publica de Network Identity.
- Archivo `authorized-masters.json` para trust persistido de Masters emparejados con el Client.
- Llave privada de Network Identity fuera de JSON, en Windows CNG/KSP de maquina.
- Escritura de identidad y licencia con archivo temporal y reemplazo/movimiento para evitar archivos parciales.
- `DurableFileWriter` centraliza escritura de archivos criticos del Agent con temp file, flush/fsync y reemplazo/movimiento atomico.
- Escritura del Master binding con archivo temporal, flush y reemplazo/movimiento atomico.
- Escritura de `network-identity.json` mediante archivo temporal, flush y move atomico sin sobrescritura automatica.
- `license.dat` guarda solo el JWT recibido.
- `master-binding.json` no guarda password, hashes de password, tokens, credenciales ni JWT.
- `network-identity.json` no guarda private key, secretos ni licencia comercial.
- `authorized-masters.json` no guarda private keys, passwords, JWT ni secretos; guarda public keys/fingerprints y estados de trust.
- No existe todavia almacenamiento de credenciales de cuentas Windows administradas de Client.
- Los scripts de instalacion separan binarios en `<ProgramFiles>\Galtek\Classroom\Agent\` y datos persistentes en `<CommonApplicationData>\Galtek\Classroom\`.
- Actualizar o desinstalar normalmente no borra `installation.json`, `license.dat`, `master-binding.json`, `network-identity.json`, `authorized-masters.json` ni la llave CNG de Network Identity.

NO IMPLEMENTADO:

- Base de datos local.
- Logs persistentes en disco.
- DPAPI/ACL hardening avanzado para `license.dat`.
- Almacenamiento seguro futuro de credenciales `PRIMARY`/`SECONDARY`.

Nota de seguridad: en esta fase `license.dat` no depende de confidencialidad para integridad. El JWT esta firmado, ligado a `installationId` y ligado al hardware por regla 3 de 4. El cifrado o endurecimiento local queda para una fase posterior.

## Limites de seguridad

VIGENTE DESDE AHORA:

- No permitir ejecucion remota arbitraria.
- No aceptar `cmd.exe /c`, PowerShell arbitrario, shell remota ni rutas arbitrarias enviadas por un Master.
- Usar comandos futuros explicitos y estructurados, por ejemplo `LOCK_INPUT`, `UNLOCK_INPUT`, `OPEN_APPLICATION` con `appId`, `SHUTDOWN`, `RESTART`, `START_PROJECTION`, `STOP_PROJECTION`.
- Las operaciones futuras de cuentas Windows administradas deben usar `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`.
- Los comandos futuros para cuentas administradas solo enviaran `accountId` logico (`PRIMARY`/`SECONDARY`), nunca passwords.
- El Master no almacenara passwords de cuentas Windows administradas en `classroom.db` ni los enviara en comandos normales.
- La UI futura nunca recibira passwords ni secretos de cuentas administradas.
- Logs no deben mostrar passwords ni material equivalente.
- La credencial real futura pertenecera al Agent Service del Client y debera protegerse con mecanismos seguros de Windows.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para iniciar o cambiar sesion Windows.
- El mecanismo productivo de login/cambio de usuario debe disenarse posteriormente con integracion soportada por Windows, contemplando Credential Provider.
- Mantener siempre una via estandar de acceso/recovery de Windows fuera de Galtek.
- Las aplicaciones abribles remotamente deben pertenecer a un catalogo configurado previamente.
- Operaciones de contenido deben usar destinos logicos de `StudentWorkspace`; el Master no debe enviar rutas absolutas arbitrarias ni path traversal.
- `Device` y `Student` son entidades independientes; mover un alumno es un workflow de alumno/workspace, no una copia manual de una carpeta de PC a PC.
- Browser profiles modelan portabilidad sin copiar passwords, cookies ni cache protegido.
- `PRIMARY` y `SECONDARY` no son kiosco por default; no deben bloquear aplicaciones, modificar archivos de alumno, forzar programas, restringir Windows, cambiar sesion ni bloquear input al iniciar clase salvo accion administrativa futura explicita.
- Office, Chrome, Scratch, RoboMind y aplicaciones escolares siguen ejecutandose localmente en Clients.
- Los documentos del alumno usan destinos logicos; Windows y aplicaciones conservan acceso normal a `AppData`, `Temp`, caches y configuracion interna necesaria.
- `REMOVABLE_STORAGE` es un destino logico futuro para USB autorizado; el Master no envia rutas arbitrarias de unidades removibles.
- El workspace canonico vive en el Master y la working copy local vive en el Client mientras el alumno usa esa PC.
- Nunca limpiar la working copy local antes de `SYNC -> VERIFY -> COMMIT CANONICAL -> CONFIRM`.
- Si red o energia fallan antes de confirmar, conservar la working copy local y reportar `PENDING_SYNC` o `RECOVERY_REQUIRED`, sin asumir `SUCCESS`.
- Una distribucion grande no debe impedir operaciones `CRITICAL` como `UNLOCK_INPUT` o `STOP_PROJECTION`.
- `OPEN_URL`/`OPEN_WEB_CONTENT` deben preferir abrir Chrome localmente en el Client; no convertir YouTube en captura 30 FPS hacia 26 PCs.
- Preview de PCs futuro debe ser opt-in operacional: thumbnails pequenos, baja frecuencia, solo devices visibles y concurrencia limitada; no iniciar captura al boot ni por `ClientHello`.
- Batch-first es obligatorio: targets pueden ser classroom, group, students, devices o items individuales cuando la accion tenga sentido.
- `PARTIAL_SUCCESS` y retry solo de fallidos deben formar parte del modelo de cualquier operacion masiva.
- `TARGET_OCCUPIED` nunca debe sobrescribir ni borrar al alumno que ocupa el equipo.
- IP, MAC y hostname no son identidad de autorizacion.
- Discovery no es pairing; discovery solo encuentra candidatos.
- Network Identity no es trust; una identidad valida no autoriza administracion sin pairing.
- Pairing requiere intencion explicita y challenge/response firmado por ambas private keys.
- El trust de pairing se persiste en Master y Client.
- Un trust `REVOKED` no puede administrar el Client.
- Licencia comercial no reemplaza pairing, certificados ni autorizacion de red.
- Licencia MASTER valida no equivale a permiso automatico para controlar clientes de la LAN.
- Autorizacion Master productiva proviene del Agent Service, no del Master Backend.
- El SID declarado por payload, UI o JSON no es prueba de identidad.
- El SID local del caller debe derivarse del token real del Named Pipe.
- Ante duda o error de autorizacion Master, el resultado debe ser `authorized=false`.
- Un administrador Windows distinto no hereda Master si su SID no esta ligado.
- `MasterWindowsBinding` nunca se persiste en SQLite.
- Rebinding de Master siempre requiere intencion explicita.
- IPC v1 es read-only; acceso al pipe no equivale a autorizacion para futuras operaciones privilegiadas.
- Las operaciones futuras que aumenten control requeriran licencia activa.
- Solo un Master localmente autorizado y con trust de pairing vigente podra ordenar logon/logoff/switch en Clients cuando se implementen comandos administrativos futuros sobre el transporte seguro.
- Existe transporte gRPC/mTLS minimo para conexion, identificacion y heartbeat; todavia no existe mDNS, discovery real ni comandos remotos.
- El transporte seguro usa el trust ya establecido por pairing y no redisena pairing como discovery.
- Las operaciones futuras de recuperacion, como `UNLOCK_INPUT` y `STOP_PROJECTION`, no deben bloquearse por expiracion para evitar dejar equipos atrapados.
