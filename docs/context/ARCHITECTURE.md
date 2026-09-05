# Arquitectura

## Estado general

Prompt 19E1 implementa `PROVISION_MANAGED_CREDENTIAL` como operacion remota tipada y secret-bearing sobre el gRPC/mTLS existente. El Master solo puede enviar `accountId` tipado `PRIMARY`/`SECONDARY` y `password_utf16le` como bytes UTF-16LE sin BOM ni NUL; no envia username, SID, `accountReference`, `credentialId`, vault token, master password, comandos ni payload generico. El Agent Service valida framing, accountId y longitud, copia el secreto a un buffer mutable controlado, valida el binding local `managed-windows-accounts.json`, protege inmediatamente con `ManagedWindowsCredentialStore`/DPAPI y limpia el buffer. La capability `MANAGED_CREDENTIAL_PROVISIONING_V1` se anuncia porque existen handler productivo, store DPAPI, binding store y mapping Protobuf. El dedupe del Agent usa una firma secret-safe para esta operacion (`operationId`, tipo, target, protocolVersion y accountId), nunca password ni hash; duplicados con mismo `operationId + accountId` devuelven el resultado original sin reaplicar. No agrega Credential Vault bridge, endpoint HTTP Master, BatchOperation, Local IPC, Session Agent password handling, login/logoff/switch, status query nuevo, retry automatico ni trabajo idle.

Prompt 19D implementa en `GaltekClassroom.Agent.Service` el Client secure credential store para passwords Windows de `PRIMARY`/`SECONDARY`. La fuente de verdad es `<CommonApplicationData>\Galtek\Classroom\managed-windows-credentials.dat` usando `GALTEK_CLASSROOM_DATA_DIR`, separada de `managed-windows-accounts.json` y del `credential-vault.dat` del Master. El envelope externo solo conserva `schemaVersion`, `installationId`, `accountId`, `protectedData` y timestamps; el payload interno binario protegido por DPAPI contiene `accountId`, `windowsSid` y password. DPAPI usa scope de usuario actual del proceso productivo, exige LocalSystem (`S-1-5-18`), usa `CRYPTPROTECT_UI_FORBIDDEN`, no usa LocalMachine ni fallback plaintext, y agrega optional entropy deterministica por `schemaVersion`/`installationId`/`accountId`. El store interno expone GetStatus/Add/Replace/Remove/Acquire con lease disposable y limpieza de buffers controlados; no agrega CLI password, reveal, Local IPC, Protobuf, UI, provisioning remoto, login/logoff/switch, heartbeat fields, polling ni trabajo idle.

Prompt 19C implementa en el Agent Service del Client `GET_WINDOWS_SESSION_STATE` productivo y read-only. La autoridad es la sesion asociada a la consola fisica por `WTSGetActiveConsoleSessionId()`, no procesos, username, foreground window, WMI, Registry, perfiles ni Session Agent. Si la consola no tiene usuario, se devuelve `NO_SESSION`; si el SID real del `TokenUser` de la sesion fisica coincide con el binding local `PRIMARY` o `SECONDARY`, se devuelve `PRIMARY_ACTIVE` o `SECONDARY_ACTIVE`; si hay un usuario real con SID no administrado o sin bindings configurados, se devuelve `OTHER_SESSION_ACTIVE`; condiciones transitorias o no confiables devuelven `UNKNOWN` o error estructurado `WINDOWS_SESSION_UNKNOWN`. El resultado remoto usa `WindowsSessionStateResult` tipado y no expone SID, username, accountReference ni sessionId. La capability `WINDOWS_SESSION_STATE_V1` se anuncia por `ClientCapabilityProvider`. No agrega UI, endpoint/batch Master, heartbeat state, polling, Local IPC, Session Command, Session Agent dependency, browser policy integration, passwords, credential store, login/logoff/switch ni writes.

