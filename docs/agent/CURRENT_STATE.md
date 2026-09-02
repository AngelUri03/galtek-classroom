# Estado actual

## Ultima actualizacion

2026-09-01 - Prompt 16B.

## Estado del proyecto

Prompt 12 implementa pairing criptografico Master-Client sobre Network Identity. El Client conserva su Network Identity en `GaltekClassroom.Agent.Service`; el Master Backend agrega una Network Identity propia, private key cifrada fuera de SQLite/JSON plano y trust store local. El pairing requiere intencion explicita, usa challenge/response firmado, expira challenges, bloquea replay, persiste trust en ambos lados y permite revocacion.

Prompt 14 implementa registro real de Devices sobre Clients paired, capabilities tipadas y framework de operaciones. El Master genera `deviceId`, persiste el vinculo Device -> Network Identity en `device_network_bindings`, expone `GET /api/network/clients` y `POST /api/classrooms/{classroomId}/devices/register`, y mantiene presencia viva principalmente en memoria.

Prompt 14.2 formaliza el modelo operativo real del aula primaria y la arquitectura Master/Client sin implementar operaciones Windows reales. El Master conserva workspaces canonicos y orquestacion; los Clients ejecutan aplicaciones localmente y conservan working copies. `PRIMARY` y `SECONDARY` son Windows normal por default, no kiosco. La preparacion del aula es progresiva por Device, `CLASS_TIME_TO_READY` queda como KPI principal y ninguna PC lenta debe bloquear a las demas.

Prompt 14.3 formaliza performance budgets, resource profiles y load shedding sin implementar Prompt 14.4/14.5 ni operaciones Windows reales. Galtek Classroom queda disenado primero para Clients de 4 GB RAM, HDD y CPU de gama baja. El Client idle debe quedar casi sin CPU, sin captura, sin scanning continuo, sin WMI periodico, sin writes periodicos y sin logs sanos repetitivos. Se agregan modelos puros para `LEGACY`, `STANDARD`, `MASTER_BALANCED`, `ResourceWorkClass`, budgets, diagnostico on-demand y estado `DEGRADED` separado de `OFFLINE`.

Prompt 14.4 implementa resiliencia ante power loss, startup rapido y boot storm sin implementar Prompt 14.5 ni operaciones Windows reales. El Agent detecta shutdown no limpio con `agent-service.running`, expone `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive` por IPC, llega a readiness minima antes de validacion comercial completa/WMI y usa jitter acotado para reconexion gRPC. El Master detecta shutdown no limpio con `master-backend.running`, conserva recovery propio de SQLite/WAL/SHM, usa escrituras atomicas/durables para archivos criticos y modela que control-plane ready no espera a Clients online.

Prompt 14.5A optimiza de forma concreta el runtime idle del Agent Service sin cambiar arquitectura ni seguridad. El Service conserva heartbeat de 15 segundos, trust fail-closed, startup phases y revalidacion de trust para `OperationRequest`; reduce allocations sanas de IPC/estado, cachea datos estaticos de proceso y acota el cache de deduplicacion de operaciones con limpieza lazy/event-driven, sin agregar timers ni polling nuevo.

Prompt 14.5B optimiza de forma concreta el runtime idle del Session Agent sin cambiar su arquitectura ni agregar funciones interactivas. El supervisor elimina el `PING` redundante antes de `GET_DEVICE_STATUS` al recuperar conexion, conserva polling sano por `PING` cada 15 segundos, usa resultados IPC no excepcionales para el flujo normal offline/retry y evita crear opciones JSON de consola durante startup background.

Prompt 14.5C optimiza de forma concreta el runtime idle del Master Backend sin cambiar arquitectura, seguridad ni comportamiento funcional. El Master conserva SQLite/WAL/`synchronous=NORMAL`, Hikari pequeno, Flyway, `quick_check`, timeout heartbeat de 45 segundos, TLS/mTLS, trust fail-closed y `MasterAccessGuard`. Se reducen timers, threads, queries y allocations sanas en gRPC, presence snapshots, `ClientHello` registrado e IPC local.

Prompt 14.5D cierra formalmente la etapa de optimizacion preventiva inicial. La revision conjunta de Agent Service, Session Agent y Master Backend no encontro contradicciones reales entre 14.5A/B/C en lifecycle, cleanup, shutdown, schedulers, caches ni seguridad. Se agrega diagnostico runtime ligero y on-demand para pruebas reales, sin telemetria continua, timers, persistencia, dashboard, Protobuf, scheduler general ni tuning JVM/.NET.

Prompt 15A implementa las primeras operaciones remotas productivas del Agent: `SHUTDOWN` y `RESTART`. Llegan exclusivamente por `OperationRequest` sobre el transporte gRPC/mTLS existente, despues de trust `PAIRED`, no `REVOKED`, Device correcto, Commercial License activa y dispatcher autorizado. El Agent anuncia `POWER_CONTROL_V1`, ejecuta power control con API nativa Windows, habilita `SeShutdownPrivilege`, usa countdown fijo de 10 segundos, no fuerza cierre de aplicaciones y devuelve `SUCCESS` solo cuando Windows acepta la solicitud.

Prompt 15B implementa el primer dispatch remoto batch real desde Master para `SHUTDOWN` y `RESTART`. Agrega `POST /api/classrooms/{classroomId}/power-control`, protegido por `MasterAccessGuard`, con request tipada y `targetDeviceIds` explicitos. El Master hace preflight por Device, persiste una `BatchOperation` antes del envio, envia `OperationRequest` solo a conexiones gRPC/mTLS autenticadas `ONLINE` con trust `PAIRED`, Device registrado y `POWER_CONTROL_V1`, correlaciona por `(deviceId, operationId)` y registra `SUCCESS`, `PARTIAL_SUCCESS` o `FAILED`. Timeout o desconexion despues del envio produce `OPERATION_RESULT_UNKNOWN` no retryable.

Prompt 15C implementa reconciliacion segura de `SHUTDOWN`/`RESTART` inciertos. Agrega `OperationStatusQuery`/`OperationStatusReport` al Protobuf v1 sobre `NetworkConnection.Connect`, sin cambiar `protocolVersion`. El Agent responde read-only desde el cache acotado del dispatcher o desde `power-operation-receipts.json`, un receipt durable minimo para power control aceptado. El Master acepta late `OperationResult` autentico, consulta status manualmente con `POST /api/operations/{operationId}/reconcile` y reconcilia de forma ligera en reconnect del mismo Device. No hay retry automatico, resend automatico, scheduler general ni inferencia de `SUCCESS` por `OFFLINE` o reconnect.

