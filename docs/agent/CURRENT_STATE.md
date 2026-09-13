# Estado actual

## Ultima actualizacion

2026-09-13 - Prompt 19I2-F01: correccion de tokenizacion sc.exe del installer real.

## Estado del proyecto

Prompt 19I2-F01 corrige un INSTALLER BUG encontrado en una primera instalacion real sobre una PC descartable Windows 11 Education x64 10.0.22621. `sc.exe create` fallaba al crear `GaltekClassroomAgent` con exit code 1639 porque `install-agent-service.ps1` enviaba opciones de `sc.exe` como argv combinados (`binPath= <value>`, `DisplayName= <value>`, `start= auto`, `obj= LocalSystem`, `reset= <value>`, `actions= <value>`) en vez de separar option y value como requiere la sintaxis nativa.

El fallo fue fail-safe: el Service no fue creado, la scheduled task `GaltekClassroomSessionAgent` no fue instalada, el Credential Provider Galtek no fue registrado, el COM CLSID Galtek no fue registrado, y solo quedaron copiados binarios/directorios previos bajo Program Files/ProgramData antes de abortar. La correccion extrae builders PowerShell puros para argv de Service Control y ajusta `create`, `config` y recovery `failure` para enviar `binPath=`, `DisplayName=`, `start=`, `obj=`, `reset=` y `actions=` como tokens separados de sus valores, conservando el quoting del ImagePath para rutas bajo `C:\Program Files\...`. `description`, `failureflag` y `delete` fueron auditados y no requieren cambio de tokenizacion.

Estado 19I2 despues del fix: `REAL_INSTALL_VALIDATION_PENDING`. No declarar 19I2 superado ni installer real validado hasta repetir fresh install en la PC de laboratorio.

La estabilizacion post-auditoria corrige hallazgos de cimientos sin avanzar 19I2 ni cambiar contratos funcionales. El `RemoteOperationDispatcher` del Agent ahora falla al construirse si DI registra dos handlers productivos para el mismo `NetworkOperationType`, con mensaje no secreto del tipo `Duplicate remote operation handler registration: SWITCH_MANAGED_ACCOUNT`. La deduplicacion vigente sigue usando `RequestSignature`; el helper stale `SameParameters` fue eliminado. Se actualizaron residuos documentales de Local IPC unlock y Managed Windows Accounts para reflejar 18B2/19H1/19H2. No hubo cambios en Protobuf, Java, native C++, installer, SQLite, Credential Provider, capabilities ni handlers funcionales.

Prompt 19I1 integra el Credential Provider nativo al lifecycle normal del Agent sin registrar el provider ni ejecutar logon/switch real en la maquina de desarrollo. Se agregan scripts productivos para publicar, verificar package, instalar, verificar instalacion y desinstalar el provider. El package vive por default en `artifacts/windows/credential-provider/`, contiene solo `GaltekClassroom.CredentialProvider.dll` y `credential-provider.manifest.json`, y el manifest guarda metadata no secreta con CLSID fijo, arquitectura x64, SHA-256 y packageId deterministico basado en el hash.

Release x64 del Credential Provider y su self-test nativo usan `RuntimeLibrary=MultiThreaded` (`/MT`) para evitar dependencia productiva de Visual C++ Redistributable manual dentro de LogonUI. El publish productivo localiza MSBuild, compila `Release|x64`, valida PE x64, calcula SHA-256 despues de producir la DLL final y reporta estado Authenticode sin fingir firma. `SIGNED_INVALID` falla cerrado; `NOT_SIGNED` queda permitido solo para deployment controlado/lab hasta que exista signing real.