Prompt 19B agrega en cada Client la fuente de verdad local `managed-windows-accounts.json` para vincular los slots logicos exactos `PRIMARY` y `SECONDARY` con cuentas Windows reales por SID. El documento vive en `<CommonApplicationData>\Galtek\Classroom\`, usa el data directory/override vigente del Agent, queda ligado al `installationId`, se escribe con `DurableFileWriter` y ACL restringida a `LocalSystem`/`Builtin Administrators`, y falla cerrado como `MANAGED_ACCOUNT_BINDINGS_INVALID` ante corrupcion, schema desconocido, mismatch de instalacion, slots duplicados, accountId desconocido, SID invalido o SID compartido. El binding guarda solo `accountId`, `windowsSid`, `accountReference`, `createdAtUtc` y `updatedAtUtc`; no guarda passwords, hashes, credentialId, tokens, profile paths ni session data. La CLI local administrativa agrega list/bind/remove con replace explicito y mutaciones elevadas. No agrega Local IPC, Protobuf, gRPC, Session Agent, browser policy integration, Credential Vault integration, DPAPI, Client credential store, login/logoff/switch ni Java productivo.

Prompt 19A agrega en el Master Backend el nucleo interno Java-only de Credential Vault local cifrado para credenciales escolares de la profesora. La fuente de verdad es `<CommonApplicationData>\Galtek\Classroom\Master\credential-vault.dat`, usando el mismo resolver/override del Master data directory. El archivo guarda un envelope JSON versionado con header tecnico minimo y el documento logico completo cifrado mediante AES-256-GCM; la master password separada deriva una KEK con PBKDF2-HMAC-SHA256 y solo envuelve un DEK aleatorio de 256 bits. La boveda no se crea en startup, queda locked tras restart, permite una sola sesion temporal in-memory con expiracion lazy de 5 minutos, CRUD interno, reveal de una credencial a la vez y cambio de master password por re-wrap del DEK. No agrega UI, endpoints HTTP, Protobuf, gRPC, Client credential store, login Windows, browser automation ni SQLite migrations.

Prompt 18B2 agrega dispatch batch Master para `LOCK_INPUT` y `UNLOCK_INPUT`. Expone endpoints separados `POST /api/classrooms/{classroomId}/input-control/lock` y `POST /api/classrooms/{classroomId}/input-control/unlock`, ambos con request estricta solo de `targetDeviceIds`. `lock` usa `MasterAccessGuard` y `unlock` usa `MasterUnlockAccessGuard`; ambos guards corren antes de leer Classroom, Devices, bindings, trust o SQLite escolar. El Master congela preflight por Device con binding, trust `PAIRED` no `REVOKED`, conexion gRPC/mTLS `ONLINE` y capability `INPUT_CONTROL_V1`, persiste una unica `BatchOperation` antes del fanout y envia operaciones tipadas sin parametros funcionales. No agrega UI, overlay, lock status, retry automatico, reconciliacion, Protobuf, Local IPC ni cambios Agent.

Prompt 18B1 agrega una ruta local read-only y recovery-safe para autorizar `UNLOCK_INPUT` desde el Master aun cuando la Commercial License local del Master no este `ACTIVE`. Local IPC v1 conserva `protocolVersion = 1` y suma `GET_MASTER_UNLOCK_AUTHORIZATION`, calculado exclusivamente por `GaltekClassroom.Agent.Service` con Installation Identity valida, `master-binding.json` existente/valido, `installationId` coincidente y SID real del caller Named Pipe obtenido por impersonation. No lee SID del payload, username, headers ni membresia de Administrators, no exige licencia activa, no lee claims de licencia invalida y no escribe archivos. El Master Backend Java agrega `LocalAgentClient.getMasterUnlockAuthorization()` y `MasterUnlockAccessGuard.requireUnlockAuthorized()` como guard interno de proposito unico para `UNLOCK_INPUT`; no hay fallback desde `MasterAccessGuard`.

Prompt 18A implementa `LOCK_INPUT` y `UNLOCK_INPUT` productivos del lado Client sin endpoint/batch Master. El Protobuf v1 conserva `protocolVersion = 1`, agrega `INPUT_CONTROL_V1` y errores `INPUT_LOCK_FAILED`/`INPUT_UNLOCK_FAILED`; las operaciones no llevan payload funcional. El Agent Service recibe `OperationRequest`, pasa por `RemoteOperationDispatcher`, aplica Commercial License activa para `LOCK_INPUT` y una excepcion recovery-safe estricta para `UNLOCK_INPUT`, y envia Session Command v1 tipado. El Session Agent ejecuta el control fisico mediante `User32.dll BlockInput(BOOL)` desde un `WindowsInputBlockCoordinator` con worker dedicado lazy para cumplir ownership de thread lock/unlock. No hay UI, overlay, endpoint Master, batch Master, retry automatico, hooks, drivers, SendInput, shell ni persistencia de lock.

Prompt 17C implementa dispatch batch end-to-end de `OPEN_APPLICATION(applicationId)` desde el Master Backend. Expone `POST /api/classrooms/{classroomId}/open-application`, protegido por `MasterAccessGuard`, con request estricta de `applicationId` y `targetDeviceIds`. El Master resuelve una `ApplicationDefinition` activa persistida, exige que este autorizada en el Classroom por `classroom_applications`, congela solo `applicationId`, persiste una unica `BatchOperation` `OPEN_APPLICATION` antes del fanout y usa `MasterRemoteOperationGateway` con `OpenApplicationOperationParameters.applicationId`. No modifica Agent, Session Agent, Protobuf, `ApplicationBinding`, resolucion de App Paths ni `CreateProcessW`.

Prompt 17B implementa `OPEN_APPLICATION(applicationId)` productivo del lado Agent. El Protobuf v1 agrega `OpenApplicationOperationParameters.applicationId`, capability `OPEN_APPLICATION_V1` y errores de aplicacion. El Agent Service hace preflight logico contra `application-bindings.json`, pero no lanza procesos ni envia paths; manda un Session Command `OPEN_APPLICATION` que conserva solo `applicationId`. El Session Agent vuelve a leer y validar el catalogo on-demand, resuelve `ABSOLUTE_EXE` o `APP_PATHS` HKLM-only (Registry64/Registry32, sin HKCU ni PATH) y lanza mediante `CreateProcessW` con ruta absoluta en `lpApplicationName`, `lpCommandLine = null`, sin argumentos, sin elevacion y sin monitoring.

Prompt 17A agrega en `GaltekClassroom.Agent.Service` el catalogo local seguro `application-bindings.json` para resolver en el Client `applicationId -> target local` en una futura operacion `OPEN_APPLICATION`. El catalogo vive en `<CommonApplicationData>\Galtek\Classroom\`, usa escritura durable, ACL local, configuracion CLI elevada y soporta solo `APP_PATHS` y `ABSOLUTE_EXE`. No agrega launch de procesos, Protobuf, Session Command, endpoint Master, batch dispatch, auto-discovery, scans, polling ni rutas ejecutables desde el Master.

Prompt 16F2 agrega dispatch batch desde Master para `OPEN_URL`. Expone `POST /api/classrooms/{classroomId}/open-url`, protegido por `MasterAccessGuard`, con request estricta de `url` y `targetDeviceIds`. El Master valida safety estructural global con `OpenUrlPolicy`, resuelve una policy efectiva por target desde SQLite con `accountType = null` (`ANY` solamente), deriva grupo por assignment actual `Student -> Device`, evalua `BrowserNavigationPolicyEvaluator` incluyendo `EXACT_URL`, congela `OpenUrlOperationParameters.url` antes del fanout, persiste una unica `BatchOperation` `OPEN_URL` y usa el transporte gRPC/mTLS existente. No modifica Agent, Registry, Protobuf ni Session Command, no agrega UI, no selecciona browser/profile y no agrega retry/reconciliacion.

Prompt 16F1 agrega dispatch batch desde Master para aplicar las policies persistidas de navegacion y descarga de navegador. Expone `POST /api/classrooms/{classroomId}/browser-policies/apply` y `POST /api/classrooms/{classroomId}/browser-download-policies/apply`, ambos protegidos por `MasterAccessGuard`, con request estricta de `targetDeviceIds` explicitos. El Master resuelve una policy efectiva por target desde SQLite con `accountType = null` (`ANY` solamente), deriva grupo por assignment actual `Student -> Device`, congela parametros Protobuf tipados antes del fanout, persiste una unica `BatchOperation` y usa el transporte gRPC/mTLS existente. No modifica Agent, Registry, Protobuf ni Session Command, no agrega UI.

Prompt 16E2B implementa enforcement real Agent-side de politicas de descarga de navegador para Google Chrome y Microsoft Edge en Windows usando la policy empresarial nativa `DownloadRestrictions` como `REG_DWORD` bajo `HKEY_USERS\<SID>` del usuario interactivo real. Agrega `ApplyBrowserDownloadPolicyOperationHandler`, store especifico de descarga, state durable `browser-download-policy-state.json`, journal lazy `browser-download-policy-apply.json`, ownership conservador de parent key, hardening ACL parent-only, rollback/recovery y capability productiva `BROWSER_DOWNLOAD_POLICY_V1`. No agrega endpoint batch Master, UI, Session Command, extension, proxy, DNS, firewall, hosts, browser automation, process scan, version polling ni DLP.

Prompt 16E2A prepara el contrato Agent/Protobuf para aplicar politicas de descarga de navegador mediante `APPLY_BROWSER_DOWNLOAD_POLICY`, parametros tipados y enum `BrowserDownloadRestrictionMode`, sin cambiar `protocolVersion`. Agrega `ChromiumDownloadPolicyCompiler` puro en C# para traducir modos Galtek a `DownloadRestrictions` nativo 0-4, con hash determinista que diferencia `NO_SPECIAL_RESTRICTIONS` implicito de policy explicita con valor 0.

Prompt 16E1 agrega en el Master Backend la fuente de verdad persistente para politicas de descarga de navegador, separada de las politicas de navegacion. El dominio `browserpolicy` modela `BrowserDownloadPolicy` con scopes `CLASSROOM`/`GROUP`/`DEVICE`, account scopes `ANY`/`PRIMARY`/`SECONDARY`, restriction modes `NO_SPECIAL_RESTRICTIONS`, `BLOCK_DANGEROUS`, `BLOCK_POTENTIALLY_DANGEROUS`, `BLOCK_ALL` y `BLOCK_MALICIOUS`, resolver determinista, migracion SQLite V4, repositorio Spring JDBC explicito y API administrativa protegida. No aplica nada al Agent, no escribe registry, no modela extensiones/MIME arbitrarios y no implementa descargas autorizadas por maestra.

Prompt 16D implementa enforcement real Agent-side de politicas de navegacion para Google Chrome y Microsoft Edge en Windows usando las policies empresariales `URLBlocklist`/`URLAllowlist` en el hive del usuario interactivo real (`HKEY_USERS\<SID>`). Agrega operacion remota tipada `APPLY_BROWSER_NAVIGATION_POLICY`, parametros Protobuf tipados, capability `BROWSER_NAVIGATION_POLICY_V1`, compilador/evaluator C# de subset Chromium, resolver local de usuario interactivo por token Windows, estado durable `browser-navigation-policy-state.json`, journal lazy `browser-navigation-policy-apply.json` y defensa en profundidad para `OPEN_URL`. No agrega endpoint batch Master, UI, Session Command nuevo, extension, proxy, DNS, firewall, hosts, inspeccion HTTPS, browser automation, polling ni kill/restart de navegador.

Prompt 16C agrega en el Master la fuente de verdad persistente para politicas administrativas de navegacion web. El dominio `browserpolicy` modela policies por aula/grupo/device y por account scope `ANY`/`PRIMARY`/`SECONDARY`, reglas URL sin regex arbitraria, normalizacion/evaluacion pura y resolucion determinista de una sola politica efectiva. No aplica bloqueo real en Chrome/Edge/Windows, no agrega transporte Agent, no modifica Protobuf/gRPC y no implementa politicas de descargas.

Prompt 16B implementa `OPEN_URL` productivo Agent-side usando el canal local seguro `Session Command v1` de Prompt 16A. `OperationRequest OPEN_URL` transporta parametros tipados `OpenUrlOperationParameters.url`; el Agent Service valida la URL y envia un comando `OPEN_URL` tipado al Session Agent de la sesion interactiva. El Session Agent valida nuevamente la URL y pide a Windows abrirla con el handler registrado de HTTP/HTTPS mediante Shell API. El Service corre como LocalSystem/Session 0 y nunca abre directamente el navegador. En esa fase no habia bloqueo real de URLs, bloqueo de descargas, endpoint batch Master para `OPEN_URL`, UI ni `OPEN_APPLICATION`.

Prompt 16A agrega el canal local seguro `Session Command v1` entre `GaltekClassroom.Agent.Service` y `GaltekClassroom.Agent.Session`. Es un Named Pipe separado por sesion interactiva, servido por el Session Agent y consumido por el Service como LocalSystem, con ACL solo para LocalSystem, autenticacion del caller real mediante token de Named Pipe, verificacion del servidor por PID/sesion/ruta productiva y framing JSON UTF-8 con longitud BIG ENDIAN de 4 bytes.

Prompt 15C cierra la primera capacidad remota end-to-end de power control con reconciliacion segura de resultados inciertos. El protocolo Protobuf v1 agrega `OperationStatusQuery`/`OperationStatusReport` sobre el stream `NetworkConnection.Connect` existente, sin cambiar `protocolVersion` ni crear otro servicio. El Agent responde read-only desde el cache acotado del dispatcher o desde un receipt durable minimo de power control aceptado; la consulta nunca ejecuta handlers ni modifica Windows. El Master acepta resultados tardios autenticos y agrega reconciliacion manual/event-driven por reconnect para targets `FAILED + OPERATION_RESULT_UNKNOWN`, sin reenviar `SHUTDOWN`/`RESTART` ni inferir exito por `OFFLINE` o reconnect.

Prompt 15B implementa el primer dispatch remoto batch real desde Master para `SHUTDOWN` y `RESTART`. Agrega `POST /api/classrooms/{classroomId}/power-control`, protegido por `MasterAccessGuard`, con body tipado y lista explicita de `targetDeviceIds`. El Master hace preflight por Device, persiste una unica `BatchOperation`, envia `OperationRequest` por la conexion gRPC/mTLS autenticada solo a targets `READY`, correlaciona resultados por `(deviceId, operationId)` y registra `SUCCESS`, `PARTIAL_SUCCESS` o `FAILED`. Si una request ya enviada queda sin `OperationResult` por timeout o desconexion, el target falla con `OPERATION_RESULT_UNKNOWN` no retryable y Prompt 15C puede reconciliarlo sin reenviar la operacion destructiva.

Prompt 15A implementa las primeras operaciones remotas productivas del Agent: `SHUTDOWN` y `RESTART`. Se ejecutan solo cuando una `OperationRequest` valida llega por el transporte seguro existente y pasa por `RemoteOperationDispatcher`. El Service usa una abstraccion testeable `IWindowsPowerController`; la implementacion productiva habilita `SeShutdownPrivilege` y solicita apagado/reinicio mediante API nativa Windows con countdown fijo de 10 segundos, sin force-close, shell, scripts, WMI ni procesos externos. `SUCCESS` significa que Windows acepto la solicitud, no que el equipo ya este apagado.

Prompt 14.4 convierte perdida de energia, reinicio abrupto y boot storm en condiciones normales de diseno. El Agent separa startup minimo de validaciones pesadas: marca arranque con `agent-service.running`, llega a `MINIMAL_READY` tras Installation Identity y expone por IPC `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive`. La validacion comercial completa queda diferida fuera del camino critico de IPC/red. El Master agrega `master-backend.running`, helpers de escritura atomica/durable y modelos puros de recovery para readiness, politicas de arranque y semantica de operaciones inciertas. La reconexion del Client agrega jitter acotado para evitar thundering herd sin reemplazar el backoff.

Prompt 14.3 convierte performance y bajo consumo en requisitos arquitectonicos medibles. Agrega modelos puros Java para perfiles `LEGACY`/`STANDARD`, `MASTER_BALANCED`, clases de trabajo de recursos, budgets de memoria/concurrencia, diagnostico on-demand y load shedding. Tambien agrega constantes compartidas C# para esos nombres y corrige ruido claro de idle: requests IPC exitosos y conexion IPC pasan a `DEBUG`, los retries repetidos de gRPC bajan a `DEBUG` y el heartbeat del Agent ya no relee `authorized-masters.json` en cada ciclo.

Prompt 14.5D cierra la optimizacion preventiva inicial y fija el principio "no optimizar sin medir". Agrega snapshots runtime ligeros y on-demand para Agent Service, Session Agent y Master Java con APIs estandar, mas una guia manual de medicion real. No agrega telemetria continua, timers, persistencia, dashboard, Protobuf, scheduler general ni tuning JVM/.NET.

Prompt 14.2 fija el modelo operativo real del aula primaria y la arquitectura Master/Client sin implementar operaciones Windows reales. Agrega modelos/enums/planners puros para estrategias de asignacion, preparacion progresiva por Device, estados de workspace canonico/local, prioridad operacional, modos de proyeccion, politica normal de `PRIMARY`/`SECONDARY` y reglas de limpieza segura de working copies. No agrega migraciones ni persistencia nueva.

Prompt 14 registra Clients paired como Devices persistentes del Master sin redisenar pairing ni mTLS. El Master conserva la autoridad sobre `deviceId`, persiste el vinculo vigente en `device_network_bindings`, expone `GET /api/network/clients` y `POST /api/classrooms/{classroomId}/devices/register`, acepta capabilities tipadas reportadas por `ClientHello` y superpone presencia viva en memoria sobre Devices registrados. El framework de operaciones remotas queda tipado en Protobuf y en el Agent, pero ninguna operacion funcional real se ejecuta todavia; toda operacion sin handler devuelve `OPERATION_NOT_IMPLEMENTED`.

Prompt 13 implementa el primer transporte real y seguro Master-Client sobre el trust de Prompt 12. El Client inicia una conexion persistente saliente hacia el Master mediante gRPC/Protobuf v1 sobre TLS/mTLS obligatorio. Los certificados son self-signed de corta vida y se validan por pinning del fingerprint `SubjectPublicKeyInfo` ya persistido por pairing; no existe CA global que autorice instalaciones arbitrarias.

El alcance de red vigente incluye `ClientHello`, estado de conexion, heartbeat, capabilities tipadas, mensajes de framework `OperationRequest`/`OperationAccepted`/`OperationResult`, consulta read-only de status y dispatch batch Master para power control, `OPEN_URL`, `OPEN_APPLICATION`, `LOCK_INPUT`, `UNLOCK_INPUT` y apply de browser policies. El Agent ya ejecuta `SHUTDOWN`, `RESTART`, `OPEN_URL`, `OPEN_APPLICATION`, `LOCK_INPUT`, `UNLOCK_INPUT`, `APPLY_BROWSER_NAVIGATION_POLICY` y `APPLY_BROWSER_DOWNLOAD_POLICY` cuando llegan por ese framework seguro. No hay mDNS ni discovery real.

Prompt 12 implementa pairing criptografico Master-Client sobre las Network Identities ya existentes. El Master tiene una Network Identity propia con metadata publica en `master-network-identity.json` y private key cifrada fuera de SQLite/JSON plano; el Client conserva su `network-identity.json` publico y private key en Windows CNG/KSP de maquina. El pairing usa challenge/response firmado, requiere intencion explicita, persiste trust en ambos lados y permite revocacion.

Prompt 9.6 formaliza el dominio futuro de cuentas Windows administradas en Clients. Cada Client podra tener dos cuentas logicas, `PRIMARY` y `SECONDARY`, y el Master podra planificar una sola accion masiva para dejar PCs en la cuenta objetivo, clasificando `NO_CHANGE`, `LOGON`, `SWITCH`, `PENDING` y bloqueos. Solo se agregan modelos/enums/planners puros y contratos compartidos; no hay passwords, Credential Provider, login/logoff real, IPC write, gRPC, mTLS ni UI.

Prompt 10 agrega la primera API administrativa real del Master Backend sobre SQLite. Los endpoints de aulas, grupos, alumnos, assignments, aplicaciones, operaciones, bootstrap y snapshot pasan por `MasterAccessGuard` antes de tocar datos escolares. La UI React/Tauri futura puede iniciar con `GET /api/master/bootstrap`, elegir aula y cargar `GET /api/classrooms/{id}/snapshot` sin N+1.

Prompt 09 implementa la autoridad real de Master Windows Binding en `GaltekClassroom.Agent.Service`. El binding local se persiste en `master-binding.json`, se liga al `installationId`, se evalua contra `LicenseState` activo con rol `MASTER` y se compara contra el SID real del cliente conectado al Named Pipe mediante impersonation. El Master Backend Java solo consume el resultado derivado por IPC y expone diagnostico.

Prompt 08 agrega persistencia SQLite local del dominio Master mediante Spring JDBC, Flyway programatico y repositories explicitos. El Master Backend ya puede crear, migrar y reabrir una base `classroom.db` con aulas, catalogo de aplicaciones, grupos, alumnos, devices, assignments, workspaces, perfiles de navegador y operaciones batch.

Prompt 07 agrega el modelo funcional completo de Galtek Classroom en el Master Backend: aula, devices, alumnos, grupos, workspaces de alumno, perfiles de navegador, binding local del Master por Windows SID, catalogo de acciones, errores operacionales y planners puros para assignment, move, swap y batch preflight.

Prompt 06 deja `GaltekClassroom.Agent.Service` como Windows Service real y agrega el ciclo de vida productivo de `GaltekClassroom.Agent.Session`. El Service se ejecuta en Session 0 como `LocalSystem`; el Session Agent arranca al logon mediante Windows Task Scheduler, se ejecuta con el token del usuario interactivo, usa privilegio limitado, permanece en background sin UI y se reconecta al Service por Local IPC.

Local IPC API v1 sigue siendo read-only sobre Windows Named Pipes. `GaltekClassroom.Agent.Service` expone estado seguro de dispositivo, Machine Code, autorizacion Master administrativa, autorizacion interna de unlock recovery y diagnostico runtime on-demand sin duplicar Installation Identity ni Commercial License. El Session Agent continua usando `PING` y `GET_DEVICE_STATUS`. Las acciones interactivas futuras no se agregan a Local IPC v1: usan el canal separado `Session Command v1`.

Las capacidades operativas de administracion remota restantes siguen planificadas. Prompt 15B despacha power control (`SHUTDOWN`/`RESTART`) desde el Master hacia el Agent Service; Prompt 16F1 despacha apply batch de policies de navegacion/descarga ya persistidas; Prompt 16F2 despacha `OPEN_URL` batch desde Master usando el handler Agent-side existente; Prompt 18B2 despacha input control batch desde Master usando handlers Agent-side existentes. Todavia no se ejecuta transferencia real, wallpapers, proyeccion, login/logoff Windows, cambio real de usuario, mDNS, UI ni captura.

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
- Performance tuning adicional requiere medicion reproducible en hardware real.

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
- Operacion remota sin ACK o sin resultado confirmado no se considera `SUCCESS`; debe conservarse como incertidumbre y solo reconciliarse cuando exista un mecanismo seguro y explicitamente soportado para ese tipo de operacion.
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
  - `POST /api/classrooms/{classroomId}/power-control`.
  - `POST /api/classrooms/{classroomId}/open-url`.
  - `POST /api/classrooms/{classroomId}/open-application`.
  - `GET /api/classrooms/{classroomId}/browser-policies`.
  - `POST /api/classrooms/{classroomId}/browser-policies`.
  - `POST /api/classrooms/{classroomId}/browser-policies/apply`.
  - `PATCH /api/browser-policies/{policyId}`.
  - `POST /api/browser-policies/{policyId}/archive`.
  - `GET /api/browser-policies/{policyId}/rules`.
  - `POST /api/browser-policies/{policyId}/rules`.
  - `PATCH /api/browser-url-rules/{ruleId}`.
  - `POST /api/browser-url-rules/{ruleId}/archive`.
  - `GET /api/classrooms/{classroomId}/browser-policies/effective`.
  - `GET /api/classrooms/{classroomId}/browser-download-policies`.
  - `POST /api/classrooms/{classroomId}/browser-download-policies`.
  - `PATCH /api/browser-download-policies/{policyId}`.
  - `POST /api/browser-download-policies/{policyId}/archive`.
  - `GET /api/classrooms/{classroomId}/browser-download-policies/effective`.
  - `POST /api/classrooms/{classroomId}/browser-download-policies/apply`.
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
  - `browserpolicy`: politicas administrativas de navegacion, normalizacion URL, reglas `ALLOW`/`BLOCK`, resolver de precedencia y evaluador puro; tambien politicas administrativas de descarga de navegador con restriction modes y resolver separado.
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
  - `RuntimeDiagnosticsSnapshot` Java para heap, non-heap, threads y uptime via MXBeans, calculado solo bajo solicitud.
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
- `POST /api/classrooms/{classroomId}/power-control` registra una `BatchOperation` `SHUTDOWN` o `RESTART` antes de enviar requests y actualiza targets al terminar el fanout.
- `OpenUrlPolicy` que permite `http`/`https` y rechaza esquemas inseguros como `file`, `javascript` y `data`.
- `BrowserUrlNormalizer` normaliza solo URLs absolutas seguras `http`/`https`, exige host, quita fragment, rechaza userinfo/control chars, baja host a minusculas, elimina trailing dot y normaliza puertos default.
- `BrowserNavigationPolicyEvaluator` aplica primero safety estructural y despues politica administrativa. Dentro de una policy gana el filtro mas especifico por host, scheme/port, path y query; solo ante igual especificidad `ALLOW` gana a `BLOCK`. `BLOCKLIST` permite por default y `ALLOWLIST` bloquea por default.
- `BrowserPolicyPrecedenceResolver` selecciona como maximo una policy efectiva: `DEVICE` cuenta especifica, `DEVICE ANY`, `GROUP` cuenta especifica, `GROUP ANY`, `CLASSROOM` cuenta especifica, `CLASSROOM ANY`, o `UNRESTRICTED` implicito.
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
- Migracion `V3__add_browser_navigation_policies.sql` con `browser_access_policies`, `browser_url_rules`, checks de enum/scope e indices unicos parciales para una policy activa por target logico y `account_scope`.
- Configuracion local `galtek.classroom.master.storage.*`.
- Resolucion de datos del Master a `<CommonApplicationData>\Galtek\Classroom\Master\` con override `GALTEK_CLASSROOM_MASTER_DATA_DIR`.
- Base local `classroom.db` ignorada por Git, junto con archivos WAL/SHM.
- `PRAGMA foreign_keys=ON`, WAL, `synchronous=NORMAL` y `busy_timeout` configurado.
- Pool Hikari pequeno para SQLite local.
- `MasterDatabaseInitializer` con `PRAGMA quick_check` antes/despues de migrar cuando corresponde.
- `MasterRunMarker` crea `master-backend.running` al arrancar y lo elimina en cierre limpio para detectar apagado no limpio sin tocar SQLite/WAL/SHM.
- `AtomicFiles` centraliza escrituras atomicas/durables de archivos criticos del Master con temp file, flush/fsync y move atomico.
- Estado de almacenamiento `MasterStorageState` con `READY`, `UNAVAILABLE`, `CORRUPT` y `MIGRATION_FAILED`.
- Repositories explicitos para `Classroom`, `ApplicationDefinition`, `SchoolGroup`, `Student`, `Device`, `DeviceNetworkBinding`, `DeviceAssignment`, `StudentWorkspace`, `BrowserProfile`, `MasterBrowserProfile`, `BrowserPolicy` y `BatchOperation`.
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
- `ClientHello` reporta capabilities tipadas productivas conocidas: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`, `POWER_CONTROL_V1`, `OPEN_URL_V1` y `BROWSER_NAVIGATION_POLICY_V1`.
- `BROWSER_DOWNLOAD_POLICY_V1` se anuncia desde 16E2B porque el Agent registra enforcement real de `DownloadRestrictions`.
- Las capabilities son informacion operativa y no autorizacion.
- `NetworkClientAdminService` lista Clients known/paired con estado seguro y registra Devices solo tras verificar trust `PAIRED`, no `REVOKED` y ausencia de doble registro.
- `GET /api/classrooms/{id}/snapshot` superpone presencia viva para Devices registrados sin escribir SQLite en cada heartbeat.
- `MasterNetworkHeartbeatMonitor` marca `OFFLINE` tras timeout de heartbeat configurado.
- Certificado TLS del Master emitido en memoria desde su Network Identity, ligado al fingerprint ya persistido por pairing.
- Pruebas Java para conexion PAIRED, rechazo no paired, rechazo REVOKED, mismatch de certificado/fingerprint, peer desconocido, heartbeat, timeout offline, reconnect, multiples Clients concurrentes, capabilities incluyendo `POWER_CONTROL_V1`, registro de Devices, no writes persistentes por heartbeat y trust/binding persistente tras reinicio.
- `MasterRemoteOperationGateway` mantiene sesiones gRPC autenticadas y pending operations en memoria por `(deviceId, operationId)`, envia `OperationRequest`, correlaciona `OperationAccepted`/`OperationResult` y limpia pending state en success, fallo, timeout o desconexion.
- `MasterRemoteOperationGateway` tambien envia `OperationStatusQuery` read-only por `(deviceId, operationId)` y limpia pending status queries en `KNOWN`, `UNKNOWN`, timeout o desconexion.
- `PowerOperationReconciliationService` acepta `OperationResult` tardios autenticos y consulta status de targets `OPERATION_RESULT_UNKNOWN` manualmente o tras reconnect del mismo Device, sin crear `BatchOperation` nuevo.
- `MasterPowerOperationStartupRecovery` ejecuta una vez al arrancar y transforma targets power `PENDING` huerfanos a `FAILED + OPERATION_RESULT_UNKNOWN` sin reenviar operaciones ni esperar Clients online.
- `POST /api/operations/{id}/reconcile` permite comprobacion administrativa protegida de `SHUTDOWN`/`RESTART` inciertos y devuelve el `OperationResponse` actualizado.
- `PowerControlDispatchService` hace preflight por target: aula correcta, Device registrado, binding vigente, trust `PAIRED`, no `REVOKED`, conexion autenticada `ONLINE` y capability `POWER_CONTROL_V1`.
- `OperationAccepted` solo confirma reconocimiento del Agent; no marca exito. Solo `OperationResult SUCCESS` produce target `SUCCESS`.
- Timeout o desconexion despues del envio produce `OPERATION_RESULT_UNKNOWN` no retryable y no se convierte en `DEVICE_OFFLINE`; si luego hay `OperationResult` o `OperationStatusReport KNOWN`, el mismo batch se reconcilia.
- Pruebas Java para endpoint power-control, preflight parcial, correlacion gRPC por target, mapeo de resultados y limpieza de pending operations.