Prompt 16B implementa `OPEN_URL` productivo Agent-side. El contrato Protobuf v1 mantiene `protocolVersion` y agrega parametros tipados `OpenUrlOperationParameters.url` dentro de `OperationRequest`, capability `OPEN_URL_V1` y codigos operacionales de URL/sesion. El Agent Service recibe `OperationRequest OPEN_URL`, valida la URL, exige Commercial License activa via dispatcher y envia `OPEN_URL` tipado por Session Command v1. El Session Agent valida nuevamente y pide a Windows abrir la URL con el handler HTTP/HTTPS registrado de la sesion interactiva. No hay endpoint batch Master para `OPEN_URL`, UI, bloqueo de URLs, bloqueo de descargas, seleccion de browser/profile ni `OPEN_APPLICATION`.

Prompt 16A implementa el canal local seguro `Session Command v1` entre `GaltekClassroom.Agent.Service` y `GaltekClassroom.Agent.Session`. El Session Agent sirve un pipe por sesion interactiva (`GaltekClassroom.Agent.SessionCommand.v1.<sessionId>`) derivado de su `Process.SessionId`; el Service resuelve la sesion interactiva con API Windows, verifica el servidor por PID/sesion/ruta productiva antes de enviar y usa request/response tipado con framing de 16 KiB.

Prompt 13 implementa el primer transporte seguro Master-Client: contrato Protobuf v1, servicio gRPC `NetworkConnection.Connect`, TLS/mTLS obligatorio, certificados self-signed de corta vida ligados al trust por fingerprint SPKI, conexion persistente iniciada por el Client, heartbeat, estados `CONNECTING`/`ONLINE`/`OFFLINE` y reconexion con backoff. No redisena Prompt 12.

Prompt 9.6 formaliza el requisito futuro de cuentas Windows administradas en Clients. Cada PC de alumnos podra tener dos cuentas logicas, `PRIMARY` y `SECONDARY`, y el Master podra planificar una sola accion masiva para dejar un aula/grupo/seleccion en la cuenta objetivo con resultados `NO_CHANGE`, `SUCCESS`, `FAILED` y retry solo de fallidos.

El Master Backend Java sigue sin leer `master-binding.json` ni `network-identity.json`, no conoce sus rutas y no recalcula autorizacion local. Consume `GET_MASTER_AUTHORIZATION` por Local IPC v1, mantiene publico `GET /api/master/authorization` para diagnostico y usa `MasterAccessGuard` en endpoints administrativos.

El producto todavia no tiene UI, mDNS, discovery real, captura, bloqueo, filesystem real, sync real, USB real, browser automation, wallpaper real, login/logoff Windows real, cambio real de usuario, proyeccion real ni distribucion real.

## Implementado