El install productivo exige elevacion, Windows x64, PowerShell x64, manifest valido, hash correcto, DLL PE x64, firma no invalida, Agent Service instalado, ACL sin write obvio para `Everyone`/`Authenticated Users`/`Builtin Users`, ausencia de filter Galtek y registro HKLM x64. Staging es side-by-side bajo `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\versions\<packageId>\`; `InprocServer32` apunta directo a la DLL de la version activa. El installer captura rollback in-memory de las claves Galtek y falla cerrado ante conflicto si el CLSID apunta fuera del root Galtek.

El uninstall productivo elimina solo el provider key Galtek y luego la registration COM Galtek; despues intenta limpiar paquetes best-effort sin matar LogonUI/Winlogon, sin reboot automatico y sin tocar otros providers. Si queda una DLL locked, reporta `UNREGISTERED_REBOOT_CLEANUP_REQUIRED`. Los orchestrators `publish-agent.ps1`, `install-agent.ps1` y `uninstall-agent.ps1` integran Service, Session Agent y Credential Provider en orden seguro. Los scripts especificos de Service preservan `Agent\Session\` y `Agent\CredentialProvider\`; los scripts de Session no tocan Credential Provider. La validacion real queda en estado `PACKAGE_VERIFIED`, `INSTALLER_PREPARED`, `REAL_LOGON_VALIDATION_PENDING`.

Prompt 19H2 agrega en el Master Backend Java `POST /api/operations/{operationId}/retry` como retry administrativo explicito y selectivo solo para `BatchOperation` existentes de tipo `SWITCH_MANAGED_ACCOUNT`. El endpoint usa `MasterAccessGuard` antes de leer storage o hacer trabajo remoto, acepta solo `targetDeviceIds`, rechaza campos extra incluyendo `targetAccountId`, source, passwords, SID, username, force y allFailed, y toma la cuenta objetivo solo del payload durable original `schemaVersion = 1`.

El retry opera sobre el mismo batch: conserva `operationId`, `createdAtUtc`, `requestedBy`, `targetCount` y payload. Solo acepta targets del batch original que actualmente esten `FAILED`, con `errorCode` no nulo y retryable; `SUCCESS`, `NO_CHANGE`, `PENDING` y `OPERATION_RESULT_UNKNOWN` de switch nunca se reintentan. Antes del primer snapshot remoto reclama todos los targets seleccionados transaccionalmente de `FAILED` a `PENDING` con `attempt + 1`; conflictos concurrentes devuelven `CONCURRENT_MODIFICATION` sin fanout. Cada target reclamado usa preflight tecnico fresco, snapshot fresco `GET_WINDOWS_SESSION_STATE` con operationId remoto nuevo y, si requiere mutation, solo `SWITCH_MANAGED_ACCOUNT(targetAccountId original)` con operationId remoto nuevo. No hay retry automatico, scheduler, polling ni startup recovery de targets `PENDING`.

Prompt 19H1 implementa en el Master Backend Java `POST /api/classrooms/{classroomId}/managed-accounts/switch` como batch administrativo para dejar Devices explicitamente seleccionados en `PRIMARY` o `SECONDARY`. El endpoint usa `MasterAccessGuard` antes de cualquier lectura escolar, acepta solo `targetAccountId` y `targetDeviceIds`, crea una sola `BatchOperation` `SWITCH_MANAGED_ACCOUNT`, persiste targets antes del primer snapshot remoto y guarda payload no secreto con `schemaVersion` y `targetAccountId`.

El preflight local por target exige Device del aula, registro/binding vigente, trust `PAIRED`, no `REVOKED`, conexion autenticada `ONLINE`, `WINDOWS_SESSION_STATE_V1` y `WINDOWS_SESSION_SWITCH_V1`; no exige `SESSION_AGENT_AVAILABLE`. Luego cada target listo obtiene `GET_WINDOWS_SESSION_STATE` con operationId propio. El planner corregido usa el snapshot real: target ya activo produce `NO_CHANGE`, `NO_SESSION` planifica logon, opposite managed activo planifica switch, `OTHER_SESSION_ACTIVE` bloquea como `WINDOWS_SESSION_CHANGED` y `UNKNOWN` como `WINDOWS_SESSION_UNKNOWN`.

El Master nunca encadena `LOGOFF_WINDOWS_SESSION + LOGON_MANAGED_ACCOUNT`: tanto plan `LOGON` como plan `SWITCH` despachan la primitive Agent-side `SWITCH_MANAGED_ACCOUNT(target)` con otro operationId remoto unico. El Master no consulta Credential Vault, no envia password/credentialId/vault token/SID/username/sessionId, no fabrica readiness de cuenta o credencial y preserva errores estructurados del Agent. `NO_CHANGE` cuenta como exito, se persiste y queda fuera de retryable targets. No hay retry automatico.

Prompt 19G4 completa `SWITCH_MANAGED_ACCOUNT(targetAccountId)` como operacion remota tipada para un Client individual. El Master Java agrega `MasterRemoteOperationGateway.switchManagedAccount(...)` y envia solo `ManagedWindowsAccountId account_id` (`PRIMARY`/`SECONDARY`) por Protobuf; no envia source account, password, username, SID, domain, sessionId, credentialId, vault token, force, timeout configurable, comando ni payload generico. No hay endpoint HTTP, BatchOperation, fanout, planner ni UI.

El Agent anuncia `WINDOWS_SESSION_SWITCH_V1` y ejecuta SWITCH por el `RemoteOperationDispatcher` normal: gRPC/mTLS, Master esperado, trust `PAIRED`, no `REVOKED`, Device correcto, operacion soportada y Commercial License Client `ACTIVE`. `UNLOCK_INPUT` sigue siendo la unica excepcion recovery-safe. Target ya activo devuelve `SUCCESS` idempotente sin logoff, logon ni DPAPI. `NO_SESSION` reutiliza `WindowsSessionLogonService`. `OTHER_SESSION_ACTIVE` nunca se cierra automaticamente y devuelve `WINDOWS_SESSION_CHANGED`; `UNKNOWN` devuelve `WINDOWS_SESSION_UNKNOWN`.

Cuando la consola esta en el opposite managed account, `WindowsSessionSwitchService` deriva source exclusivamente desde `WindowsSessionState`, valida target antes de cerrar source solo de forma estructural (binding, SID `SidTypeUser` y credential DPAPI usable), relee source, llama internamente `WindowsSessionLogoffService` expected-account, espera de forma local y acotada a confirmar `NO_SESSION` y luego llama `WindowsSessionLogonService` para el target. La disponibilidad real de LogonUI/Credential Provider se verifica en ese tramo LOGON posterior a `NO_SESSION`. Si target aparece activo durante la espera, devuelve `SUCCESS` sin activation nueva. Si no se confirma `NO_SESSION`, devuelve `WINDOWS_SWITCH_NOT_CONFIRMED`.

SWITCH reutiliza LOGOFF 19F y LOGON 19G3; no duplica WTS logoff ni Credential Provider logic y no cambia C++ nativo. Una vez aceptado WTS logoff puede haber efecto parcial, incluido `CREDENTIAL_PROVIDER_UNAVAILABLE` si LogonUI/Credential Provider no aparece despues de `NO_SESSION`; no hay rollback automatico a source, retry automatico, journal, receipt ni ampliacion de `OperationStatusQuery`. El Master usa timeout fijo especifico de 75s para SWITCH; si pierde el resultado conserva `OPERATION_RESULT_UNKNOWN`.

Prompt 19G3 completa `LOGON_MANAGED_ACCOUNT` remoto end-to-end para un Client individual. El Master Java agrega `MasterRemoteOperationGateway.logonManagedAccount(...)` y envia solo `ManagedWindowsAccountId account_id` (`PRIMARY`/`SECONDARY`) por Protobuf; no envia password, username, SID, domain, sessionId, credentialId, vault token, comando ni payload generico. No hay endpoint HTTP, BatchOperation, fanout, planner ni UI.

El Agent anuncia `WINDOWS_SESSION_LOGON_V1` y ejecuta logon solo despues del `RemoteOperationDispatcher` normal: gRPC/mTLS, Master esperado, trust `PAIRED`, no `REVOKED`, Device correcto, operacion soportada y Commercial License Client `ACTIVE`. `UNLOCK_INPUT` sigue siendo la unica excepcion recovery-safe. El preflight observa la consola fisica: target ya activo devuelve `SUCCESS` idempotente sin activation ni DPAPI; `NO_SESSION` permite continuar; otra sesion activa devuelve `WINDOWS_SESSION_CHANGED`; estado no confiable devuelve `WINDOWS_SESSION_UNKNOWN`.

Antes de activar, el Agent valida binding local, SID `SidTypeUser` y credencial DPAPI usable ligada al mismo SID; luego revalida inmediatamente que la consola siga en `NO_SESSION`. La activation remota es efimera e in-memory, con `activationId`, `operationId`, `accountId`, TTL, `autoSubmitRequested=true` y SID esperado cuando ya fue resuelto. Otra activation remota de distinto operationId devuelve `WINDOWS_LOGON_BUSY`; duplicados de la misma operacion/cuenta reutilizan el mismo resultado.

El bridge local `GaltekClassroom.CredentialProvider.v1` suma `WAIT_FOR_ACTIVATION_CHANGE(observedGeneration)` y `REPORT_LOGON_RESULT(activationId,outcome)`. El Service mantiene un generation counter y listener count en memoria; el Credential Provider, mientras esta en `CPUS_LOGON` y `Advise` activo, espera cambios sin polling sano, usa marshaling COM inter-thread y llama `ICredentialProviderEvents::CredentialsChanged`. Si no hay listener LogonUI validado antes de activation, el logon remoto falla como `CREDENTIAL_PROVIDER_UNAVAILABLE`.

Con activation remota, el provider enumera una credential, default `0`, `pbAutoLogonWithDefault=TRUE` y auto-submit exactly once. `GetSerialization` adquiere password local una sola vez, usa buffers RAII, `CredProtectW`, `KERB_INTERACTIVE_UNLOCK_LOGON` y paquete `Negotiate`. `ReportResult(STATUS_SUCCESS)` reporta `SUCCESS`; rechazo de autenticacion reporta `FAILED`; fallo local despues de acquire reporta `LOCAL_SERIALIZATION_FAILED`. Solo `SUCCESS` produce `OperationResult SUCCESS`; `FAILED` y `LOCAL_SERIALIZATION_FAILED` producen `WINDOWS_LOGON_FAILED`; timeout produce `WINDOWS_LOGON_NOT_CONFIRMED` y limpia la activation.

Prompt 19F implementa `LOGOFF_WINDOWS_SESSION` como operacion remota tipada y segura del lado Agent para cerrar solo la sesion Windows administrada esperada en la consola fisica. El Protobuf v1 agrega `LogoffWindowsSessionOperationParameters(account_id)`, capability `WINDOWS_SESSION_LOGOFF_V1` y errores `WINDOWS_SESSION_CHANGED`/`WINDOWS_LOGOFF_FAILED`. El Agent Service/LocalSystem exige binding local `PRIMARY`/`SECONDARY`, observa consola fisica por `WTSGetActiveConsoleSessionId()`, obtiene SID real mediante `WTSQueryUserToken` + `GetTokenInformation(TokenUser)`, compara solo contra `managed-windows-accounts.json`, repite sessionId+SID inmediatamente antes de llamar `WTSLogoffSession(WTS_CURRENT_SERVER_HANDLE, sessionId, FALSE)` y no usa Session Agent, password, DPAPI, Credential Vault, shell ni polling. `NO_SESSION` es `SUCCESS` idempotente; otra sesion activa devuelve `WINDOWS_SESSION_CHANGED` y no se cierra; `SUCCESS` significa solicitud WTS aceptada, no cierre confirmado. El Master Java agrega solo `MasterRemoteOperationGateway.logoffWindowsSession(...)`, OperationType/capability/error mappings y tests; no hay endpoint HTTP, BatchOperation, fanout, planner, UI, LOGON, SWITCH, retry automatico ni reconciliation.

Prompt 19E2 agrega `ManagedCredentialProvisioningBridge` como primitive interna Java-only del Master Backend para conectar Credential Vault -> `MasterRemoteOperationGateway.provisionManagedCredential(...)` sobre un solo `deviceId` explicito. La operacion exige `MasterAccessGuard.requireAuthorized()` antes de tocar el vault, requiere sesion de vault vigente, valida metadata antes de extraer secreto, permite solo `WINDOWS_ACCOUNT`, rechaza `GOOGLE_ACCOUNT` con `CREDENTIAL_NOT_PROVISIONABLE`, acepta solo `vaultSessionToken`, `credentialId`, `deviceId`, `operationId` y `PRIMARY`/`SECONDARY`, codifica la password como UTF-16LE sin BOM/NUL y limpia el `byte[]` controlado en `finally`. El bridge no devuelve password, no registra reveal humano, no envia `credentialId` ni vault token al Client, no crea endpoint HTTP, UI, BatchOperation, SQLite migration, fanout, retry automatico, startup provisioning ni cambios Client.

Prompt 19E1 implementa `PROVISION_MANAGED_CREDENTIAL` como operacion remota tipada y secret-bearing para provisionar o reemplazar en el Agent Service la password almacenada por Galtek Client para `PRIMARY`/`SECONDARY`. El Protobuf v1 agrega `ProvisionManagedCredentialOperationParameters` con `ManagedWindowsAccountId account_id` y `bytes password_utf16le`; el password viaja como UTF-16LE sin BOM/NUL y no como string. El transporte usa solo el gRPC/mTLS existente con Master esperado, trust `PAIRED`, no `REVOKED` y Device correcto; no hay HTTP, Local IPC, Session Command, file/clipboard fallback ni compresion especifica. El handler valida framing/accountId/longitud, copia a un buffer mutable controlado, lee Installation Identity, valida binding/SID local por el store, protege inmediatamente con DPAPI y persiste durablemente `managed-windows-credentials.dat`. `SUCCESS` no valida la password contra Windows ni cambia la password real. El dedupe ya no conserva `OperationRequest` completo; para provisioning retiene solo metadata no secreta y el resultado, sin password ni hash. Se anuncia `MANAGED_CREDENTIAL_PROVISIONING_V1`. El Master Java agrega solo un metodo explicito `provisionManagedCredential(...)` en `MasterRemoteOperationGateway` para construir el request tipado; no agrega endpoint HTTP ni BatchOperation.

Prompt 19D implementa en el Agent Service del Client el almacenamiento seguro local de passwords Windows administradas para `PRIMARY`/`SECONDARY`. `managed-windows-credentials.dat` vive en el data directory del Agent (`<CommonApplicationData>\Galtek\Classroom\` u override), separado de `managed-windows-accounts.json` y del Credential Vault del Master. El envelope externo contiene solo `schemaVersion`, `installationId`, `accountId`, `protectedData` y timestamps; `windowsSid` y password viven dentro de un payload binario protegido por Windows DPAPI. DPAPI usa scope de usuario actual del proceso productivo LocalSystem, exige LocalSystem antes de protect/unprotect, usa `CRYPTPROTECT_UI_FORBIDDEN`, optional entropy deterministica por instalacion/slot, no usa LocalMachine y no tiene fallback plaintext. El store interno agrega GetStatus/Add/Replace/Remove/Acquire, valida binding obligatorio y SID `SidTypeUser`, devuelve leases disposable con buffers limpiables y no expone reveal, CLI password, Local IPC, UI, login/logoff/switch, heartbeat fields, polling ni trabajo idle.

Prompt 19C implementa en el Agent Service del Client `GET_WINDOWS_SESSION_STATE` productivo, read-only y on-demand. Usa `WTSGetActiveConsoleSessionId()` como autoridad de consola fisica; `0xFFFFFFFF` y Session 0 producen `UNKNOWN`. `WTSUserName` se usa solo como senal auxiliar para distinguir ausencia de login (`NO_SESSION`), nunca como identidad. Cuando hay usuario, el Service debe estar como LocalSystem, habilita `SeTcbPrivilege` de forma acotada, obtiene token con `WTSQueryUserToken`, lee `TokenUser`, convierte el SID con API Windows soportada y cierra/libera token y buffers. El SID activo se compara contra `managed-windows-accounts.json`: `PRIMARY_ACTIVE`, `SECONDARY_ACTIVE`, `OTHER_SESSION_ACTIVE`, `NO_SESSION` o `UNKNOWN`. El resultado remoto es `WindowsSessionStateResult.state` tipado y no expone SID, username, accountReference ni sessionId. Se anuncia `WINDOWS_SESSION_STATE_V1`. No agrega Master endpoint/batch, UI, Local IPC, Session Command, Session Agent dependency, heartbeat state, polling, browser policy integration, login/logoff/switch ni writes.

Prompt 19B implementa en el Agent Service del Client el binding local seguro de cuentas Windows administradas. `managed-windows-accounts.json` vive en el data directory del Agent (`<CommonApplicationData>\Galtek\Classroom\` u override), separado de identidades, licencia, trust, application bindings, browser policy state y `credential-vault.dat`. El documento liga los slots exactos `PRIMARY`/`SECONDARY` al `installationId` actual y a cuentas Windows reales por SID; `accountReference` es metadata canonica devuelta por Windows. No guarda passwords, hashes, credentialId, tokens, profile paths ni sesiones. Bind/replace/remove son CLI locales administrativas elevadas; list/status es read-only y reporta `NOT_CONFIGURED`, `CREDENTIAL_NOT_CONFIGURED` o `ACCOUNT_NOT_FOUND`, con `credentialConfigured=false` siempre en 19B. No agrega IPC, Protobuf, gRPC, Session Agent, Java productivo, browser policy enforcement, DPAPI, Client credential store, login/logoff/switch ni Credential Vault integration.

Prompt 19A implementa en el Master Backend el nucleo seguro interno de una boveda local cifrada de credenciales escolares para la profesora. `credential-vault.dat` vive en el Master data directory, separado de `classroom.db`, y guarda un envelope JSON versionado con el documento logico completo cifrado. La master password de vault no se persiste, PBKDF2-HMAC-SHA256 deriva la KEK, AES-256-GCM envuelve un DEK aleatorio de 256 bits y AES-256-GCM cifra entries completas. La boveda no se crea en startup, queda locked tras restart, permite una sola sesion in-memory con expiracion lazy de 5 minutos, CRUD interno, reveal de una credencial a la vez y cambio de master password por re-wrap del DEK. No agrega UI, endpoints HTTP, Protobuf, gRPC, Client credential store, login Windows, Google automation ni SQLite migrations.

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

Prompt 16B implementa `OPEN_URL` productivo Agent-side. El contrato Protobuf v1 mantiene `protocolVersion` y agrega parametros tipados `OpenUrlOperationParameters.url` dentro de `OperationRequest`, capability `OPEN_URL_V1` y codigos operacionales de URL/sesion. El Agent Service recibe `OperationRequest OPEN_URL`, valida la URL, exige Commercial License activa via dispatcher y envia `OPEN_URL` tipado por Session Command v1. El Session Agent valida nuevamente y pide a Windows abrir la URL con el handler HTTP/HTTPS registrado de la sesion interactiva. No hay UI, seleccion de browser/profile ni `OPEN_APPLICATION`.

Prompt 16C agrega en el Master Backend la fuente de verdad persistente y determinista para politicas administrativas de navegacion web. Se agregan dominio `browserpolicy`, normalizador URL, evaluador puro, resolver de policy efectiva, migracion SQLite `V3`, repositorio JDBC explicito y API administrativa protegida. No se modifican Agent .NET, Protobuf/gRPC, Session Command, Local IPC ni UI; no se aplica bloqueo real en navegadores y no se modelan descargas.

Prompt 16D agrega enforcement Agent-side de politicas de navegacion para Chrome y Edge usando `URLBlocklist`/`URLAllowlist` de Chromium en `HKEY_USERS\<SID>` del usuario interactivo real. Agrega operacion remota tipada `APPLY_BROWSER_NAVIGATION_POLICY`, parametros Protobuf tipados, capability `BROWSER_NAVIGATION_POLICY_V1`, compilador/evaluator C#, resolver de usuario interactivo por APIs Windows, registry store con ACL, state durable `browser-navigation-policy-state.json`, journal lazy `browser-navigation-policy-apply.json` y defensa en profundidad para `OPEN_URL`. No agrega endpoint batch Master, UI, Session Command nuevo, descargas, extension, proxy, DNS, firewall, hosts, inspeccion HTTPS, polling, browser automation ni matar/reiniciar navegadores.

Prompt 16E1 agrega en el Master Backend la fuente de verdad persistente para politicas de descarga de navegador. Se agregan `BrowserDownloadPolicy`, enum `BrowserDownloadRestrictionMode`, resolver puro con la misma precedencia de navegacion, migracion SQLite `V4__add_browser_download_policies.sql`, repositorio Spring JDBC explicito y API administrativa protegida. No se modifica Agent .NET, Protobuf/gRPC, Registry, C# ni enforcement; no se modelan listas arbitrarias de extensiones/MIME y no se implementan descargas autorizadas por maestra.

Prompt 16E2B implementa enforcement Agent-side real para politicas de descarga de navegador. El Agent registra `ApplyBrowserDownloadPolicyOperationHandler`, anuncia `BROWSER_DOWNLOAD_POLICY_V1`, resuelve el usuario interactivo real, usa el compilador `ChromiumDownloadPolicyCompiler` como autoridad de mapping y aplica `DownloadRestrictions` como `REG_DWORD` en `HKEY_USERS\<SID>\Software\Policies\Google\Chrome` y `HKEY_USERS\<SID>\Software\Policies\Microsoft\Edge`. Agrega store especifico de descarga, state durable `browser-download-policy-state.json`, journal lazy `browser-download-policy-apply.json`, ownership conservador, conflicto ante `DownloadRestrictions` HKLM/user-level ajeno, hardening ACL parent-only, rollback y recovery tras power loss. No agrega dispatch batch desde Master, UI, Session Command, extension, proxy, DNS, firewall, hosts, browser automation, process scan, version polling ni DLP.

Prompt 16F1 agrega dispatch batch desde Master para aplicar policies persistidas de navegacion y descarga. Expone `POST /api/classrooms/{classroomId}/browser-policies/apply` y `POST /api/classrooms/{classroomId}/browser-download-policies/apply`, protegidos por `MasterAccessGuard`, con request estricta de `targetDeviceIds`. El Master resuelve la policy efectiva desde SQLite por target con `accountType = null` (`ANY` solamente), deriva grupo por assignment actual, congela parametros Protobuf tipados antes del fanout, persiste una unica `BatchOperation`, reutiliza `MasterRemoteOperationGateway` sobre gRPC/mTLS y registra resultados por target. No modifica Agent, Protobuf, Registry ni Session Command, no agrega UI y en esa fase no implementa endpoint batch Master para `OPEN_URL`.

Prompt 16F2 agrega dispatch batch desde Master para `OPEN_URL`. Expone `POST /api/classrooms/{classroomId}/open-url`, protegido por `MasterAccessGuard`, con request estricta de `url` y `targetDeviceIds`. El Master valida safety estructural global con `OpenUrlPolicy`, resuelve policy efectiva por target con `BrowserPolicyPrecedenceResolver` y `accountType = null` (`ANY` solamente), deriva grupo por assignment actual, evalua `BrowserNavigationPolicyEvaluator` incluyendo `EXACT_URL`, congela `OpenUrlOperationParameters.url`, persiste una unica `BatchOperation` `OPEN_URL` antes del fanout y reutiliza `MasterRemoteOperationGateway`. No modifica Agent, Protobuf, Registry, Session Command ni UI; no agrega retry automatico, reconciliacion, browser selector ni policy apply automatico.

Prompt 17A agrega en `GaltekClassroom.Agent.Service` el catalogo local seguro `application-bindings.json` para vincular `applicationId` logico Galtek con un target local del Client. Soporta solo `APP_PATHS` y `ABSOLUTE_EXE`, se configura por CLI local administrativa elevada, usa escritura durable y ACL local, falla cerrado ante corrupcion y no ejecuta aplicaciones todavia. No modifica Master, Java, Protobuf, Session Agent, Session Command, installer, UI ni dispatch remoto.

Prompt 17B implementa `OPEN_APPLICATION(applicationId)` productivo del lado Agent/Session sin endpoint/batch Master. Protobuf y Session Command transportan solo `applicationId`; el Agent Service valida el binding local y envia el comando tipado, y el Session Agent vuelve a leer/validar `application-bindings.json`, resuelve `ABSOLUTE_EXE` o `APP_PATHS` HKLM-only y lanza con `CreateProcessW` en la sesion interactiva. No hay argumentos, shell, HKCU App Paths, PATH search, elevacion, monitoring ni retry automatico tras comando enviado.

Prompt 17C implementa dispatch batch Master para `OPEN_APPLICATION`. Expone `POST /api/classrooms/{classroomId}/open-application`, protegido por `MasterAccessGuard`, con request estricta `applicationId + targetDeviceIds`. El Master valida `ApplicationDefinition` activa persistida y asociacion aula/aplicacion antes de crear batch, congela solo `applicationId`, persiste una unica `BatchOperation` `OPEN_APPLICATION`, reutiliza `MasterRemoteOperationGateway` con `OpenApplicationOperationParameters.applicationId` y preserva errores locales del Agent por Device. No modifica Agent, Session Agent, Protobuf, bindings, App Paths, Registry, filesystem, `CreateProcessW` ni UI.

Prompt 18A implementa `LOCK_INPUT`/`UNLOCK_INPUT` productivo del lado Client sin endpoint/batch Master. El Agent Service recibe `OperationRequest` tipado, pasa por `RemoteOperationDispatcher`, mantiene licencia comercial activa para `LOCK_INPUT` y aplica una excepcion estricta recovery-safe solo para `UNLOCK_INPUT`; luego envia Session Command v1 tipado. El Session Agent ejecuta `User32.dll BlockInput(BOOL)` desde un `WindowsInputBlockCoordinator` con worker dedicado lazy que conserva ownership del thread para lock/unlock, libera en shutdown acotado y no persiste estado. No hay UI, overlay, hooks, drivers, SendInput, shell, timeout configurable, retry automatico ni bloqueo de `CTRL+ALT+DEL`.

Prompt 18B1 agrega una autorizacion local read-only de proposito unico para que el Master Backend pueda solicitar un futuro `UNLOCK_INPUT` recovery aunque la Commercial License local del Master no este `ACTIVE`. El Agent Service suma `GET_MASTER_UNLOCK_AUTHORIZATION` en Local IPC v1, conserva `protocolVersion = 1`, valida Installation Identity, `master-binding.json`, `installationId` y SID real del caller Named Pipe, y no lee SID de payload, username, Administrators membership ni claims de licencia invalida. Java agrega `LocalAgentClient.getMasterUnlockAuthorization()` y `MasterUnlockAccessGuard.requireUnlockAuthorized()` como guard interno separado. No hay endpoint HTTP, BatchOperation, dispatch gRPC, UI, cache, writes ni cambios de Protobuf.

Prompt 18B2 agrega dispatch batch Master para `LOCK_INPUT` y `UNLOCK_INPUT`. Expone endpoints separados `POST /api/classrooms/{classroomId}/input-control/lock` y `POST /api/classrooms/{classroomId}/input-control/unlock`, con request estricta solo de `targetDeviceIds`. `lock` usa `MasterAccessGuard`; `unlock` usa `MasterUnlockAccessGuard`; ambos guards corren antes de leer Classroom, Devices, bindings, trust o SQLite escolar. El Master hace preflight tecnico por target con Device del aula, binding vigente, trust `PAIRED`, no `REVOKED`, conexion gRPC/mTLS `ONLINE` e `INPUT_CONTROL_V1`, persiste una unica `BatchOperation` antes del fanout y envia operaciones tipadas sin payload funcional. No modifica Agent, Session Agent, Protobuf, Local IPC, UI, overlay, status query, retry automatico ni lock state persistente.

Prompt 16A implementa el canal local seguro `Session Command v1` entre `GaltekClassroom.Agent.Service` y `GaltekClassroom.Agent.Session`. El Session Agent sirve un pipe por sesion interactiva (`GaltekClassroom.Agent.SessionCommand.v1.<sessionId>`) derivado de su `Process.SessionId`; el Service resuelve la sesion interactiva con API Windows, verifica el servidor por PID/sesion/ruta productiva antes de enviar y usa request/response tipado con framing de 16 KiB.

Prompt 13 implementa el primer transporte seguro Master-Client: contrato Protobuf v1, servicio gRPC `NetworkConnection.Connect`, TLS/mTLS obligatorio, certificados self-signed de corta vida ligados al trust por fingerprint SPKI, conexion persistente iniciada por el Client, heartbeat, estados `CONNECTING`/`ONLINE`/`OFFLINE` y reconexion con backoff. No redisena Prompt 12.

Prompt 9.6 formaliza el requisito futuro de cuentas Windows administradas en Clients. Cada PC de alumnos podra tener dos cuentas logicas, `PRIMARY` y `SECONDARY`, y el Master podra planificar una sola accion masiva para dejar un aula/grupo/seleccion en la cuenta objetivo con resultados `NO_CHANGE`, `SUCCESS`, `FAILED` y retry solo de fallidos.

El Master Backend Java sigue sin leer `master-binding.json` ni `network-identity.json`, no conoce sus rutas y no recalcula autorizacion local. Consume `GET_MASTER_AUTHORIZATION` por Local IPC v1 para autorizacion administrativa normal, mantiene publico `GET /api/master/authorization` para diagnostico y usa `MasterAccessGuard` en endpoints administrativos normales. La unica excepcion actual es `POST /api/classrooms/{classroomId}/input-control/unlock`, que consume `GET_MASTER_UNLOCK_AUTHORIZATION` mediante `MasterUnlockAccessGuard` para despachar solo `UNLOCK_INPUT`, sin exponer endpoint publico de autorizacion y sin fallback entre guards.

El producto todavia no tiene UI, mDNS, discovery real, captura, filesystem real, sync real, USB real, browser automation, wallpaper real, proyeccion real ni distribucion real. `LOGON_MANAGED_ACCOUNT` y `LOGOFF_WINDOWS_SESSION` existen como primitives remotas Agent/Master gateway para un Client individual; `SWITCH_MANAGED_ACCOUNT` ya tiene batch Master administrativo desde 19H1 mediante `POST /api/classrooms/{classroomId}/managed-accounts/switch` y retry administrativo explicito/selectivo desde 19H2 mediante `POST /api/operations/{operationId}/retry`; la UI sigue pendiente y la validacion real Credential Provider/logon/switch 19I2 sigue pendiente.

## Implementado

- Backend Master en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code` via IPC local al Agent Service.
- Endpoint `GET /api/master/authorization` via IPC local al Agent Service.
- `MasterAccessGuard.requireAuthorized()` en endpoints administrativos reales.
- Operacion Local IPC v1 `GET_MASTER_UNLOCK_AUTHORIZATION`, read-only, con respuesta minima `status`, `authorized`, `configured` para recovery-safe unlock.
- `MasterUnlockAccessGuard.requireUnlockAuthorized()` en Java como guard interno separado para acciones que reducen control; actualmente solo `UNLOCK_INPUT`.
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
- Endpoints protegidos de browser policies: listar/crear/patch/archive policies, listar/crear/patch/archive rules y resolver read-only de policy efectiva.
- `POST /api/classrooms/{classroomId}/browser-policies/apply` protegido, aplica la policy de navegacion efectiva persistida a Devices explicitamente seleccionados.
- Endpoints protegidos de browser download policies: listar/crear/patch/archive policies y resolver read-only de policy efectiva.
- `POST /api/classrooms/{classroomId}/browser-download-policies/apply` protegido, aplica la policy de descarga efectiva persistida a Devices explicitamente seleccionados.
- `POST /api/classrooms/{classroomId}/open-url` protegido, envia batch `OPEN_URL` a Devices explicitamente seleccionados despues de safety global y policy efectiva por target.
- `POST /api/classrooms/{classroomId}/open-application` protegido, envia batch `OPEN_APPLICATION` a Devices explicitamente seleccionados despues de validar `ApplicationDefinition` activa y asociacion Classroom/Application.
- `POST /api/classrooms/{classroomId}/input-control/lock` protegido por `MasterAccessGuard`, envia batch `LOCK_INPUT` a Devices explicitamente seleccionados.
- `POST /api/classrooms/{classroomId}/input-control/unlock` protegido por `MasterUnlockAccessGuard`, envia batch `UNLOCK_INPUT` recovery-safe a Devices explicitamente seleccionados.
- Credential Vault interno Java-only del Master en `credential-vault.dat`, separado de SQLite y sin endpoints HTTP.
- Bridge interno `ManagedCredentialProvisioningBridge` para provisionar una credential `WINDOWS_ACCOUNT` del vault hacia un Client explicito mediante `MasterRemoteOperationGateway`, sin HTTP ni BatchOperation.
- `managed-windows-accounts.json` en `<CommonApplicationData>\Galtek\Classroom\` como fuente de verdad local del Client para `PRIMARY`/`SECONDARY` -> Windows SID.
- `managed-windows-credentials.dat` en `<CommonApplicationData>\Galtek\Classroom\` como fuente de verdad cifrada DPAPI del Client para passwords Windows de `PRIMARY`/`SECONDARY`.
- Store local `IManagedWindowsAccountBindingStore`/`ManagedWindowsAccountBindingStore` con Load/List/Get/Add/Replace/Remove, escritura durable, verificacion posterior y fail closed.
- Store local `IManagedWindowsCredentialStore`/`ManagedWindowsCredentialStore` con GetStatus/Add/Replace/Remove/Acquire, escritura durable cifrada, verificacion posterior y fail closed.
- Protector `IManagedWindowsCredentialProtector`/`WindowsDpapiManagedWindowsCredentialProtector` con DPAPI CurrentUser bajo LocalSystem, `CRYPTPROTECT_UI_FORBIDDEN` y optional entropy por instalacion/slot.
- `ManagedWindowsCredentialLease` disposable para adquirir el secreto en buffer mutable UTF-16LE sin devolver string.
- Modelo `ManagedWindowsAccountBinding` con `accountId`, `windowsSid`, `accountReference`, `createdAtUtc` y `updatedAtUtc`, sin campos de password/credential/token.
- Operacion remota Agent-side `GET_WINDOWS_SESSION_STATE` registrada en `RemoteOperationDispatcher`.
- Operacion remota Agent-side `LOGON_MANAGED_ACCOUNT` registrada en `RemoteOperationDispatcher`.
- Resolver `IWindowsConsoleSessionResolver`/`WindowsConsoleSessionResolver` basado en consola fisica con WTS, `WTSQueryUserToken`, `TokenUser` y SID real.
- Servicio `WindowsSessionStateService` que compara SID activo contra los bindings locales `PRIMARY`/`SECONDARY`.
- Servicio `WindowsSessionLogonService` que crea activation remota efimera solo tras preflight de consola fisica, binding, SID User, credential DPAPI usable y revalidacion de `NO_SESSION`.
- Resultado Protobuf tipado `WindowsSessionStateResult.state`, sin SID, username, `accountReference` ni `sessionId`.
- Capability productiva `WINDOWS_SESSION_STATE_V1` anunciada por `ClientCapabilityProvider`.
- Capability productiva `WINDOWS_SESSION_LOGON_V1` anunciada por `ClientCapabilityProvider`.
- El documento de managed accounts incluye `schemaVersion = 1`, `installationId` y `bindings`; `installationId` distinto al actual produce `MANAGED_ACCOUNT_BINDINGS_INVALID`.
- Archivo ausente de managed accounts equivale a ambos slots `NOT_CONFIGURED`; no se crea en startup ni en list.
- Bind de managed accounts resuelve cuentas con APIs Windows nativas, canonicaliza por SID con lookup inverso y acepta solo `SidTypeUser`.
- Nombres no calificados como `Primaria` se transforman deterministamente a `<MACHINE>\Primaria` antes de resolver.
- PRIMARY y SECONDARY no pueden compartir SID; username igual recreado con SID nuevo no se adopta automaticamente.
- Status local de managed accounts: sin binding `NOT_CONFIGURED`; binding + SID User resoluble `CREDENTIAL_NOT_CONFIGURED`; SID no resoluble `ACCOUNT_NOT_FOUND`.
- CLI local administrativa de managed accounts: `--managed-account-list`, `--managed-account-bind <PRIMARY|SECONDARY> <WINDOWS_ACCOUNT>`, `--managed-account-remove <PRIMARY|SECONDARY>` y `--replace-managed-account-binding`.
- Mutaciones de managed accounts requieren consola elevada; no autoelevan y no aceptan parametros de password/credential/secret/token/PIN.
- ACL de `managed-windows-accounts.json`: `LocalSystem` y `Builtin Administrators` con `FullControl`; usuarios normales sin read/write explicito.
- ACL de `managed-windows-credentials.dat`: `LocalSystem` y `Builtin Administrators` con `FullControl`; usuarios normales y `Authenticated Users` sin read/write explicito.
- Modelo `CredentialVaultEntry` con `credentialId`, `credentialType`, `displayName`, `loginIdentifier`, `password`, `createdAtUtc` y `updatedAtUtc`.
- Tipos de credencial de vault soportados: `WINDOWS_ACCOUNT` y `GOOGLE_ACCOUNT`.
- Crypto de vault: PBKDF2-HMAC-SHA256 con salt/work factor versionados, DEK aleatorio de 256 bits, AES-256-GCM para wrapped DEK y AES-256-GCM para documento cifrado completo.
- `CredentialVaultService.initialize(masterPassword)` crea la boveda explicitamente; archivo ausente devuelve `CREDENTIAL_VAULT_NOT_INITIALIZED` y no se crea durante startup.
- `unlock(masterPassword)` valida el vault completo y crea una unica sesion temporal; password incorrecto devuelve `CREDENTIAL_VAULT_UNLOCK_FAILED`.
- Sesiones de vault: token aleatorio en memoria, una activa como maximo, invalidada por nuevo unlock, lock explicito, restart o expiracion lazy de 5 minutos.
- `list(sessionToken)` devuelve metadata descifrada sin password; `reveal(sessionToken, credentialId)` devuelve solo el password solicitado.
- `add`, `update`, `remove` y `changeMasterPassword` requieren sesion valida; `changeMasterPassword` re-wrappea el DEK y no re-encripta entries si no cambian.
- `LOCK_INPUT` y `UNLOCK_INPUT` productivos Agent-side llegan por `OperationRequest` tipado y no transportan payload funcional.
- `INPUT_CONTROL_V1` se anuncia en `ClientHello` desde el Agent.
- `LockInputOperationHandler` y `UnlockInputOperationHandler` envian solo Session Command tipado; el Agent Service nunca llama `BlockInput`.
- Session Command v1 soporta `LOCK_INPUT` y `UNLOCK_INPUT` sin payload, sin key list, keyboardOnly, mouseOnly, duration, timeout, message, command, shell ni argumentos.
- `WindowsInputBlockCoordinator` en Session Agent usa un worker dedicado lazy para llamar `BlockInput(TRUE)` y `BlockInput(FALSE)` en el mismo Managed Thread, procesar locks repetidos en el owner thread y terminar tras unlock.
- Input control normal desbloqueado agrega 0 threads extra, 0 timers, 0 polling, 0 hooks, 0 scans y 0 writes.
- `UNLOCK_INPUT` sin lock activo es idempotente y exitoso; con lock activo desbloquea desde el owner thread y termina el worker.
- Si `BlockInput(FALSE)` falla se reporta `INPUT_UNLOCK_FAILED`, pero el worker termina igualmente para favorecer el fail-safe de Windows por salida de thread/proceso.
- `CTRL+ALT+DEL` queda documentado como escape nativo de Windows; Galtek no bloquea Secure Attention Sequence, Task Manager ni Winlogon.
- `LOCK_INPUT` exige Commercial License `ACTIVE`; `UNLOCK_INPUT` no se bloquea por estado de licencia, pero conserva mTLS, trust `PAIRED`, no `REVOKED`, Device correcto y Session Command autenticado.
- `application-bindings.json` en `<CommonApplicationData>\Galtek\Classroom\` es la fuente de verdad local del Client para `applicationId -> launch target`.
- `OPEN_APPLICATION` productivo Agent-side llega por `OperationRequest` tipado y usa `OpenApplicationOperationParameters.applicationId` como unico input funcional remoto.
- `OPEN_APPLICATION_V1` se anuncia en `ClientHello` desde el Agent.
- `OpenApplicationOperationHandler` valida `applicationId`, catalogo, binding existente, `enabled` y estructura; para `ABSOLUTE_EXE` comprueba existencia puntual antes del Session Command.
- Session Command v1 soporta `OPEN_APPLICATION` con `openApplication.applicationId` solamente; no transporta path, command line, argumentos, working directory, shell, URI, shortcut ni environment.
- El Session Agent resuelve aplicaciones read-only/on-demand con `SessionApplicationResolver`, releyendo y validando `application-bindings.json` en cada `OPEN_APPLICATION`.
- `APP_PATHS` en launch se resuelve solo por HKLM App Paths, valor default, Registry64/Registry32 cuando aplica; no usa HKCU, PATH, Program Files, Start Menu, WindowsApps, uninstall keys, procesos ni scans.
- `ABSOLUTE_EXE` se revalida en el Session Agent como ruta Windows local absoluta `.exe` y se verifica con `File.Exists` justo antes de lanzar.
- `WindowsApplicationLauncher` usa `CreateProcessW` con `lpApplicationName` absoluto, `lpCommandLine = null`, sin argumentos, sin handles heredados y working directory del parent del executable; cierra handles de process/thread tras success.
- `OPEN_APPLICATION SUCCESS` significa solo que Windows acepto crear el proceso; no hay foreground guarantee, app monitoring, PID tracking, `WaitForExit`, instancia unica ni dedupe por proceso.
- Store local `IApplicationBindingStore`/`ApplicationBindingStore` con Load/Get/List/Add/Replace/SetEnabled/Remove, escritura durable, verificacion posterior y fail closed.
- Launch types locales de aplicaciones soportados en el Agent: `APP_PATHS` y `ABSOLUTE_EXE`.
- CLI local administrativa de application bindings: `--application-bind-list`, `--application-bind-exe`, `--application-bind-app-path`, `--application-bind-disable`, `--application-bind-enable`, `--application-bind-remove` y `--replace-application-binding`.
- Mutaciones de application bindings requieren elevacion administrativa; list/read-only no muta ni autoeleva.
- `APP_PATHS` valida solo nombre `.exe` sin path, comillas, espacios, control chars ni argumentos.
- `ABSOLUTE_EXE` valida ruta Windows local absoluta `.exe`, no UNC, no relativa, sin `..`, ADS, control chars, comillas, argumentos, wildcards ni placeholders; al crear/reemplazar verifica existencia puntual.
- Corrupcion de `application-bindings.json`, schema desconocido, duplicados o campos incompatibles producen `APPLICATION_BINDINGS_INVALID` sin regenerar ni adoptar parcialmente.
- Errores remotos de aplicacion vigentes: `APPLICATION_BINDINGS_INVALID`, `APPLICATION_BINDING_NOT_FOUND`, `APPLICATION_BINDING_INVALID`, `APPLICATION_DISABLED`, `APPLICATION_EXECUTABLE_NOT_FOUND` y `APPLICATION_LAUNCH_FAILED`.
- `BrowserAccessPolicy` persiste `mode`, `scopeType`, target `CLASSROOM`/`GROUP`/`DEVICE`, `accountScope` `ANY`/`PRIMARY`/`SECONDARY`, `active`, `version` y timestamps UTC.
- `BrowserUrlRule` persiste `ALLOW`/`BLOCK`, `HOST_EXACT`/`HOST_SUFFIX`/`URL_PREFIX`/`EXACT_URL`, pattern canonico, enabled, descripcion opcional y timestamps UTC.
- `BrowserPolicyPrecedenceResolver` selecciona una sola policy efectiva: `DEVICE` cuenta especifica, `DEVICE ANY`, `GROUP` cuenta especifica, `GROUP ANY`, `CLASSROOM` cuenta especifica, `CLASSROOM ANY`, o `UNRESTRICTED` implicito.
- `BrowserDownloadPolicy` persiste `restrictionMode`, `scopeType`, target `CLASSROOM`/`GROUP`/`DEVICE`, `accountScope` `ANY`/`PRIMARY`/`SECONDARY`, `active`, `version` y timestamps UTC.
- `BrowserDownloadPolicyPrecedenceResolver` selecciona una sola policy efectiva con la misma precedencia de navegacion, o `NO_SPECIAL_RESTRICTIONS` implicito.
- Restriction modes de descarga implementados en Master: `NO_SPECIAL_RESTRICTIONS`, `BLOCK_DANGEROUS`, `BLOCK_POTENTIALLY_DANGEROUS`, `BLOCK_ALL` y `BLOCK_MALICIOUS`.
- Dispatch Master de browser policies acepta solo `targetDeviceIds`; rechaza campos como `policyId`, rules, `accountType`, URLs, browser, commands, registry paths, timeouts o payload arbitrario.
- Dispatch Master de browser policies usa `accountType = null`, por lo que resuelve solo policies `ANY`; no infiere `PRIMARY` ni `SECONDARY`.
- Dispatch Master de browser policies deriva `groupId` desde assignment actual `Student -> Device -> Student.groupId` y congela parametros Protobuf tipados antes del fanout.
- Dispatch Master de navegacion bloquea targets cuya policy efectiva contiene `EXACT_URL` habilitado con `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`, sin enviar request al Agent.
- Dispatch Master de descarga conserva la diferencia entre ausencia de policy efectiva (`implicit_no_special_restrictions = true`) y policy explicita `NO_SPECIAL_RESTRICTIONS`.
- Dispatch Master de `OPEN_URL` acepta solo `url` y `targetDeviceIds`, rechaza campos extra, conserva la URL original aceptada para el Agent y usa la normalizacion solo para safety/evaluacion.
- Dispatch Master de `OPEN_URL` evalua `EXACT_URL` directamente mediante `BrowserNavigationPolicyEvaluator`; un target bloqueado queda `URL_BLOCKED_BY_POLICY`, no `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`.
- Dispatch Master de `OPEN_URL` distingue `OPERATION_RESULT_UNKNOWN` (Master envio al Agent sin resultado) de `SESSION_COMMAND_RESULT_UNKNOWN` (Agent envio al Session Agent sin confirmar respuesta) y no hace retry automatico.
- SQLite V5 permite persistir `BatchOperation` de `APPLY_BROWSER_NAVIGATION_POLICY` y `APPLY_BROWSER_DOWNLOAD_POLICY`.
- SQLite V1/V5 ya permiten persistir `BatchOperation` `OPEN_URL`; no se agrego migracion V6 artificial.
- Protobuf v1 agrega `APPLY_BROWSER_DOWNLOAD_POLICY` como operacion remota distinta de `APPLY_BROWSER_NAVIGATION_POLICY`, con parametros tipados `ApplyBrowserDownloadPolicyOperationParameters`.
- Protobuf v1 agrega enum tipado de descarga equivalente a `NO_SPECIAL_RESTRICTIONS`, `BLOCK_DANGEROUS`, `BLOCK_POTENTIALLY_DANGEROUS`, `BLOCK_ALL` y `BLOCK_MALICIOUS`, conservando valor `UNSPECIFIED`.
- `ChromiumDownloadPolicyCompiler` es puro y mapea `NO_SPECIAL_RESTRICTIONS -> 0`, `BLOCK_DANGEROUS -> 1`, `BLOCK_POTENTIALLY_DANGEROUS -> 2`, `BLOCK_ALL -> 3` y `BLOCK_MALICIOUS -> 4`.
- `ChromiumDownloadPolicyCompiler` distingue `NO_SPECIAL_RESTRICTIONS` implicito (`RemoveGaltekPolicy = true`, sin valor nativo) de policy explicita `NO_SPECIAL_RESTRICTIONS` (`RemoveGaltekPolicy = false`, valor nativo `0`).
- `ChromiumDownloadPolicyCompiler` genera content hash determinista sin timestamps, nombres visibles, SID ni rutas Registry, y diferencia removal implicito de valor explicito 0.
- `BROWSER_DOWNLOAD_POLICY_V1` es capability productiva anunciada por `ClientCapabilityProvider`.
- `ApplyBrowserDownloadPolicyOperationHandler` esta registrado para `APPLY_BROWSER_DOWNLOAD_POLICY`.
- El Agent aplica `DownloadRestrictions` user-scope en Chrome y Edge bajo `HKEY_USERS\<SID>` del usuario interactivo real, no `HKCU` desde LocalSystem ni HKLM.
- Descargas usa durable state `browser-download-policy-state.json` y journal `browser-download-policy-apply.json`, separados de `browser-navigation-policy-state.json` y `browser-navigation-policy-apply.json`.
- Download policy endurece solo la parent key Chrome/Edge cuando es seguro; no hay ACL por value y no hay rewrite recursivo de child subkeys.
- `NO_SPECIAL_RESTRICTIONS` implicito remueve solo policy Galtek-owned; `NO_SPECIAL_RESTRICTIONS` explicito escribe `REG_DWORD 0`.
- Errores download-specific vigentes: `BROWSER_DOWNLOAD_POLICY_INVALID`, `BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT`, `BROWSER_DOWNLOAD_POLICY_APPLY_FAILED`, `BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED` y `BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED`.
- SQLite V4 crea `browser_download_policies` con checks de enum/scope, indices unicos parciales por target/account activo y triggers para impedir `GROUP`/`DEVICE` de otro classroom.
- No existen `blockedExtensions`, `allowedExtensions`, `blockedMimeTypes` ni `allowedMimeTypes` en el modelo persistente de descargas.
- `BrowserNavigationPolicyEvaluator` aplica safety estructural primero; gana el filtro mas especifico por host, scheme/port, path y query; solo ante igual especificidad `ALLOW` gana a `BLOCK`; conserva defaults `BLOCKLIST`/`ALLOWLIST` y decision estructurada con reason code.
- `BrowserUrlNormalizer` acepta solo URL absoluta segura `http`/`https`, host obligatorio, sin userinfo/control chars, host lowercase, trailing dot removido, puertos default normalizados, path vacio como `/` y fragment eliminado.
- Migracion SQLite `V3__add_browser_navigation_policies.sql` crea `browser_access_policies` y `browser_url_rules` con checks e indices unicos parciales para una policy activa por target/account.
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
- `ManagedAccountSwitchPlanner` puro para decidir `NO_CHANGE`, `LOGON`, `SWITCH` o `BLOCKED` desde estado de sesion Windows real.
- Operaciones tipadas de sesion Windows `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`; el batch Master existente usa snapshot read-only y mutation `SWITCH_MANAGED_ACCOUNT(target)`.
- `TargetExecutionStatus.NO_CHANGE` tratado como exito no retryable en `BatchOperation`.
- Errores estructurados para cuentas/sesion Windows administrada: `ACCOUNT_NOT_CONFIGURED`, `MANAGED_CREDENTIAL_NOT_CONFIGURED`, `WINDOWS_SESSION_UNKNOWN`, `WINDOWS_LOGON_FAILED`, `WINDOWS_LOGOFF_FAILED`, `SESSION_SWITCH_FAILED` y `CREDENTIAL_PROVIDER_UNAVAILABLE`.
- Contratos compartidos C# para operaciones, destinos logicos, estrategias de asignacion, preparacion, workspace, proyeccion, prioridades, tipos de cuenta, estados de sesion y acciones de switch administrado.
- Local IPC API v1 read-only sobre Windows Named Pipes.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`, `GET_MASTER_UNLOCK_AUTHORIZATION`, `GET_RUNTIME_DIAGNOSTICS`.
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
- `OpenUrlOperationHandler` es handler remoto explicito para `OPEN_URL`; no abre navegador, no usa `Process.Start`, `cmd`, PowerShell, scripts, WMI shell ni `CreateProcessAsUser`. Desde 16D valida safety estructural primero y despues la policy Galtek aplicada localmente al usuario interactivo; si la policy bloquea devuelve `URL_BLOCKED_BY_POLICY` sin enviar Session Command.
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
- Capabilities productivas conocidas actuales: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`, `POWER_CONTROL_V1`, `OPEN_URL_V1`, `OPEN_APPLICATION_V1`, `BROWSER_NAVIGATION_POLICY_V1`, `BROWSER_DOWNLOAD_POLICY_V1`, `INPUT_CONTROL_V1`, `WINDOWS_SESSION_STATE_V1`, `WINDOWS_SESSION_LOGON_V1`, `MANAGED_CREDENTIAL_PROVISIONING_V1`, `WINDOWS_SESSION_LOGOFF_V1` y `WINDOWS_SESSION_SWITCH_V1`; capabilities desconocidas se ignoran y no autorizan.
- `RemoteOperationDispatcher` del Agent deduplica por `operationId`, incluye parametros tipados al detectar conflicto de duplicado, aplica timeout, rechaza licencia comercial no activa antes de handler salvo `UNLOCK_INPUT` recovery-safe y devuelve `OPERATION_NOT_IMPLEMENTED` para cualquier operacion sin handler.
- `ShutdownOperationHandler`, `RestartOperationHandler`, `OpenUrlOperationHandler`, `OpenApplicationOperationHandler`, `LockInputOperationHandler`, `UnlockInputOperationHandler`, `ApplyBrowserPolicyOperationHandler`, `ApplyBrowserDownloadPolicyOperationHandler`, `GetWindowsSessionStateOperationHandler`, `ProvisionManagedCredentialOperationHandler`, `LogoffWindowsSessionOperationHandler`, `LogonManagedAccountOperationHandler` y `SwitchManagedAccountOperationHandler` son handlers tipados explicitos; no existe handler generico de comandos.
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

- Prompt 16F2 cerrado tecnicamente.
- No queda desarrollo 16F2 a medias.

## Pendiente inmediato

- UI futura para diagnosticar/configurar binding sin convertirse en autoridad.
- IPC write futuro solo cuando exista un diseno de autorizacion local adecuado.
- Mantener cualquier nuevo endpoint administrativo bajo `MasterAccessGuard`.
- Implementar filesystem real de StudentWorkspace y recovery en fases posteriores.
- Implementar sync real, USB real y distribucion real en fases posteriores sin romper la regla `SYNC -> VERIFY -> COMMIT CANONICAL -> CONFIRM`.
- Implementar preview/captura/proyeccion real en fases posteriores distinguiendo modos y costos.
- Implementar scheduler/backpressure real, medicion con profiling real y deteccion conservadora de perfil en fases posteriores solo con evidencia.
- Implementar workflows reales de workspace/sync en fases posteriores.
- Implementar mDNS/discovery real y exponer flujos reales de pairing/discovery sobre red sin convertir discovery en trust.
- Implementar comandos administrativos remotos restantes en fases posteriores sobre el transporte seguro.
- Implementar posteriormente entrega autorizada por maestra mediante canal Galtek tipado/controlado hacia destinos logicos de `StudentWorkspace`, no desbloqueando temporalmente el browser.
- Prompt 14.5A, 14.5B, 14.5C y 14.5D quedan cerrados.
- Empaquetar la llave publica real de Galtek Hub para produccion.

## Cambios aceptados

- Agent Service decide Master authorization; Java solo consume resultado derivado.
- El SID enviado por JSON, HTTP, UI o payload IPC no se considera prueba.
- La autorizacion local Master falla cerrado ante Agent down, binding invalido, licencia no activa, falta de rol `MASTER`, mismatch de instalacion o SID distinto.
- Otro administrador Windows no hereda acceso Master si su SID no esta ligado.
- Rebinding requiere `--replace-master-binding`.
- El Master envia `applicationId`, nunca rutas ejecutables.
- `ApplicationDefinition` del Master no equivale a `ApplicationBinding` del Client.
- El binding fisico local de aplicaciones vive en `application-bindings.json` y se configura solo localmente por administrador.
- Reemplazar un binding de aplicacion requiere `--replace-application-binding`.
- Un binding de aplicacion disabled se conserva; una futura operacion de launch debe devolver `APPLICATION_DISABLED` sin lanzar.
- `APP_PATHS` y `ABSOLUTE_EXE` son los unicos launch types locales de aplicaciones en 17A.
- `application-bindings.json` corrupto o incompatible falla cerrado como `APPLICATION_BINDINGS_INVALID`.
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
- El framework de operaciones remotas queda tipado y deduplicado por `operationId`; `ShutdownOperationHandler`, `RestartOperationHandler`, `OpenUrlOperationHandler`, `OpenApplicationOperationHandler`, `LockInputOperationHandler`, `UnlockInputOperationHandler`, `ApplyBrowserPolicyOperationHandler`, `ApplyBrowserDownloadPolicyOperationHandler`, `GetWindowsSessionStateOperationHandler`, `ProvisionManagedCredentialOperationHandler`, `LogoffWindowsSessionOperationHandler`, `LogonManagedAccountOperationHandler` y `SwitchManagedAccountOperationHandler` son handlers productivos actuales en el Agent, y las operaciones que continuan sin handler devuelven `OPERATION_NOT_IMPLEMENTED`.
- Power control del Agent usa API nativa Windows, no shell ni procesos externos.
- `SHUTDOWN` y `RESTART` habilitan explicitamente `SeShutdownPrivilege`, usan countdown fijo inicial de 10 segundos y no fuerzan cierre de aplicaciones.
- `OperationResult SUCCESS` en power control significa que Windows acepto la solicitud, no que el equipo ya desaparecio de la red.
- `POWER_CONTROL_UNAVAILABLE` no es retryable; `POWER_CONTROL_FAILED` representa fallo operacional potencialmente transitorio.
- `students/batch` permite parcialidad por fila; un alumno invalido no cancela los demas.
- `assignments/batch` preflight completo antes de writes; `TARGET_OCCUPIED` no reemplaza automaticamente.
- El Master no almacena passwords de cuentas Windows administradas en `classroom.db` ni los envia en comandos normales.
- La UI no recibe passwords por defecto; la unica excepcion futura sera Credential Vault Reveal explicito despues de `MasterAccessGuard`, vault unlock valido y sesion de vault no expirada, entregando solo una credencial.
- Las passwords Google escolares pueden almacenarse solo dentro de Credential Vault cifrado, nunca en BrowserProfile, SQLite, logs, Cookies, Login Data o Local State.
- `MasterUnlockAccessGuard` no autoriza Credential Vault; la excepcion recovery-safe de `UNLOCK_INPUT` no aplica a passwords.
- Los comandos de cuentas administradas envian solo `accountId` logico (`PRIMARY`/`SECONDARY`).
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
- Browser policy administrativa queda separada de safety estructural de `OPEN_URL`; una rule `ALLOW` nunca autoriza `file:`, `javascript:`, `data:` ni otros esquemas inseguros.
- La semantica vigente de browser policy ya no es "`ALLOW` siempre gana": gana el filtro mas especifico por host, scheme/port, path y query; solo ante igual especificidad `ALLOW` gana a `BLOCK`.
- `EXACT_URL` se persiste en Master, pero 16D lo rechaza como `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE` al aplicar porque Chromium native policy no ofrece equivalencia byte-for-byte general segura.
- `accountScope = ANY` aplica al usuario interactivo actual; `PRIMARY`/`SECONDARY` siguen sin integrarse a browser policy aunque ya exista binding local seguro a Windows SID.
- `PRIMARY` y `SECONDARY` no implican restriccion automatica; una policy por cuenta existe solo si el administrador la crea explicitamente.
- Si el contexto no conoce cuenta administrada, solo aplican policies `ANY`; no se infiere `PRIMARY`.
- Las politicas de descarga de navegador quedan separadas de las politicas de navegacion. `DownloadRestrictions` no se agrega a `BrowserAccessPolicy`.
- `NO_SPECIAL_RESTRICTIONS` significa que Galtek no agrega restricciones especiales de descarga; no significa desactivar Safe Browsing ni toda seguridad del navegador.
- En Windows no se modela bloqueo administrable arbitrario por extension/MIME para Chrome/Edge hasta contar con un mecanismo comun soportado y enforceable.
- Bajo `BLOCK_ALL`, una futura descarga autorizada por maestra debe entregarse por canal Galtek controlado hacia `StudentWorkspace`, no abriendo ventanas temporales de descarga en el navegador.
- Las prioridades operacionales deben impedir que transferencias grandes, thumbnails o inventario bloqueen operaciones `CRITICAL`.
- Power loss, reboot abrupto, kill del proceso y boot storm son condiciones normales de diseno.
- Startup rapido del plano de control tiene prioridad sobre licencia comercial completa, WMI costoso, inventario, thumbnails, captura, proyeccion, transferencias grandes y filesystem sync.
- El Master queda control-plane ready con proceso vivo y storage listo, aunque haya 0 Clients online.
- SQLite conserva su recovery propio: no borrar ni recrear `classroom.db`, WAL ni SHM por marker de shutdown no limpio.
- Los markers de ejecucion se escriben al inicio y se eliminan en cierre limpio; no son heartbeat persistente ni deben producir writes periodicos.
- Clasificacion futura de durabilidad: `EPHEMERAL`, `NORMAL` y `CRITICAL_DURABLE`.
- Boot, Session Agent startup, `ClientHello`, pairing, registration, reconnect, heartbeat y `DEVICE_ONLINE` no inician captura/proyeccion/thumbnails/sync/inventario pesado automaticamente.
- Una operacion remota sin ACK o sin resultado confirmado no es `SUCCESS`; debe conservarse como incertidumbre y solo reconciliarse cuando exista un mecanismo seguro y explicitamente soportado para ese tipo de operacion.
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
- No aceptar executable paths, comandos, argumentos, shell, PowerShell, `cmd`, scripts, shortcuts, MSI ni URI arbitraria desde el Master para abrir aplicaciones.
- No implementar `OPEN_APPLICATION` real sin resolver antes `applicationId` contra el catalogo local seguro del Client.
- No agregar auto-discovery, Program Files scans, Start Menu scans, Registry polling, WMI, process scans, filesystem watchers ni heartbeat data para application bindings.
- No crear endpoint HTTP `GET /passwords`, reveal masivo, export de passwords, reset destructivo de vault, Client credential store, provisioning remoto ni Google browser automation como parte de 19A.
- No borrar working copies locales para completar sync o move.
- No modelar YouTube como screen share obligatorio.
- No hacer commits automaticamente.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- No existe todavia una llave publica real de Galtek Hub empaquetada; si falta llave publica, la licencia queda en `LICENSE_KEY_NOT_CONFIGURED` y Master no autoriza.
- `license.dat` no se cifra localmente en esta fase.
- `classroom.db` no tiene cifrado at-rest, backup/restore automatico ni politica de retencion/borrado seguro de PII.
- `master-network-identity.key` y `master-network-identity.protector` son almacenamiento separado y cifrado minimo para desarrollo/local; no son hardening productivo final.
- ACL de `credential-vault.dat` es hardening best-effort encapsulado; la confidencialidad principal depende de master password + crypto.
- Los certificados TLS actuales son self-signed de corta vida emitidos en memoria desde Network Identity; falta ciclo de vida productivo de certificados y rotacion operacional.
- El Credential Provider artifact de 19I1 queda `NOT_SIGNED`; Authenticode con certificado real sigue siendo gate pendiente antes de distribucion comercial externa.
- La validacion productiva con Service Control Manager, Task Scheduler y CLI elevada depende de ejecutar en un entorno con permisos administrativos.
- La creacion real de la llave CNG de Network Identity requiere el contexto del Service como `LocalSystem` o una consola elevada; una prueba manual desde shell no elevado devuelve acceso denegado.
- No se creo una segunda cuenta Windows para prueba manual de SID distinto; ese caso queda cubierto por tests automatizados.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~MasterNetworkTransport|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~OperationContracts"` en `agent`: correcto, 54 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 70 pruebas Session y 620 pruebas Service superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- Parser PowerShell sobre scripts nuevos/modificados de Credential Provider y orchestrators: correcto.
- `.\installer\windows\publish-credential-provider.ps1`: correcto; compila `Release|x64`, genera package limpio y reporta `Authenticode: NOT_SIGNED`.
- `.\installer\windows\test-credential-provider-package.ps1`: correcto; manifest, CLSID, x64, SHA-256, packageId y Authenticode verificados sin elevacion.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj`: correcto, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.
- `dumpbin /dependents` sobre `artifacts/windows/credential-provider/GaltekClassroom.CredentialProvider.dll`: dependencias `ole32.dll`, `ADVAPI32.dll`, `Secur32.dll`, `KERNEL32.dll`; sin `VCRUNTIME*.dll`, `MSVCP*.dll` ni dependencia .NET.
- `.\installer\windows\publish-agent.ps1`: correcto; publica Service, Session Agent y Credential Provider.
- Package final `artifacts/windows/credential-provider/`: contiene solo `GaltekClassroom.CredentialProvider.dll` y `credential-provider.manifest.json`.
- `mvn -q "-Dtest=ManagedAccountSwitchDispatchControllerTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=ManagedAccountSwitchDispatchControllerTest,MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -Ddebug=false "-Dtest=ManagedAccountSwitchDispatchControllerTest,ManagedAccountSwitchPlannerTest,BatchOperationPlannerTest,MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q test` en `master-backend`: correcto, 338 pruebas superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q "-Dtest=ManagedAccountSwitchPlannerTest,ManagedAccountSwitchDispatchControllerTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=MasterSqlitePersistenceIntegrationTest,BrowserDownloadPolicyPersistenceIntegrationTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q test` en `master-backend`: correcto, 330 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionSwitch|FullyQualifiedName~OperationContracts|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 79 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionSwitch|FullyQualifiedName~WindowsSessionLogoff|FullyQualifiedName~WindowsSessionLogon|FullyQualifiedName~WindowsSessionState|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~OperationContracts|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 147 pruebas Service superadas; Session sin coincidencias.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~CredentialProviderBridge|FullyQualifiedName~CredentialProviderActivation|FullyQualifiedName~ManagedWindowsCredential"` en `agent`: correcto, 73 pruebas Service superadas; Session sin coincidencias.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider/GaltekClassroom.CredentialProvider.vcxproj`: correcto, 0 advertencias, 0 errores.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj`: correcto, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `rg` dirigido sobre provider/bridge/scripts para `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogonW`, `DefaultPassword`, `WinStation`, `SendKeys`, PowerShell/`cmd`, WinHTTP/WinINet/sockets, `ICredentialProviderFilter` y password logging: sin coincidencias productivas; solo aparece la asercion del self-test que confirma que Galtek no implementa `ICredentialProviderFilter`.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~CredentialProviderBridge"` en `agent`: correcto, 24 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider/GaltekClassroom.CredentialProvider.vcxproj`: correcto, 0 advertencias, 0 errores.
- MSBuild Release x64 de `agent/native/GaltekClassroom.CredentialProvider.Tests/GaltekClassroom.CredentialProvider.Tests.vcxproj`: correcto, 0 advertencias, 0 errores.
- `agent/native/GaltekClassroom.CredentialProvider.Tests/x64/Release/GaltekClassroom.CredentialProvider.Tests.exe`: correcto, `Credential Provider self-test passed`.
- `rg` dirigido sobre provider/bridge/scripts para `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogon`, `DefaultPassword`, `WinStation`, `SendKeys`, `LsaLogonUser`, `KERB_INTERACTIVE_UNLOCK_LOGON`, WinHTTP/WinINet/WSA/socket y `ICredentialProviderFilter`: sin coincidencias en codigo productivo 19G1.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionLogoff|FullyQualifiedName~WindowsSessionState|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~OperationContracts|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 93 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q "-Dtest=ManagedCredentialProvisioningBridgeTest" test` en `master-backend`: correcto, bridge interno, autorizacion, vault validation, tipo `WINDOWS_ACCOUNT`, UTF-16LE, limpieza de bytes, propagation de outcomes y no retry superados.
- `mvn -q "-Dtest=ManagedCredentialProvisioningBridgeTest,CredentialVaultServiceTest" test` en `master-backend`: correcto, regresion dirigida del bridge y Credential Vault superada.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ProvisionManagedCredential|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~ManagedWindowsCredential|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 100 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ManagedWindowsCredential|FullyQualifiedName~ManagedWindowsAccount|FullyQualifiedName~AgentCommandLineTests|FullyQualifiedName~OperationContractsTests"` en `agent`: correcto, 112 pruebas Service y 6 pruebas Session superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~WindowsSessionState|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport|FullyQualifiedName~ManagedWindowsAccountBindingStore"` en `agent`: correcto, 81 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ManagedWindowsAccount|FullyQualifiedName~WindowsAccountResolver|FullyQualifiedName~AgentCommandLineTests|FullyQualifiedName~OperationContractsTests|FullyQualifiedName~MasterBindingConfigurationServiceTests"` en `agent`: correcto, 84 pruebas Service y 6 pruebas Session sin coincidencia funcional superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=CredentialVaultServiceTest" test` en `master-backend`: correcto, pruebas dirigidas de init, no plaintext, unlock, tamper AES-GCM, corrupcion, sesiones lazy, CRUD, reveal, cambio de master password, no secret leak y performance conceptual superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~MasterAuthorizationServiceTests|FullyQualifiedName~LocalIpcRequestHandlerTests|FullyQualifiedName~SessionCommandProtocolTests|FullyQualifiedName~LocalIpcFramingTests|FullyQualifiedName~LocalIpcServerTests"` en `agent`: correcto, 58 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `mvn -q "-Dtest=MasterUnlockAccessGuardTest,MasterAccessGuardTest,WindowsNamedPipeLocalAgentClientTest,LocalIpcFramingTest" test` en `master-backend`: correcto.
- `mvn -q "-Dtest=InputControlDispatchControllerTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto, rutas, request estricta, split de guards, preflight, batch/fanout, mapping de errores de input y ausencia de status query superados.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~InputBlock|FullyQualifiedName~InputControl|FullyQualifiedName~SessionCommand|FullyQualifiedName~RemoteOperationDispatcher|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 35 pruebas Session y 84 pruebas Service superadas.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest,MasterNetworkTransportTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ApplicationBinding|FullyQualifiedName~OpenApplication|FullyQualifiedName~SessionCommand|FullyQualifiedName~OperationContracts|FullyQualifiedName~ClientCapabilityProvider|FullyQualifiedName~MasterNetworkTransport"` en `agent`: correcto, 20 pruebas Session y 105 pruebas Service superadas.
- `mvn -q "-Dtest=OpenApplicationDispatchControllerTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto, request estricta, autorizacion de app, preflight, batch/fanout, mapping de errores de aplicacion, incertidumbre y ausencia de status query `OPEN_APPLICATION` superados.
- `mvn -q "-Dtest=MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto; Flyway sigue con 5 migraciones y `OPEN_APPLICATION` ya era valido en el CHECK.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~ApplicationBinding|FullyQualifiedName~AgentCommandLineTests"` en `agent`: correcto, 47 pruebas Service y 6 pruebas Session sin coincidencia funcional superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=OpenUrlDispatchControllerTest,MasterRemoteOperationGatewayTest,BrowserNavigationPolicyEvaluatorTest" test` en `master-backend`: correcto, pruebas dirigidas de request/safety, policy, preflight, dispatch `OPEN_URL`, gateway tipado y evaluator superadas.
- `mvn -q "-Dtest=BrowserPolicyDispatchControllerTest,OpenUrlDispatchControllerTest,MasterRemoteOperationGatewayTest,BrowserNavigationPolicyEvaluatorTest" test` en `master-backend`: correcto, regresion dirigida 16F1 + 16F2 superada.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~BrowserDownloadPolicyAgentTests|FullyQualifiedName~BrowserPolicyAgentTests|FullyQualifiedName~OperationContractsTests|FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 86 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto, mapping de errores remotos de descarga validado.
- `mvn -q "-Dtest=BrowserPolicyDispatchControllerTest,BrowserPolicyControllerTest,BrowserDownloadPolicyControllerTest,BrowserPolicyPrecedenceResolverTest,BrowserDownloadPolicyPrecedenceResolverTest,BrowserDownloadPolicyPersistenceIntegrationTest,MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto, tests dirigidos 16F1/API/resolvers/migracion/gateway superados.
- `mvn -q test` en `master-backend`: correcto, 222 pruebas superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~BrowserPolicyAgentTests|FullyQualifiedName~OperationContractsTests|FullyQualifiedName~MasterNetworkTransportTests"` en `agent`: correcto, 59 pruebas Service superadas; el proyecto Session no tuvo coincidencias con el filtro.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `mvn -q "-Dtest=MasterRemoteOperationGatewayTest" test` en `master-backend`: correcto.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q "-Dtest=BrowserDownloadPolicyPrecedenceResolverTest,BrowserDownloadPolicyPersistenceIntegrationTest,BrowserDownloadPolicyControllerTest" test` en `master-backend`: correcto, pruebas dirigidas de resolver, V4/repository y API 16E1 superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `mvn -q "-Dtest=BrowserNavigationPolicyEvaluatorTest,BrowserPolicyPrecedenceResolverTest,BrowserPolicyControllerTest,MasterSqlitePersistenceIntegrationTest" test` en `master-backend`: correcto, pruebas dirigidas de evaluador, resolver, API y persistencia SQLite superadas.
- `mvn -q "-Dtest=BrowserNavigationPolicyEvaluatorTest,BrowserPolicyPrecedenceResolverTest,BrowserPolicyControllerTest" test` en `master-backend`: correcto, pruebas dirigidas browserpolicy 16D superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln --filter "FullyQualifiedName~BrowserPolicyAgentTests|FullyQualifiedName~OpenUrlOperationHandlerTests|FullyQualifiedName~MasterNetworkTransportTests|FullyQualifiedName~OperationContractsTests"` en `agent`: correcto, 55 pruebas Service superadas.
- `mvn -q -DskipTests compile` en `master-backend`: correcto.
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

Fase 19I2 sigue en `REAL_INSTALL_VALIDATION_PENDING`. Repetir fresh install en la PC descartable despues del fix de tokenizacion `sc.exe`, y solo entonces continuar la validacion real de fail-open, no activation espontanea, logon real, wrong password, switch real, batch/update/uninstall y providers estandar visibles.