PLANIFICADO:

- React + Tauri para UI de escritorio, sin Vite.
- Exponer pairing mediante flujos reales sobre el transporte seguro existente.
- Visualizacion de equipos, miniaturas y estado.
- UI batch-first para grupos, alumnos y equipos con partial success y retry de fallidos.
- Consulta y cambio masivo de sesion Windows administrada por `accountId` logico.
- Integracion real de workspaces, navegador, transferencia, wallpaper, proyeccion y auditoria.
- Auditoria administrativa.
- UI/API protegida futura para Credential Vault, incluyendo `REVEAL CREDENTIAL` explicito una credencial a la vez.

NO IMPLEMENTADO:

- UI.
- Autenticacion.
- Descubrimiento.
- mDNS real.
- Commercial License en Java.
- Llaves publicas o JWT dentro del Master Backend.
- Ejecucion real de `DISTRIBUTE_FILE`, `CREATE_FOLDER`, `SET_WALLPAPER`, `MOVE_STUDENT` o `SWAP_STUDENTS`.
- Ejecucion real de `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` o `SWITCH_MANAGED_ACCOUNT`.
- Almacenamiento de passwords o credenciales Windows administradas en `classroom.db`.
- Login/logoff Windows real, Credential Provider, filesystem/sync real, USB real, automatizacion Chrome, captura, proyeccion, UI, reconciliacion productiva de workflows de datos futuros, performance tuning y mDNS.