- Backend Master en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code` via IPC local al Agent Service.
- Endpoint `GET /api/master/authorization` via IPC local al Agent Service.
- `MasterAccessGuard.requireAuthorized()` en endpoints administrativos reales.
- API administrativa documentada en `docs/api/master-api-v1.md`.
- `GET /api/master/bootstrap` protegido, con authorization status, storage status y aulas activas con conteos.
- CRUD/archive protegido para `Classroom` y `SchoolGroup`.
- CRUD/archive protegido para `Student`, incluyendo `students/batch` y `students/archive-batch`.
- `students/batch` devuelve resultado independiente por fila, con `clientReference`, `SUCCESS`/`FAILED`, `studentId` o `errorCode`.
- `GET /api/classrooms/{id}/students` soporta filtros `groupId`, `active` y `search`.
- Endpoints protegidos de assignments: listar por aula, asignar alumno a device, batch assign y cerrar assignment actual.
- `assignments/batch` hace preflight del lote completo, detecta conflictos internos antes de escribir y registra una `batch_operation` `ASSIGN_STUDENT`.
- `GET /api/classrooms/{id}/snapshot` protegido, con aula, grupos, alumnos activos, devices, assignments actuales, aplicaciones y resumen.
- `GET /api/classrooms/{id}/snapshot` superpone presencia viva para Devices registrados sin generar N+1 ni depender de writes por heartbeat.
- `GET /api/network/clients` protegido, lista Clients conocidos/paired con `networkIdentityId`, estado de trust, estado de conexion, registro, Device/aula si existe, display name, version de Agent, capabilities y timestamps seguros.
- `POST /api/classrooms/{classroomId}/devices/register` protegido, registra un Client `PAIRED` como Device del aula y crea binding; rechaza `REVOKED`, no paired y doble registro.
- `POST /api/classrooms/{classroomId}/power-control` protegido, envia batch `SHUTDOWN`/`RESTART` solo a Devices explicitamente seleccionados.
- Request de power control acepta solo `type` (`SHUTDOWN`/`RESTART`) y `targetDeviceIds` obligatorio, no vacio y sin duplicados; rechaza comandos, rutas, args, timeout, force, mensaje, shell y payload libre.
- `PowerControlDispatchService` hace preflight independiente por target: aula correcta, Device registrado, binding vigente, trust `PAIRED`, no `REVOKED`, conexion autenticada `ONLINE` y capability `POWER_CONTROL_V1`.
- `MasterRemoteOperationGateway` mantiene sesiones gRPC autenticadas y pending operations en memoria por `(deviceId, operationId)`, envia `OperationRequest`, procesa `OperationAccepted`/`OperationResult` y limpia pending state en success, fallo, timeout o desconexion.
- `OperationAccepted` no se trata como `SUCCESS`; solo `OperationResult SUCCESS` marca target exitoso.
- El mismo `operationId` del batch se envia a varios Agents y la correlacion Master usa `(deviceId, operationId)`.
- `OPERATION_RESULT_UNKNOWN` queda como error operacional no retryable cuando una request enviada queda sin resultado confirmado.
- `POST /api/operations/{operationId}/reconcile` protegido por `MasterAccessGuard` consulta solo targets power `FAILED + OPERATION_RESULT_UNKNOWN` que esten online y devuelve la operacion actualizada.
- El Master reconcilia late `OperationResult` solo si el `operationId` existe, el target Device coincide, el tipo corresponde, el target sigue incierto y la sesion gRPC autenticada pertenece al mismo Device.
- El reconnect autenticado de un Device registrado consulta solo operaciones `SHUTDOWN`/`RESTART` inciertas de ese Device; no recorre todo el historial ni reenvia operaciones.
- El startup del Master transforma una vez targets power `PENDING` huerfanos a `FAILED + OPERATION_RESULT_UNKNOWN`, sin esperar Clients online.
- Endpoints protegidos de aplicaciones y operaciones.
- `RestControllerAdvice` uniforme para errores HTTP: validacion 400, no encontrado 404, conflicto/version 409, Master no autorizado 403, Agent/storage no disponible 503.
- Modelos puros Java de Prompt 14.2 para `StudentAssignmentStrategy`, `StudentPreparationStage`, `StudentPreparationStatus`, `StudentPreparationState`, `ClassroomReadinessPlan`, `WorkspaceResidencyState`, `WorkspaceSyncState`, `WorkspaceCommitStage`, `WorkspaceSyncSafetyPlanner`, `ProjectionMode`, `OperationPriority` y `ManagedWindowsAccountOperatingPolicy`.
- `StudentAssignmentStrategy` formaliza `LIST_ORDER`, `RANDOM`, `PREVIOUS` y `MANUAL`.
- `ClassroomReadinessPlan` permite representar readiness parcial por target; un aula puede tener targets `READY`, `PREPARING`, `RECOVERY_REQUIRED` y `FAILED` simultaneamente.
- `WorkspaceSyncSafetyPlanner` bloquea limpieza de working copy local hasta completar `SYNC`, `VERIFY`, `COMMIT_CANONICAL` y `CONFIRM`; sin confirmacion devuelve estado recuperable y conserva la copia local.
- `LogicalWorkspaceDestination.REMOVABLE_STORAGE` formaliza USB futuro como destino logico autorizado, sin rutas arbitrarias.
- `DistributeFileRequest.openAfterDistribution` modela apertura opcional posterior sin transferencia real.
- `ProjectionMode` distingue `SCREEN_SHARE`, `WHITEBOARD`, `POINTER`, `LOCAL_MEDIA` y `OPEN_WEB_CONTENT`.
- `OperationPriority` define `CRITICAL > HIGH > NORMAL > LOW` y defaults conceptuales para operaciones existentes.
- `ManagedWindowsAccountOperatingPolicy` deja `PRIMARY` y `SECONDARY` como Windows normal por default, sin modo restringido, bloqueo de input ni cambio forzado de sesion.
- Modelos puros Java de Prompt 14.3 para `DevicePerformanceProfile`, `MasterPerformanceProfile`, `ResourceWorkClass`, `ClientPerformanceBudget`, `MasterPerformanceBudget`, `LoadSheddingPolicy`, `SheddableWork`, `ResourcePressureState` y `PerformanceDiagnosticPolicy`.
- `DevicePerformanceProfile` formaliza `LEGACY` y `STANDARD`; perfil desconocido de Client se resuelve a `LEGACY`.
- `MasterPerformanceProfile` formaliza `MASTER_BALANCED`.
- `ClientPerformanceBudget` fija objetivos idle de 60 MB Service, 40 MB Session, 100 MB combinado y revision sobre 150 MB combinado, sin convertirlos en garantia contractual.
- `ClientPerformanceBudget` no permite en idle captura, overlays, UI, process scanning, filesystem scanning, WMI query, inventario, prefetch, writes periodicos, logs de heartbeat sano ni diagnostico continuo.
- `MasterPerformanceBudget` fija objetivo inicial de heap JVM 512 MB, Hikari pequeno y rechazo conceptual a infraestructura distribuida pesada o terminal server.
- `LoadSheddingPolicy` modela sacrificio de prefetch, inventario no esencial, thumbnails, calidad/FPS de preview, transferencias no urgentes y background antes de control critico.
- `ResourcePressureState.DEGRADED` queda separado de `OFFLINE`.
- Modelos puros Java de Prompt 14.4 para `MasterStartupReadiness`, `StartupWorkPolicy`, `StartupEvent`, `RemoteOperationDeliveryState`, `RemoteOperationRecoveryDecision` y `RemoteOperationRecoveryPolicy`.
- `MasterStartupReadiness` modela control-plane ready con proceso vivo y storage listo, sin esperar Clients online.
- `StartupWorkPolicy` limita startup de control a `CONTROL_CRITICAL`, difiere `VISUAL`, `TRANSFER` y `BACKGROUND` durante recovery, y prohibe captura automatica en eventos de boot/reconnect/heartbeat.
- `RemoteOperationRecoveryPolicy` evita tratar como exito una operacion remota sin ACK o sin resultado confirmado.
- Modelos puros Java para cuentas Windows administradas: `ManagedWindowsAccount`, `ManagedWindowsAccountType`, `ManagedWindowsAccountStatus` y `WindowsSessionState`.
- `ManagedAccountSwitchPlanner` puro para decidir `NO_CHANGE`, `LOGON`, `SWITCH`, `PENDING` o `BLOCKED` por device.
- Operaciones futuras tipadas `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`.
- `TargetExecutionStatus.NO_CHANGE` tratado como exito no retryable en `BatchOperation`.
- Errores estructurados para cuentas/sesion Windows administrada: `ACCOUNT_NOT_CONFIGURED`, `MANAGED_CREDENTIAL_NOT_CONFIGURED`, `WINDOWS_SESSION_UNKNOWN`, `WINDOWS_LOGON_FAILED`, `WINDOWS_LOGOFF_FAILED`, `SESSION_SWITCH_FAILED` y `CREDENTIAL_PROVIDER_UNAVAILABLE`.
- Contratos compartidos C# para operaciones, destinos logicos, estrategias de asignacion, preparacion, workspace, proyeccion, prioridades, tipos de cuenta, estados de sesion y acciones de switch administrado.
- Local IPC API v1 read-only sobre Windows Named Pipes.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`, `GET_RUNTIME_DIAGNOSTICS`.
- Local IPC v1 permanece read-only; no se agregaron comandos write ni acciones interactivas a `GaltekClassroom.Agent.v1`.
- Protocolo canonico `Session Command v1` documentado en `protocol/local-session-command-v1.md`.
- Contratos compartidos `SessionCommandRequest`/`SessionCommandResponse`, `protocolVersion = 1`, `requestId` UUID, `commandType` tipado y `openUrl` tipado para `OPEN_URL`.
- Framing Session Command v1 con longitud BIG ENDIAN de 4 bytes mas JSON UTF-8 y limite de 16 KiB.
- Nombre de pipe de sesion derivado internamente como `GaltekClassroom.Agent.SessionCommand.v1.<sessionId>`; `SessionId = 0` se rechaza.
- Session Agent background inicia el servidor de comandos solo despues de adquirir instancia unica y validar `Process.SessionId != 0`.
- Pipe server de Session Command v1 espera conexiones con `WaitForConnectionAsync`, sin polling, timer, heartbeat, disk writes ni loop ocupado.
- ACL productiva del pipe de comandos restringida a LocalSystem (`S-1-5-18`) como cliente; no concede `Users`, `Authenticated Users` ni `Everyone`.
- Session Agent valida el SID real del cliente Named Pipe mediante impersonation y rechaza cualquier caller distinto de LocalSystem.
- Agent Service registra `SessionCommandClient` on-demand, sin conexion persistente.
- Agent Service resuelve sesion interactiva mediante `WTSGetActiveConsoleSessionId`, sin procesos externos, shell, PowerShell, WMI shell ni Session 0.
- Agent Service valida el servidor del Named Pipe con `GetNamedPipeServerProcessId`, existencia del proceso, `Process.SessionId` esperado y ruta productiva normalizada del Session Agent antes de enviar request.
- Timeouts del canal de sesion: 2 segundos para connect y request/response.
- `CHANNEL_PING` devuelve `SUCCESS` y no ejecuta acciones externas.
- `SessionCommandClient.OpenUrlAsync(operationId, url)` crea un `requestId` nuevo, envia `OPEN_URL` tipado, valida `requestId` de response y no hace retry automatico.
- Si `OPEN_URL` no llega a enviarse al Session Agent, el Service mapea a `SESSION_AGENT_UNAVAILABLE`; si ya se envio y se pierde/expira la respuesta, mapea a `SESSION_COMMAND_RESULT_UNKNOWN`.
- `OpenUrlSafetyPolicy` C# compartida por Service y Session Agent acepta solo URL absoluta `http://`/`https://`, con host no vacio, sin caracteres de control/CR/LF, sin userinfo y longitud maxima 4096; rechaza rutas locales/UNC, URLs relativas y esquemas no permitidos.
- `OpenUrlOperationHandler` es handler remoto explicito para `OPEN_URL`; no abre navegador, no usa `Process.Start`, `cmd`, PowerShell, scripts, WMI shell ni `CreateProcessAsUser`.
- `WindowsUrlLauncher` en Session Agent llama Windows Shell API `ShellExecuteExW` con verbo `open`, URL validada y sin parametros; no acepta browser/executable path desde Master.
- `OPEN_URL SUCCESS` significa solo que Windows acepto la solicitud para abrir la URL con el handler registrado, no que la pagina cargo ni que hubo HTTP 200.
- `GET_RUNTIME_DIAGNOSTICS` devuelve snapshot on-demand del proceso real `GaltekClassroom.Agent.Service`: working set aproximado, private memory aproximada, CPU acumulado, thread count, uptime y GC managed memory aproximada.
- El transporte IPC local del Master usa virtual threads por intercambio en vez de un cached pool de threads de plataforma.
- Agent Service instalable como Windows Service `GaltekClassroomAgent`.
- Agent Service como autoridad local de Installation Identity, Commercial License y Master Windows Binding.
- Agent Service como autoridad local de Network Identity criptografica del Client.
- `network-identity.json` separado de `installation.json`, `license.dat`, `master-binding.json` y `classroom.db`.
- Metadata de Network Identity schema v1 con `networkIdentityId`, `installationId`, `keyId`, `keyName`, `publicKeyFingerprint` y `createdAtUtc`.
- Llave privada de Network Identity generada localmente en Windows CNG/KSP de maquina, no exportable y fuera de JSON.
- Fingerprint SHA-256 de la public key en formato `SubjectPublicKeyInfo`.
- Primera ejecucion del Service sin metadata ni llave previa crea la Network Identity; reaperturas conservan la misma identidad y fingerprint.
- Estados explicitos de Network Identity: `NOT_CONFIGURED`, `READY`, `INVALID`, `KEY_MISSING` e `INSTALLATION_MISMATCH`.
- Errores explicitos `NETWORK_IDENTITY_INVALID`, `NETWORK_IDENTITY_KEY_MISSING` y `NETWORK_IDENTITY_INSTALLATION_MISMATCH`.
- CLI read-only `--network-identity-status`, sin exponer private key ni `keyName`.
- `-PurgeData` intenta eliminar la llave CNG de Network Identity solo si puede leer un `keyName` valido con prefijo Galtek desde la metadata.
- Master Network Identity local en Java con metadata publica `master-network-identity.json`.
- Private key del Master cifrada fuera de SQLite/JSON plano en `master-network-identity.key`, con `master-network-identity.protector` separado.
- `MasterPairingService` crea challenges firmados solo ante intencion explicita.
- `ClientPairingService` acepta challenges solo con aprobacion explicita y valida destino local, expiracion, fingerprints y firma del Master.
- `PairingResponse` se firma con la private key del Client y permite al Master verificar posesion de llave.
- Trust store del Master en `paired-clients.json`.
- Trust store del Client en `authorized-masters.json`.
- Proteccion contra replay mediante `challengeId`, nonce y registro de challenges pendientes/consumidos.
- Estados de pairing/trust `UNPAIRED`, `PAIRING_PENDING`, `PAIRED` y `REVOKED`.
- Revocacion sin borrar Installation Identity ni Network Identity.
- `REVOKED` y no emparejado fallan cerrado con `MASTER_NOT_PAIRED`.
- Soporte conceptual para multiples Clients por Master y multiples Masters por Client.
- Protobuf versionado en `protocol/network/v1/galtek-classroom-network-v1.proto`.
- gRPC Java/.NET generado desde el contrato compartido.
- Servicio Master `NetworkConnection.Connect` para `ClientHello`, `ConnectionStatus`, `Heartbeat`, `HeartbeatAck`, `OperationRequest`, `OperationAccepted` y `OperationResult`.
- Conexion persistente saliente iniciada por el Client; no depende de puertos entrantes en cada PC Client.
- TLS/mTLS obligatorio, sin fallback plaintext.
- Certificados self-signed de corta vida emitidos desde Network Identity y validados por fingerprint `SubjectPublicKeyInfo` ya persistido en trust.
- Sin CA global que confie automaticamente en cualquier instalacion.
- El Master valida certificados de Client contra `paired-clients.json`, exige `PAIRED` y bloquea `REVOKED`.
- El Client valida el certificado del Master contra `authorized-masters.json`, exige `PAIRED` y bloquea `REVOKED`.
- `ClientHello` transporta ids/fingerprints/public SPKI necesarios, version de Agent y capabilities tipadas, nunca secretos.
- `ClientHello.device_id` queda compatible pero no se usa como identidad; el Master controla `deviceId`.
- `ClientConnectionRegistry` mantiene estado real de Clients conectados como `CONNECTING`, `ONLINE` u `OFFLINE`, distinguiendo paired sin Device y registered con Device.
- `ClientConnectionRegistry` evita el `Heartbeat` sintetico durante `ClientHello`, usa una sola marca de tiempo por pasada de timeout y ofrece snapshots por `networkIdentityId`/`deviceId` sin exponer mapas mutables internos.
- Capabilities conocidas actuales: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`, `POWER_CONTROL_V1` y `OPEN_URL_V1`; capabilities desconocidas se ignoran y no autorizan.
- `RemoteOperationDispatcher` del Agent deduplica por `operationId`, incluye parametros tipados al detectar conflicto de duplicado, aplica timeout, rechaza licencia comercial no activa antes de handler y devuelve `OPERATION_NOT_IMPLEMENTED` para cualquier operacion sin handler.
- `ShutdownOperationHandler`, `RestartOperationHandler` y `OpenUrlOperationHandler` son handlers tipados explicitos; no existe handler generico de comandos.
- `IWindowsPowerController` encapsula power control productivo; `WindowsPowerController` usa `InitiateSystemShutdownExW`, habilita `SeShutdownPrivilege` con `OpenProcessToken`, `LookupPrivilegeValue` y `AdjustTokenPrivileges`, y no usa `shutdown.exe`, `cmd.exe`, PowerShell, WMI shell, scripts ni `Process.Start`.
- `SHUTDOWN` y `RESTART` usan countdown fijo de 10 segundos, mensaje constante del sistema, `forceAppsClosed=false`, sin payload arbitrario, sin `force=true` y sin timeout arbitrario enviado por Master.
- `OperationResult SUCCESS` para power control significa que Windows acepto la solicitud; no significa que la PC ya este apagada o reiniciada.
- Fallos de power control se mapean a `POWER_CONTROL_UNAVAILABLE` o `POWER_CONTROL_FAILED` sin exponer stack traces, rutas, tokens ni codigos Win32 crudos como mensaje principal.
- `RemoteOperationDispatcher` rechaza ejecucion de handlers si Commercial License no esta activa, preservando el bloqueo comercial tras diferir la validacion completa.
- El cache de deduplicacion de `RemoteOperationDispatcher` queda acotado por retencion y maximo de operation IDs completados, con limpieza lazy durante dispatch y sin timers nuevos.
- `RemoteOperationDispatcher.TryGetCompletedResult` expone una consulta read-only del resultado completado sin ejecutar handlers ni extender retencion.
- `PowerOperationReceiptStore` persiste receipts acotados de `SHUTDOWN`/`RESTART` aceptados por Windows en `power-operation-receipts.json`, con cleanup lazy y sin timer.
- `OperationStatusQuery` en el Agent responde `KNOWN` solo desde cache/receipt o `UNKNOWN`; no crea `OperationRequest`, no llama handlers, no modifica Windows y no depende de licencia comercial para reejecutar nada.
- IPC saludable reduce allocations: `PING` reutiliza payload inmutable, `GET_DEVICE_STATUS` reutiliza roles/features vacios cuando aplica y `LocalIpcFraming.WriteJsonAsync` evita construir un frame duplicado completo en memoria.
- Datos estaticos de proceso usados en rutas repetidas se resuelven una vez: hostname del sistema y version del Agent.
- `MasterConnectionStateTracker` actualiza ACK/estado con una sola seccion critica por cambio, conservando `NETWORK_READY` y semantica de heartbeat.
- Heartbeat del Client cada 15 segundos por default; timeout Master default 45 segundos.
- Heartbeat del Agent sin relectura periodica de `authorized-masters.json`; los `OperationRequest` revalidan trust antes de cualquier accion.
- Reconexion del Agent con jitter acotado: initial jitter default hasta 2 segundos y retry jitter default hasta 1 segundo sobre el backoff base.
- Requests IPC exitosos y conexion IPC saludable se registran en `DEBUG`, no `INFO`; autorizacion Master aceptada tambien queda en `DEBUG`; retries repetidos de gRPC bajan a `DEBUG` tras el primer warning.
- Reconexión del Client con backoff `2s`, `5s`, `10s`, `30s`.
- Servidor gRPC del Master configurable con `galtek.classroom.master.network.grpc.enabled`; por defecto no abre puerto.
- Beans runtime de gRPC del Master se crean solo cuando `galtek.classroom.master.network.grpc.enabled=true`.
- `MasterNetworkHeartbeatMonitor` queda activo solo con gRPC habilitado y no crea scheduler hasta que existe una conexion activa; pausa el scheduler cuando todos los Clients estan offline.
- Heartbeat gRPC del Master conserva revalidacion de trust para detectar revocacion en streams abiertos, pero evita reparsear/rehashear la public key del Client tras un `ClientHello` ya aceptado.
- `PersistentNetworkClientConnectionService` registra `ClientHello` de Devices ya registrados con un solo lookup inicial y sin reread posterior del mismo binding.
- Cliente gRPC del Agent configurable con `Galtek:Classroom:Agent:MasterConnection`.
- `master-binding.json` separado de `installation.json`, `license.dat` y `classroom.db`.
- Binding schema v1 con `installationId`, `windowsSid`, `accountDisplayName` y `boundAtUtc`.
- Unico binding por instalacion: cero o un SID autorizado.
- Binding ligado al `installationId`; mismatch bloquea Master.
- Binding corrupto, incompleto, schema desconocido o SID invalido bloquea Master sin tumbar el Service.
- CLI administrativa `--bind-master-current-user`, `--bind-master-account <WINDOWS_ACCOUNT>` y `--replace-master-binding`.
- Comprobacion explicita de elevacion para crear o reemplazar binding; sin autoelevacion.
- Resolucion de cuenta Windows con APIs .NET (`WindowsIdentity`, `NTAccount`, `SecurityIdentifier`), sin shell.
- Identidad real del cliente IPC obtenida con `NamedPipeServerStream.RunAsClient(...)`.
- Respuesta Master Authorization segura: no expone SID completo, JWT, ruta de binding ni ACLs internas.
- Persistencia SQLite local del dominio Master con Spring JDBC, Flyway programatico y repositories explicitos.
- Migracion V2 `device_network_bindings` para vincular Device persistente con `networkIdentityId`, `installationId`, fingerprint publico, version de Agent, capabilities, `registeredAt` y `lastConnectedAt`.
- Indices unicos parciales garantizan maximo un Device vigente por Network Identity y una Network Identity vigente por Device.
- No existe tabla `master_windows_binding` en SQLite.
- `MasterRunMarker` escribe `master-backend.running` al arrancar y lo elimina en cierre limpio para detectar shutdown no limpio sin tocar SQLite/WAL/SHM.
- `AtomicFiles` centraliza escrituras atomicas/durables de archivos criticos del Master.
- `DurableFileWriter` centraliza escrituras atomicas/durables de `installation.json` y `license.dat` en el Agent.
- `AgentRuntimeState` expone fases `STARTING`, `RECOVERING`, `MINIMAL_READY`, `SECURITY_READY`, `NETWORK_READY`, `OPERATION_READY` y `DEGRADED`.
- `Worker.StartAsync` resuelve solo Installation Identity y devuelve tras `MINIMAL_READY`; Network Identity se resuelve en background y gRPC espera `SECURITY_READY`.
- `GET_DEVICE_STATUS` ahora incluye `startupPhase`, `previousShutdownWasUnclean` y `recoveryActive`.
- Session Agent background/autostart via Scheduled Task `GaltekClassroomSessionAgent`; conserva WinExe, AtLogon, RunLevel Limited, mutex por sesion, rechazo de Session 0 y permanencia aunque el Service este caido.
- Session Agent optimizado en Prompt 14.5B: recuperacion inicial con un solo `GET_DEVICE_STATUS`, polling saludable por `PING` cada 15 segundos, backoff `2s/5s/10s/30s`, resultados IPC no excepcionales en el supervisor y sin opciones JSON de consola durante startup background.
- `GaltekClassroom.Agent.Service.exe --runtime-diagnostics` emite snapshot on-demand del proceso actual del Service en modo consola/diagnostico.
- `GaltekClassroom.Agent.Session.exe --agent-runtime-diagnostics` consulta por IPC read-only el snapshot runtime del Agent Service.
- `GaltekClassroom.Agent.Session.exe --runtime-diagnostics` emite snapshot on-demand del proceso Session que ejecuta el diagnostico local.
- El Master Backend agrega `RuntimeDiagnosticsSnapshot` basado en `MemoryMXBean`, `ThreadMXBean` y `RuntimeMXBean`, invocable con `--runtime-diagnostics` sin levantar Spring.
- `docs/testing/PERFORMANCE_VALIDATION.md` documenta medicion manual real para Client legacy idle/offline/online, Master 0 Clients, Master aprox. 26 Clients, startup y `CLASS_TIME_TO_READY`.
- Documentacion de API, contexto, arquitectura, modelo funcional, reglas, decisiones, estado e historial actualizada.
- Contratos compartidos C# para perfiles de performance, clases de trabajo de recursos, estado `DEGRADED` y trabajo sacrificable futuro.

## En progreso

- Prompt 16B cerrado tecnicamente.
- No queda desarrollo 16B a medias.

## Pendiente inmediato

- UI futura para diagnosticar/configurar binding sin convertirse en autoridad.
- IPC write futuro solo cuando exista un diseno de autorizacion local adecuado.
- Mantener cualquier nuevo endpoint administrativo bajo `MasterAccessGuard`.
- Disenar posteriormente almacenamiento seguro de credenciales administradas en el Agent Service del Client.
- Disenar posteriormente login/logoff/switch con integracion soportada por Windows, contemplando Credential Provider.
- Implementar filesystem real de StudentWorkspace y recovery en fases posteriores.
- Implementar sync real, USB real y distribucion real en fases posteriores sin romper la regla `SYNC -> VERIFY -> COMMIT CANONICAL -> CONFIRM`.
- Implementar preview/captura/proyeccion real en fases posteriores distinguiendo modos y costos.
- Implementar scheduler/backpressure real, medicion con profiling real y deteccion conservadora de perfil en fases posteriores solo con evidencia.
- Implementar workflows reales de workspace/sync en fases posteriores.
- Implementar mDNS/discovery real y exponer flujos reales de pairing/discovery sobre red sin convertir discovery en trust.
- Implementar comandos administrativos remotos restantes en fases posteriores sobre el transporte seguro.
- Prompt 16C pendiente: disenar bloqueo/seguridad de URLs sin convertir `OPEN_URL` en ejecucion arbitraria.
- Implementar endpoint batch Master para `OPEN_URL` en una fase posterior, con preflight/capability y persistencia batch.
- Implementar bloqueo de descargas en fase posterior.
- Prompt 14.5A, 14.5B, 14.5C y 14.5D quedan cerrados.
- Empaquetar la llave publica real de Galtek Hub para produccion.

## Cambios aceptados

- Agent Service decide Master authorization; Java solo consume resultado derivado.
- El SID enviado por JSON, HTTP, UI o payload IPC no se considera prueba.
- La autorizacion local Master falla cerrado ante Agent down, binding invalido, licencia no activa, falta de rol `MASTER`, mismatch de instalacion o SID distinto.
- Otro administrador Windows no hereda acceso Master si su SID no esta ligado.
- Rebinding requiere `--replace-master-binding`.
- Update de binarios y uninstall normal preservan `master-binding.json`.
- `-PurgeData` elimina Installation Identity, Commercial License, Master Windows Binding, Network Identity metadata y, si se puede identificar de forma segura, la llave CNG de Network Identity.
- Network Identity no reemplaza pairing, certificados, mTLS ni autorizacion remota.
- Network Identity no es trust; trust solo existe despues de pairing explicito.
- Discovery no es pairing.
- Pairing requiere intencion explicita y challenge/response firmado por Master y Client.
- El trust se persiste en ambos lados: `paired-clients.json` y `authorized-masters.json`.
- `REVOKED` bloquea administracion del Client.
- IP, MAC, hostname y licencia MASTER no crean pairing ni autorizan Clients.
- Metadata corrupta, llave faltante, fingerprint incompatible o `installationId` distinto no se regeneran silenciosamente.
- Nunca se adopta `network-identity.json` de otra instalacion.
- La API administrativa falla cerrado: si Agent Service esta caido devuelve `503 LOCAL_AGENT_UNAVAILABLE`; si Master no esta autorizado devuelve `403`.
- Bootstrap/snapshot usan modelos de lectura agregados batch-friendly para la UI futura.
- `Network Identity != Pairing != Device != Student`; el Master genera y controla `deviceId`.
- `device_network_bindings` vincula Device y Network Identity paired, pero `paired-clients.json` sigue siendo la autoridad de trust.
- `PAIRED + ONLINE + sin Device` se expone como `AVAILABLE_FOR_REGISTRATION`; `PAIRED + binding vigente` como `REGISTERED`; `REVOKED` no es registrable ni administrable.
- Capabilities son informacion operativa, no autorizacion.
- El heartbeat mantiene presencia principalmente en memoria y no escribe SQLite cada 15 segundos.
- El framework de operaciones remotas queda tipado y deduplicado por `operationId`; `ShutdownOperationHandler`, `RestartOperationHandler` y `OpenUrlOperationHandler` son handlers productivos actuales en el Agent, y las operaciones que continuan sin handler devuelven `OPERATION_NOT_IMPLEMENTED`.
- Power control del Agent usa API nativa Windows, no shell ni procesos externos.
- `SHUTDOWN` y `RESTART` habilitan explicitamente `SeShutdownPrivilege`, usan countdown fijo inicial de 10 segundos y no fuerzan cierre de aplicaciones.
- `OperationResult SUCCESS` en power control significa que Windows acepto la solicitud, no que el equipo ya desaparecio de la red.
- `POWER_CONTROL_UNAVAILABLE` no es retryable; `POWER_CONTROL_FAILED` representa fallo operacional potencialmente transitorio.
- `students/batch` permite parcialidad por fila; un alumno invalido no cancela los demas.
- `assignments/batch` preflight completo antes de writes; `TARGET_OCCUPIED` no reemplaza automaticamente.
- El Master no almacena ni envia passwords de cuentas Windows administradas; la UI no recibe secretos.
- Los comandos futuros de cuentas administradas enviaran solo `accountId` logico (`PRIMARY`/`SECONDARY`).
- `SWITCH_MANAGED_ACCOUNT(PRIMARY)` puede producir targets `NO_CHANGE`, `SUCCESS` y `FAILED`; el retry posterior solo aplica a fallidos retryable.
- El hardware objetivo real queda documentado: Master i5 8a gen aprox./16 GB DDR4/SSD 256 GB; Clients renovados aprox. 10 i5 6a gen/8 GB DDR4/SSD 256 GB; Clients legacy aprox. 16 con hardware heterogeneo muy limitado, principalmente 4 GB RAM + HDD y CPUs Core 2 Duo / Celeron / AMD antiguos.
- Galtek Classroom se disena primero para Clients de 4 GB RAM, HDD y CPU de gama baja.
- Si performance compite con una funcion secundaria, se degrada la funcion secundaria antes que afectar Windows, la aplicacion educativa o el control critico de la maestra.
- El Client idle debe ser casi cero: sin captura, scanning continuo, WMI periodico, writes periodicos ni logs sanos repetitivos.
- `LEGACY` es default conservador cuando el perfil del Client es desconocido; `STANDARD` no habilita autorizacion ni concurrencia ilimitada.
- `MASTER_BALANCED` mantiene el Master pequeno, local, con SQLite/WAL/Hikari pequeno y objetivo inicial de heap JVM <= 512 MB salvo profiling real.
- Los perfiles de performance no son identidad, trust, pairing ni autorizacion.
- `DEGRADED` representa presion de recursos y no equivale a `OFFLINE`.
- Diagnostico de performance debe ser on-demand, sin telemetria continua ni envio por heartbeat.
- Performance tuning adicional requiere medicion reproducible en hardware real.
- Load shedding sacrifica prefetch, inventario no esencial, thumbnails, calidad/FPS de preview, transferencias no urgentes y background antes que control critico.
- El Master no es terminal server; aplicaciones interactivas de alumnos corren localmente en Clients.
- `PRIMARY` y `SECONDARY` son Windows normal por default; no son kiosco ni restringen apps/input/sesion sin accion administrativa explicita futura.
- El aula se prepara progresivamente y los primeros Devices `READY` pueden iniciar clase sin esperar al resto.
- `CLASS_TIME_TO_READY` es KPI principal de producto.
- El workspace canonico vive en Master y la working copy local en Client; no usar share SMB como almacenamiento principal del alumno.
- La working copy local no se limpia antes de sync, verify, commit canonico y confirmacion.
- Si falta ACK/confirmacion, conservar datos locales y reportar `PENDING_SYNC` o `RECOVERY_REQUIRED`.
- `REMOVABLE_STORAGE` es destino logico futuro autorizado.
- `OPEN_URL`/`OPEN_WEB_CONTENT` se diferencian de `SCREEN_SHARE`; YouTube debe preferir abrirse localmente en Chrome del Client.
- Las prioridades operacionales deben impedir que transferencias grandes, thumbnails o inventario bloqueen operaciones `CRITICAL`.
- Power loss, reboot abrupto, kill del proceso y boot storm son condiciones normales de diseno.
- Startup rapido del plano de control tiene prioridad sobre licencia comercial completa, WMI costoso, inventario, thumbnails, captura, proyeccion, transferencias grandes y filesystem sync.
- El Master queda control-plane ready con proceso vivo y storage listo, aunque haya 0 Clients online.
- SQLite conserva su recovery propio: no borrar ni recrear `classroom.db`, WAL ni SHM por marker de shutdown no limpio.
- Los markers de ejecucion se escriben al inicio y se eliminan en cierre limpio; no son heartbeat persistente ni deben producir writes periodicos.
- Clasificacion futura de durabilidad: `EPHEMERAL`, `NORMAL` y `CRITICAL_DURABLE`.
- Boot, Session Agent startup, `ClientHello`, pairing, registration, reconnect, heartbeat y `DEVICE_ONLINE` no inician captura/proyeccion/thumbnails/sync/inventario pesado automaticamente.
- Una operacion remota sin ACK o sin resultado confirmado no es `SUCCESS`; requiere reconciliacion posterior.
- Reconexion masiva de Clients usa backoff y jitter acotado para reducir thundering herd.

## Cambios rechazados / No repetir

- No implementar IPC write para set/update/delete de binding en Prompt 09.
- No leer `master-binding.json` desde Java.
- No crear tabla `master_windows_binding` ni migration SQLite.
- No autorizar por username, display name, hostname, IP, MAC, session id o pertenencia a Administrators.
- No usar `whoami.exe`, PowerShell, WMI shell ni procesos externos para obtener SID.
- No exponer SID completo, JWT, hashes de hardware, rutas internas ni ACLs internas en respuestas IPC/HTTP.
- No reintroducir endpoints administrativos sin `MasterAccessGuard`.
- No implementar UI, gRPC, pairing, comandos remotos ni filesystem real en Prompt 10.
- No implementar passwords reales, DPAPI, Credential Provider, login/logoff Windows real, cambio real de usuario ni almacenamiento de credenciales en Prompt 9.6.
- No implementar pairing, certificados emitidos por Master, CA, mTLS real, gRPC, discovery, comandos remotos, rotacion automatica de claves ni UI en Prompt 11.
- No implementar acciones reales `LOCK_INPUT`, `UNLOCK_INPUT`, `SHUTDOWN`, `RESTART`, `OPEN_APPLICATION`, `OPEN_URL`, login Windows, archivos, wallpaper, captura, proyeccion, mDNS, discovery ni UI como parte del cierre de Prompt 14.
- No implementar Prompt 14.5, captura/proyeccion real, sync real ni scheduler real como parte del cierre de Prompt 14.4.
- No redisenar ni reimplementar pairing despues de Prompt 13; usar el trust ya persistido.
- No guardar private key de Network Identity en JSON, logs, SQLite ni archivos planos.
- No usar Commercial License, IP, MAC ni hostname como Network Identity, trust ni autorizacion.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para automatizar sesiones Windows.
- No convertir `PRIMARY` ni `SECONDARY` en kiosco dentro de Prompt 14.2.
- No implementar login/logoff Windows real, Credential Provider, filesystem real, sync real, USB real, Chrome automation, captura, proyeccion, distribucion real, OPEN_APPLICATION real, OPEN_URL real, power-loss recovery tecnico, performance tuning, mDNS ni UI como parte de Prompt 14.2.
- No implementar Prompt 14.4 ni Prompt 14.5 dentro de Prompt 14.3.
- No implementar power-loss recovery tecnico, operaciones Windows reales, captura/proyeccion real, transferencia real, filesystem sync real, scheduler real, deteccion agresiva de hardware ni monitoreo continuo pesado dentro de Prompt 14.3.
- No usar hardware profile como autorizacion.
- No crear unit tests que fallen por memoria/CPU/Working Set exacto.
- No reintroducir logs `INFO` por requests IPC exitosos, autorizacion aceptada, PING/heartbeat sano ni relecturas periodicas de trust store por heartbeat idle.
- No borrar working copies locales para completar sync o move.
- No modelar YouTube como screen share obligatorio.
- No hacer commits automaticamente.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- No existe todavia una llave publica real de Galtek Hub empaquetada; si falta llave publica, la licencia queda en `LICENSE_KEY_NOT_CONFIGURED` y Master no autoriza.
- `license.dat` no se cifra localmente en esta fase.
- `classroom.db` no tiene cifrado at-rest, backup/restore automatico ni politica de retencion/borrado seguro de PII.
- `master-network-identity.key` y `master-network-identity.protector` son almacenamiento separado y cifrado minimo para desarrollo/local; no son hardening productivo final.
- Los certificados TLS actuales son self-signed de corta vida emitidos en memoria desde Network Identity; falta ciclo de vida productivo de certificados y rotacion operacional.
- La validacion productiva con Service Control Manager, Task Scheduler y CLI elevada depende de ejecutar en un entorno con permisos administrativos.
- La creacion real de la llave CNG de Network Identity requiere el contexto del Service como `LocalSystem` o una consola elevada; una prueba manual desde shell no elevado devuelve acceso denegado.
- No se creo una segunda cuenta Windows para prueba manual de SID distinto; ese caso queda cubierto por tests automatizados.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~OpenUrlSafetyPolicyTests|FullyQualifiedName~SessionCommand|FullyQualifiedName~WindowsUrlLauncherTests|FullyQualifiedName~OpenUrlOperationHandlerTests"` en `agent`: correcto, 19 pruebas Session y 48 pruebas Service superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~SessionCommand|FullyQualifiedName~SessionAgentBackgroundHostTests"` en `agent`: correcto, 11 pruebas Session y 14 pruebas Service superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~PowerOperationHandlerTests|FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 28 pruebas Service superadas.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,PowerOperationReconciliationServiceTest,NetworkClientControllerTest" test` en `master-backend`: correcto, pruebas dirigidas de gateway, reconciliacion y endpoint power-control superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 17 pruebas Session y 136 pruebas Service superadas.
- `mvn test` en `master-backend`: correcto, 162 pruebas superadas.

## Proximo paso recomendado

Siguiente fase recomendada: Prompt 16C para bloqueo/seguridad de URLs y politicas posteriores sin convertir `OPEN_URL` en ejecucion remota arbitraria.