## Almacenamiento local del Master

IMPLEMENTADO:

- SQLite local en archivo `classroom.db`.
- Ruta productiva por defecto: `<CommonApplicationData>\Galtek\Classroom\Master\classroom.db`.
- Override de desarrollo/tests: `GALTEK_CLASSROOM_MASTER_DATA_DIR` o `galtek.classroom.master.storage.data-dir`.
- Credential Vault local cifrado en archivo separado `credential-vault.dat`, dentro del mismo Master data directory.
- `credential-vault.dat` no usa SQLite y contiene solo header tecnico minimo en plaintext: schema/crypto version, KDF, salt, iterations, nonces y ciphertext/tag.
- El documento logico cifrado contiene `entries[]` completas: `credentialId`, `credentialType`, `displayName`, `loginIdentifier`, `password`, `createdAtUtc` y `updatedAtUtc`.
- Tipos iniciales de credencial de vault: `WINDOWS_ACCOUNT` y `GOOGLE_ACCOUNT`.
- Escritura de vault mediante temp file en el mismo directorio, flush/fsync, move/replace atomico y hardening ACL best-effort encapsulado.
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
- UI/API HTTP de Credential Vault, clipboard, export masivo, reset destructivo de vault y provisioning remoto de credenciales.

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
- `ClientCapabilityProvider` anuncia `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`, `POWER_CONTROL_V1`, `OPEN_URL_V1`, `OPEN_APPLICATION_V1`, `BROWSER_NAVIGATION_POLICY_V1`, `BROWSER_DOWNLOAD_POLICY_V1` e `INPUT_CONTROL_V1`.
- Heartbeat periodico default cada 15 segundos y reconexion con backoff `2s, 5s, 10s, 30s`.
- Reconexion con jitter acotado: jitter inicial default hasta 2 segundos y jitter por retry default hasta 1 segundo, sin quitar el backoff base.
- El heartbeat del Agent conserva el stream TLS/mTLS persistente y ya no relee `authorized-masters.json` en cada ciclo; los `OperationRequest` revalidan trust antes de cualquier accion.
- Los retries repetidos de conexion gRPC se registran en `DEBUG` tras el primer warning para evitar spam de retry.
- `MasterConnectionStateTracker` mantiene estado local `CONNECTING`, `ONLINE` y `OFFLINE` derivado del stream autenticado.
- `RemoteOperationDispatcher` del Agent deduplica por `operationId`, aplica timeout y devuelve `OperationResult` estructurado; `SHUTDOWN`, `RESTART`, `OPEN_URL`, `OPEN_APPLICATION`, `LOCK_INPUT`, `UNLOCK_INPUT`, `APPLY_BROWSER_NAVIGATION_POLICY` y `APPLY_BROWSER_DOWNLOAD_POLICY` tienen handlers productivos y cualquier operacion sin handler sigue devolviendo `OPERATION_NOT_IMPLEMENTED`.
- `RemoteOperationDispatcher.TryGetCompletedResult` permite consultar read-only el resultado completado retenido por dedupe/cache, sin ejecutar handlers ni renovar retencion.
- `PowerOperationReceiptStore` persiste en `power-operation-receipts.json` receipts acotados solo para `SHUTDOWN`/`RESTART` aceptados por Windows: `operationId`, `operationType`, `targetDeviceId`, `acceptedAtUtc` y `SUCCESS`.
- `OperationStatusQuery` en el Agent valida el stream Master/trust vigente y responde `KNOWN` desde cache/receipt o `UNKNOWN`; nunca crea un `OperationRequest`, nunca llama handlers y nunca modifica Windows.
- `ShutdownOperationHandler` y `RestartOperationHandler` son handlers tipados explicitos y usan `IWindowsPowerController`; no existe handler generico de comandos.
- `WindowsPowerController` usa `InitiateSystemShutdownExW` como API nativa Windows, habilita `SeShutdownPrivilege` mediante `OpenProcessToken`, `LookupPrivilegeValue` y `AdjustTokenPrivileges`, y no ejecuta `shutdown.exe`, `cmd.exe`, PowerShell, scripts, WMI shell ni procesos externos.
- Power control usa countdown fijo de 10 segundos, mensaje constante, `forceAppsClosed=false` y no acepta payload arbitrario, `force=true`, timeout arbitrario ni mensajes enviados por Master.
- `OperationResult SUCCESS` en power control significa que Windows acepto la solicitud; si Windows no la acepta se devuelve `FAILED` con `POWER_CONTROL_UNAVAILABLE` o `POWER_CONTROL_FAILED`.
- Si Windows acepta `SHUTDOWN`/`RESTART` pero falla la persistencia del receipt, el Agent conserva el `OperationResult SUCCESS` normal y registra warning seguro; la falta de receipt solo limita reconciliacion futura tras reboot.
- `RemoteOperationDispatcher` rechaza ejecucion de handlers cuando Commercial License todavia no esta activa, preservando el bloqueo comercial aunque la validacion completa se difiera fuera del startup critico. La unica excepcion vigente es `UNLOCK_INPUT`, porque reduce control y debe funcionar como recovery; esta excepcion no omite mTLS, trust, Device authorization ni el canal Session Command autenticado.
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
- Operaciones IPC permitidas: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`, `GET_MASTER_UNLOCK_AUTHORIZATION`, `GET_RUNTIME_DIAGNOSTICS`.
- Local IPC v1 conserva estrictamente sus operaciones read-only; no se agregan comandos write ni acciones interactivas alli.
- `GET_DEVICE_STATUS` expone solo estado seguro y no expone JWT ni hashes de hardware.
- `GET_DEVICE_STATUS` expone `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive` para diagnostico local de recovery.
- `GET_MACHINE_CODE` reutiliza la implementacion existente de Machine Code y no exige licencia activa.
- `GET_MASTER_AUTHORIZATION` deriva el SID real del cliente Named Pipe y no acepta SID en el payload.
- `GET_MASTER_UNLOCK_AUTHORIZATION` deriva el SID real del cliente Named Pipe y autoriza solo una futura recuperacion `UNLOCK_INPUT` si Installation Identity, binding e `installationId` coinciden; no exige Commercial License activa, no usa claims de licencia invalida y no expone SID, licencia, roles, installationId completo, rutas ni usernames.
- `GET_RUNTIME_DIAGNOSTICS` devuelve snapshot on-demand del Agent Service con memoria aproximada, CPU acumulado, threads, uptime y GC managed memory.
- Requests IPC exitosos y conexion IPC saludable se registran en `DEBUG`, no en `INFO`, para evitar logs periodicos durante idle.
- ACL actual del pipe: `LocalSystem` y `BuiltinAdministrators` con `FullControl`; `Authenticated Users` con `ReadWrite | Synchronize`.
- Cliente `SessionCommandClient` para canal Service -> Session separado, on-demand y no persistente.
- Resolucion productiva de sesion interactiva mediante `WTSGetActiveConsoleSessionId`, sin `quser.exe`, `query session`, shell, PowerShell, WMI shell ni procesos externos.
- El Service nunca intenta enviar comandos a Session 0.
- Antes de enviar un comando de sesion, el Service valida el servidor Named Pipe con `GetNamedPipeServerProcessId`, existencia de proceso, `Process.SessionId` esperado y ruta productiva normalizada `<ProgramFiles>\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe`.
- Timeouts iniciales del canal de sesion: 2 segundos para conectar y request/response; no son configurables desde Master.

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
- Comandos remotos funcionales distintos de `SHUTDOWN`, `RESTART`, `OPEN_URL` y apply de browser policies en Master.
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
- Servidor `Session Command v1` por sesion en `GaltekClassroom.Agent.SessionCommand.v1.<sessionId>`, derivado del `Process.SessionId` real.
- ACL del pipe de comandos restringida a LocalSystem (`S-1-5-18`) como cliente; no concede `Users`, `Authenticated Users` ni `Everyone`.
- Autenticacion del caller real del Named Pipe mediante impersonation y rechazo de cualquier SID distinto de LocalSystem.
- `CHANNEL_PING` como prueba de canal; devuelve `SUCCESS` sin ejecutar acciones externas ni generar UI.
- `OPEN_URL` como comando tipado de sesion; valida solo URL absoluta `http://`/`https://`, rechaza userinfo, controles, CR/LF, rutas locales/UNC y esquemas peligrosos, y llama a Windows Shell API con verbo `open`, URL validada y sin parametros.
- `OPEN_URL SUCCESS` significa solo que Windows acepto la solicitud de abrir la URL con el handler registrado; no comprueba Internet, DNS, HTTP status ni carga de pagina.
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
- Capabilities tipadas productivas vigentes: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`, `POWER_CONTROL_V1`, `OPEN_URL_V1`, `OPEN_APPLICATION_V1`, `BROWSER_NAVIGATION_POLICY_V1` y `BROWSER_DOWNLOAD_POLICY_V1`.
- Capabilities desconocidas se ignoran y no otorgan permisos.
- `device_network_bindings` vincula un Client paired con un Device persistente generado por el Master; SQLite no reemplaza `paired-clients.json`.
- Clients `PAIRED + ONLINE` sin Device se exponen como `AVAILABLE_FOR_REGISTRATION`.
- Heartbeat periodico del Client con `HeartbeatAck` del Master.
- Heartbeat pequeno, sin polling HTTP, sin telemetria pesada, sin logs sanos y sin writes persistentes por ciclo.
- Estado real de conexion `CONNECTING`, `ONLINE` y `OFFLINE` derivado de streams autenticados.
- Framework Protobuf compatible para `OperationRequest`, `OperationAccepted`, `OperationResult`, `OperationStatusQuery` y `OperationStatusReport`, con `operationId`, `targetDeviceId`, `protocolVersion` y resultados tipados.
- El Agent deduplica `OperationRequest` por `operationId`; `SHUTDOWN`, `RESTART`, `OPEN_URL`, `OPEN_APPLICATION`, `APPLY_BROWSER_NAVIGATION_POLICY` y `APPLY_BROWSER_DOWNLOAD_POLICY` usan handlers productivos reales, y cualquier operacion no implementada devuelve `OPERATION_NOT_IMPLEMENTED`.
- El Master despacha `SHUTDOWN`/`RESTART`, `OPEN_URL`, `OPEN_APPLICATION` y apply de browser policies batch con el mismo `operationId` para todos los Devices objetivo y correlaciona por `(deviceId, operationId)`.
- `OperationAccepted` no equivale a exito; `OperationResult SUCCESS` es la unica confirmacion exitosa del target.
- El resultado incierto posterior al envio se registra como `OPERATION_RESULT_UNKNOWN`, `FAILED`, no retryable; reconciliacion pregunta por el resultado original y conserva `UNKNOWN` si no hay evidencia.
- Timeout de heartbeat default 45 segundos en el Master.
- Reconexión del Client con backoff acotado.

PLANIFICADO:

- El descubrimiento usara mDNS/DNS-SD en una fase posterior.
- Exponer pairing/discovery mediante flujos reales de red sin confundir discovery con trust, en una fase posterior.
- Handlers reales restantes de comandos administrativos remotos tipados sobre el framework de operaciones, en una fase posterior.

NO IMPLEMENTADO:

- Descubrimiento real.
- APIs reales de discovery/pairing sobre red.
- Endpoint batch Master para comandos remotos distintos de `SHUTDOWN` y `RESTART`.

Nota de seguridad: descubrir un equipo no significa confiar en el. Network Identity tampoco equivale a trust; el trust aparece solo tras pairing explicito y puede revocarse.

## IPC local

IMPLEMENTADO:

- Windows Named Pipe `GaltekClassroom.Agent.v1`.
- `GaltekClassroom.Agent.Service` es el servidor IPC.
- `GaltekClassroom.Agent.Session` consume `PING` y `GET_DEVICE_STATUS` en CLI one-shot y en supervisor background; tambien puede consultar `GET_RUNTIME_DIAGNOSTICS` bajo solicitud explicita.
- Master Backend Java consume `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION` y `GET_MASTER_UNLOCK_AUTHORIZATION`.
- Protocolo documentado en `protocol/local-ipc-v1.md`.
- `protocolVersion = 1`.
- Mensajes JSON UTF-8 con prefijo de longitud de 4 bytes BIG ENDIAN.
- Limite de payload de 64 KiB.
- Operaciones permitidas en v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`, `GET_MASTER_UNLOCK_AUTHORIZATION`, `GET_RUNTIME_DIAGNOSTICS`.
- IPC v1 es read-only.
- El SID de autorizacion Master se deriva del token real del cliente conectado al Named Pipe mediante impersonation; no viene del payload.
- Version desconocida devuelve `IPC_PROTOCOL_UNSUPPORTED`.
- Operacion desconocida devuelve `IPC_OPERATION_NOT_SUPPORTED`.
- JSON malformado, longitud invalida y desconexiones de clientes se manejan sin detener el Service.
- Canal separado `Session Command v1` para Service -> Session, documentado en `protocol/local-session-command-v1.md`.
- Pipe por sesion `GaltekClassroom.Agent.SessionCommand.v1.<sessionId>`, servido por el Session Agent de esa sesion.
- Framing Session Command v1: longitud BIG ENDIAN de 4 bytes mas JSON UTF-8, con limite de 16 KiB.
- ACL de Session Command v1 solo para LocalSystem como cliente y autenticacion bilateral: Session Agent valida SID real del caller, Service valida PID/sesion/ruta del servidor.
- Operaciones Session Command v1 implementadas: `CHANNEL_PING` y `OPEN_URL`.

NO IMPLEMENTADO:

- Activacion de licencia por IPC.
- Operaciones write por IPC.
- Bloqueo de URLs, bloqueo de descargas, lanzamiento de aplicaciones u otras acciones interactivas sobre Session Command v1.

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
- `GET_MASTER_UNLOCK_AUTHORIZATION` expone al Master Backend un estado derivado minimo (`status`, `authorized`, `configured`) para recovery-safe unlock; `MasterAuthorizationService.EvaluateUnlock` omite solo el gate de Commercial License/rol y conserva Installation Identity, binding valido, installationId coincidente y SID real del caller.
- `MasterUnlockAccessGuard` en Java es interno y de uso exclusivo para acciones declaradas recovery-safe que reducen control. Actualmente la unica accion prevista es `UNLOCK_INPUT`; no reemplaza `MasterAccessGuard` ni agrega fallback entre guards.
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
- Archivo `application-bindings.json` para vincular `applicationId` logico Galtek con un launch target local configurado por administrador.
- Archivo `managed-windows-accounts.json` para vincular los slots logicos `PRIMARY`/`SECONDARY` con cuentas Windows reales mediante SID.
- Archivo `managed-windows-credentials.dat` para almacenar solo ciphertext DPAPI de passwords Windows administradas `PRIMARY`/`SECONDARY`.
- `application-bindings.json` queda separado de identidad, licencia, Master binding, Network Identity, trust stores y state/journals de browser policy.
- `managed-windows-accounts.json` queda separado de `installation.json`, `license.dat`, `master-binding.json`, `network-identity.json`, `authorized-masters.json`, `application-bindings.json`, browser policy state/journals y `credential-vault.dat`.
- `managed-windows-credentials.dat` queda separado de `managed-windows-accounts.json`, `installation.json`, `license.dat`, `master-binding.json`, `network-identity.json`, `authorized-masters.json`, `application-bindings.json`, browser policy state/journals y `credential-vault.dat`.
- El catalogo local de aplicaciones usa `DurableFileWriter`, temp file en el mismo directorio, flush/fsync, replace/move atomico y verificacion posterior.
- El catalogo de cuentas Windows administradas usa `DurableFileWriter`, temp file en el mismo directorio, flush/fsync, replace/move atomico y verificacion posterior.
- El credential store del Client usa `DurableFileWriter` y escribe solo el envelope cifrado; nunca escribe plaintext en temp, journal, backup ni log.
- ACL de `application-bindings.json`: `LocalSystem` y `Builtin Administrators` con `FullControl`; `Builtin Users` y `Authenticated Users` solo lectura para compatibilidad read-only futura.
- ACL de `managed-windows-accounts.json`: `LocalSystem` y `Builtin Administrators` con `FullControl`; usuarios normales no reciben read/write.
- ACL de `managed-windows-credentials.dat`: `LocalSystem` y `Builtin Administrators` con `FullControl`; usuarios normales y `Authenticated Users` no reciben read/write explicito.
- CLI local administrativa: `--application-bind-list`, `--application-bind-exe`, `--application-bind-app-path`, `--application-bind-disable`, `--application-bind-enable`, `--application-bind-remove` y `--replace-application-binding`.
- CLI local administrativa de managed accounts: `--managed-account-list`, `--managed-account-bind <PRIMARY|SECONDARY> <WINDOWS_ACCOUNT>`, `--managed-account-remove <PRIMARY|SECONDARY>` y `--replace-managed-account-binding`.
- Las mutaciones del catalogo requieren consola elevada; list/read-only no requiere elevacion y no crea ni modifica el archivo.
- Las mutaciones de managed accounts requieren consola elevada; list/read-only no muta y respeta el ACL vigente.
- Corrupcion, schema desconocido, duplicados, launch type desconocido o campos incompatibles producen `APPLICATION_BINDINGS_INVALID` sin regeneracion silenciosa ni adopcion parcial.
- Corrupcion, schema desconocido, `installationId` ausente/mismatch, slots desconocidos/duplicados, SID invalido, SID compartido o campos de secreto producen `MANAGED_ACCOUNT_BINDINGS_INVALID` sin regeneracion silenciosa ni adopcion parcial.
- `OPEN_APPLICATION` lee el catalogo on-demand desde el Session Agent y conserva `applicationId` como unico input remoto/inter-proceso.
- `APP_PATHS` se resuelve solo por HKLM App Paths, valor default, vistas Registry64 y Registry32 cuando corresponde; no hay HKCU fallback, PATH search, Program Files scan, Start Menu scan ni discovery.
- `ABSOLUTE_EXE` se vuelve a validar y se comprueba con `File.Exists` justo antes de launch; si desaparecio devuelve `APPLICATION_EXECUTABLE_NOT_FOUND`.
- Llave privada de Network Identity fuera de JSON, en Windows CNG/KSP de maquina.
- Escritura de identidad y licencia con archivo temporal y reemplazo/movimiento para evitar archivos parciales.
- `DurableFileWriter` centraliza escritura de archivos criticos del Agent con temp file, flush/fsync y reemplazo/movimiento atomico.
- Escritura del Master binding con archivo temporal, flush y reemplazo/movimiento atomico.
- Escritura de `network-identity.json` mediante archivo temporal, flush y move atomico sin sobrescritura automatica.
- `license.dat` guarda solo el JWT recibido.
- `master-binding.json` no guarda password, hashes de password, tokens, credenciales ni JWT.
- `managed-windows-accounts.json` no guarda password, hashes de password, credentialId, tokens, credenciales, sessionId, profile path ni membership/admin flag.
- `managed-windows-credentials.dat` no guarda SID, accountReference, username, domain, password, hash, credentialId Master ni vault entry id fuera del ciphertext.
- Las passwords Windows administradas del Client se protegen con DPAPI user scope bajo LocalSystem, optional entropy por instalacion/slot y SID dentro del payload protegido.
- Rebind de un slot a otro SID invalida logicamente la credencial anterior sin borrarla automaticamente.
- Acquire de credenciales devuelve un lease disposable con buffer mutable y no un string.
- `network-identity.json` no guarda private key, secretos ni licencia comercial.
- `authorized-masters.json` no guarda private keys, passwords, JWT ni secretos; guarda public keys/fingerprints y estados de trust.
- Los scripts de instalacion separan binarios en `<ProgramFiles>\Galtek\Classroom\Agent\` y datos persistentes en `<CommonApplicationData>\Galtek\Classroom\`.
- Actualizar o desinstalar normalmente no borra `installation.json`, `license.dat`, `master-binding.json`, `network-identity.json`, `authorized-masters.json` ni la llave CNG de Network Identity.

NO IMPLEMENTADO:

- Base de datos local.
- Logs persistentes en disco.
- DPAPI/ACL hardening avanzado para `license.dat`.
- Almacenamiento seguro futuro de credenciales `PRIMARY`/`SECONDARY`.
- Integracion de `managed-windows-accounts.json` y `managed-windows-credentials.dat` con browser policies, Local IPC, Protobuf, login/logoff/switch o Credential Vault.

Nota de seguridad: en esta fase `license.dat` no depende de confidencialidad para integridad. El JWT esta firmado, ligado a `installationId` y ligado al hardware por regla 3 de 4. El cifrado o endurecimiento local queda para una fase posterior.

## Limites de seguridad

VIGENTE DESDE AHORA:

- No permitir ejecucion remota arbitraria.
- No aceptar `cmd.exe /c`, PowerShell arbitrario, shell remota ni rutas arbitrarias enviadas por un Master.
- Usar operaciones remotas explicitas y estructuradas, por ejemplo `LOCK_INPUT`, `UNLOCK_INPUT`, `OPEN_APPLICATION` con `appId`, `SHUTDOWN`, `RESTART`, `START_PROJECTION`, `STOP_PROJECTION`.
- `SHUTDOWN` y `RESTART` ya implementados en el Agent deben seguir pasando por gRPC/mTLS, trust `PAIRED`, no `REVOKED`, Commercial License activa y `RemoteOperationDispatcher`; no existe una via paralela de ejecucion.
- Power control debe usar API nativa Windows y no `shutdown.exe`, `cmd.exe`, PowerShell, WMI shell, scripts, `Process.Start`, `SendKeys` ni elevacion de procesos.
- Power control no fuerza cierre de aplicaciones en esta version y `SUCCESS` significa que Windows acepto la solicitud, no que el apagado/reinicio ya concluyo.
- Las operaciones futuras de cuentas Windows administradas deben usar `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`.
- `GET_WINDOWS_SESSION_STATE` ya existe Agent-side como snapshot remoto read-only y on-demand de la consola fisica; no se consulta en heartbeat ni startup.
- `GET_WINDOWS_SESSION_STATE` usa SID del token real de la consola fisica para clasificar contra `managed-windows-accounts.json`; nunca mapea por username ni `accountReference`.
- `WTSGetActiveConsoleSessionId()` es la autoridad inicial. `0xFFFFFFFF` y Session 0 se tratan como `UNKNOWN`.
- `WTSUserName` puede usarse solo como senal auxiliar de presencia de login: vacio significa `NO_SESSION`; nunca se envia, persiste ni compara contra bindings.
- `WTSQueryUserToken` se usa desde LocalSystem con `SeTcbPrivilege` habilitado de forma acotada, solo para leer `TokenUser`; el token se cierra siempre.
- Sesiones locked siguen clasificandose por el usuario logueado; sesiones RDP o disconnected historicas no sustituyen automaticamente la consola fisica.
- El resultado remoto de `GET_WINDOWS_SESSION_STATE` contiene solo `WindowsSessionStateResult.state`, sin SID, username, domain, accountReference ni sessionId.
- Los comandos futuros para cuentas administradas solo enviaran `accountId` logico (`PRIMARY`/`SECONDARY`), nunca passwords.
- El Client credential store para `PRIMARY`/`SECONDARY` no ofrece reveal, export, dump, CLI password ni Local IPC; la consulta humana de passwords pertenece al Credential Vault del Master.
- El credential store del Client requiere LocalSystem para protect/unprotect y no usa `CRYPTPROTECT_LOCAL_MACHINE` ni fallback si DPAPI falla.
- El Master no almacenara passwords de cuentas Windows administradas en `classroom.db` ni los enviara en comandos normales.
- La UI no recibe passwords por defecto. La unica excepcion futura es una operacion explicita `REVEAL CREDENTIAL` despues de `MasterAccessGuard.requireAuthorized()`, vault unlock valido y sesion de boveda no expirada; solo se entrega el secreto de la credencial solicitada.
- Las passwords Windows y Google escolares solo pueden persistirse dentro de `credential-vault.dat` cifrado; nunca en SQLite, logs, BatchOperation, heartbeat, ClientHello, OperationRequest normal, BrowserProfile, Cookies, Login Data, Local State ni StudentWorkspace metadata.
- Logs no deben mostrar master password, credential password, DEK, KEK, session token, plaintext vault ni ciphertext completo innecesario.
- `MasterUnlockAccessGuard` jamas autoriza acceso a Credential Vault; la excepcion recovery-safe de `UNLOCK_INPUT` no aplica a passwords.
- La credencial real futura pertenecera al Agent Service del Client y debera protegerse con mecanismos seguros de Windows.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para iniciar o cambiar sesion Windows.
- El mecanismo productivo de login/cambio de usuario debe disenarse posteriormente con integracion soportada por Windows, contemplando Credential Provider.
- Mantener siempre una via estandar de acceso/recovery de Windows fuera de Galtek.
- Las aplicaciones abribles remotamente deben pertenecer a un catalogo configurado previamente.
- Para aplicaciones, el Master solo puede enviar `applicationId`; la resolucion fisica vive en el Client y nunca acepta `executablePath`, comandos, argumentos, working directory, shell, PowerShell, `cmd`, scripts, shortcuts, MSI ni URI arbitraria desde el Master.
- Los launch types locales iniciales son exactamente `APP_PATHS` y `ABSOLUTE_EXE`; `ABSOLUTE_EXE` exige ruta local absoluta `.exe` y existencia del archivo al crear/reemplazar el binding.
- `OPEN_APPLICATION` productivo se ejecuta exclusivamente por gRPC/mTLS -> Agent Service -> Session Command autenticado -> Session Agent; el Service en Session 0 nunca llama `CreateProcess`.
- `OPEN_APPLICATION` usa `CreateProcessW` en el Session Agent con `lpApplicationName` absoluto y `lpCommandLine = null`; no usa ShellExecute, `runas`, UAC intencional, argumentos ni monitoreo de proceso.
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
- `OPEN_URL`/`OPEN_WEB_CONTENT` deben preferir abrir contenido web localmente en el Client; Prompt 16B usa el navegador predeterminado registrado de la sesion interactiva y no convierte YouTube en captura 30 FPS hacia 26 PCs.
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
- Existe transporte gRPC/mTLS para conexion, identificacion, heartbeat y operaciones remotas tipadas; todavia no existe mDNS, discovery real ni comandos remotos distintos de `SHUTDOWN` y `RESTART`.
- El transporte seguro usa el trust ya establecido por pairing y no redisena pairing como discovery.
- Las operaciones futuras de recuperacion, como `UNLOCK_INPUT` y `STOP_PROJECTION`, no deben bloquearse por expiracion para evitar dejar equipos atrapados.
