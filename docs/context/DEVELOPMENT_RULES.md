# Reglas de desarrollo

Estas reglas son obligatorias para todos los agentes futuros.

## Antes de modificar archivos

1. Leer primero:
   - `docs/context/PROJECT_CONTEXT.md`
   - `docs/context/ARCHITECTURE.md`
   - `docs/context/FUNCTIONAL_MODEL.md`
   - `docs/context/DEVELOPMENT_RULES.md`
   - `docs/agent/CURRENT_STATE.md`
   - `docs/agent/DECISIONS.md`
2. Revisar despues:
   - `git status`
   - `git log --oneline -15`
3. Consultar `docs/agent/HISTORY.md` solo cuando se necesite contexto historico adicional.
4. Entender como se viene construyendo antes de modificar algo existente.
5. No rehacer arquitectura sin justificarlo.
6. No hacer commits automaticamente.

## Codigo

- Mantener cambios pequenos y coherentes con la estructura actual.
- No agregar dependencias innecesarias.
- Escribir codigo claro, simple y mantenible.
- Separar componentes y responsabilidades.
- En Java, mantener `controller -> service -> repository` cuando corresponda.
- No introducir repositorios, capas o abstracciones antes de que haya una necesidad real.
- Agregar pruebas proporcionales al riesgo del cambio.
- Mantener el proyecto compilando al terminar cada tarea.

## Seguridad

- Priorizar seguridad por defecto.
- No implementar ejecucion remota arbitraria.
- No aceptar comandos como `cmd.exe /c ...`, PowerShell arbitrario, shell remota ni rutas arbitrarias enviadas por un Master.
- Modelar futuros comandos como operaciones explicitas y estructuradas.
- No confiar en IP o MAC como identidad de autorizacion.
- No asumir que descubrimiento equivale a confianza.
- No asumir que una licencia MASTER autoriza control automatico sobre cualquier cliente.
- Mantener Network Identity separada de Installation Identity, Commercial License y Master Windows Binding.
- Network Identity no equivale a trust: solo identifica una llave publica y permite verificar firmas.
- Discovery no equivale a pairing: discovery solo encuentra candidatos en LAN.
- Pairing siempre requiere intencion/aprobacion explicita de la maestra o administrador.
- El challenge/response de pairing debe demostrar posesion de private keys en ambos lados sin exponerlas.
- Un challenge debe tener expiracion y proteccion contra replay.
- El trust resultante del pairing debe persistirse tanto en Master como en Client.
- Un registro `REVOKED` no puede administrar el Client ni reactivarse silenciosamente.
- La private key de Network Identity nunca debe guardarse en JSON, logs, SQLite, payload IPC ni archivos planos.
- `network-identity.json` solo puede contener metadata publica y debe estar ligado al `installationId` actual.
- Si Network Identity tiene metadata corrupta, llave faltante, fingerprint incompatible o `installationId` distinto, no regenerar ni adoptar silenciosamente.
- Network Identity no equivale a pairing, certificado, mTLS, trust ni autorizacion remota.
- IP, MAC y hostname no autorizan administracion.
- Una licencia MASTER valida no crea pairing ni trust con Clients.
- La autorizacion Master productiva proviene del Agent Service; el Master Backend solo consume estado derivado por IPC.
- Todo endpoint administrativo nuevo del Master Backend debe llamar a `MasterAccessGuard` antes de leer o escribir datos escolares, salvo la unica excepcion actual y explicita: `POST /api/classrooms/{classroomId}/input-control/unlock`.
- `POST /api/classrooms/{classroomId}/input-control/unlock` debe llamar a `MasterUnlockAccessGuard.requireUnlockAuthorized()` antes de leer datos escolares y solo puede despachar `UNLOCK_INPUT` recovery-safe. Esta excepcion no se generaliza a otros "recovery endpoints" ni autoriza acciones distintas.
- Solo quedan publicos sin `MasterAccessGuard` los endpoints de diagnostico `GET /api/system/health`, `GET /api/device/status`, `GET /api/device/machine-code` y `GET /api/master/authorization`.
- `GET_MASTER_UNLOCK_AUTHORIZATION` es una operacion Local IPC v1 read-only interna para recovery-safe `UNLOCK_INPUT`; no debe exponerse como endpoint publico ni reutilizarse como permiso administrativo general.
- La autorizacion local de unlock recovery la calcula solo el Agent Service con Installation Identity valida, Master Windows Binding valido, `installationId` coincidente y SID real del caller Named Pipe. No usar SID de JSON, username, headers, parametros ni membresia de Administrators.
- `GET_MASTER_UNLOCK_AUTHORIZATION` no exige Commercial License Master `ACTIVE` y no lee claims/roles de una licencia invalida, pero falla cerrado ante binding ausente/corrupto/schema desconocido/mismatch o SID no resuelto/distinto.
- `MasterUnlockAccessGuard` solo puede proteger acciones que reducen control y hayan sido declaradas recovery-safe; actualmente solo `UNLOCK_INPUT`. No agregar fallback entre `MasterAccessGuard` y `MasterUnlockAccessGuard`.
- Nunca confiar en un SID declarado por JSON, UI, request HTTP o payload IPC.
- El SID local del caller debe derivarse del token real del cliente Named Pipe mediante APIs Windows soportadas.
- Cualquier error o duda en autorizacion Master debe fallar cerrado con `authorized=false`.
- Un administrador Windows distinto no hereda Master si su SID no esta ligado.
- `MasterWindowsBinding` nunca va en SQLite ni dentro de `installation.json` o `license.dat`.
- Rebinding de Master siempre debe ser explicito.
- El Master no debe almacenar passwords de cuentas Windows administradas en `classroom.db`.
- El Master no debe enviar passwords en comandos normales.
- La UI no recibe passwords por defecto. La unica excepcion es una operacion explicita de Credential Vault Reveal despues de MasterAccessGuard + vault unlock valido; el secreto se entrega unicamente para la credencial solicitada y nunca se persiste en estado normal de UI.
- Las passwords Google escolares pueden almacenarse unicamente dentro de Credential Vault cifrado y nunca dentro de BrowserProfile, SQLite, logs, Cookies, Login Data o Local State.
- Los logs nunca deben mostrar passwords ni material equivalente.
- Los comandos futuros de cuentas Windows administradas deben enviar solo `accountId` logico (`PRIMARY`/`SECONDARY`).
- La credencial real futura pertenece al Agent Service del Client y debe protegerse con mecanismos seguros de Windows.
- `PRIMARY` y `SECONDARY` en cada Client se vinculan a cuentas Windows por SID real en `managed-windows-accounts.json`, no por username.
- `accountReference` del binding de cuentas administradas es informativa/canonica; no es identidad ni prueba de autorizacion.
- Borrar y recrear el mismo username no autoriza adoptar el SID nuevo; el rebind siempre debe ser explicito.
- `PRIMARY` y `SECONDARY` no pueden compartir SID.
- Passwords, hashes, credentialId, tokens, PINs y session data nunca van en `managed-windows-accounts.json`.
- `WindowsSessionState` se determina por SID del token real de la consola fisica, nunca por username.
- `WTSUserName` solo puede usarse como senal auxiliar de existencia de login; no es identidad ni authority de mapping.
- Session 0 nunca representa al alumno.
- `0xFFFFFFFF` de `WTSGetActiveConsoleSessionId()` es `UNKNOWN`, no evidencia suficiente de `NO_SESSION`.
- Una sesion bloqueada sigue clasificandose por su cuenta logueada.
- Las sesiones RDP o disconnected no sustituyen automaticamente la consola fisica.
- `GET_WINDOWS_SESSION_STATE` es on-demand y nunca debe convertirse en polling, timer, startup scan o heartbeat field.
- Ningun token, SID, username, domain, `accountReference` ni `sessionId` debe salir en el result remoto de `GET_WINDOWS_SESSION_STATE`.
- `SESSION_AGENT_AVAILABLE` no es requisito para consultar `WindowsSessionState`.
- `LOGOFF_WINDOWS_SESSION` nunca cierra una sesion cuyo SID real no coincida con el managed account esperado.
- Nunca aceptar `sessionId`, username, domain, SID, `accountReference`, password, force, timeout, command, args ni payload arbitrario desde Master para elegir que sesion cerrar.
- Antes de un logoff destructivo, revalidar inmediatamente `sessionId` y SID real de consola fisica contra el binding esperado.
- `OTHER_SESSION_ACTIVE` nunca se cierra automaticamente; debe reportarse como mismatch/cambio de sesion.
- `NO_SESSION` puede ser `SUCCESS` idempotente para `LOGOFF_WINDOWS_SESSION` porque la sesion esperada ya esta ausente.
- `LOGOFF_WINDOWS_SESSION` no requiere credenciales almacenadas, no consulta DPAPI, no usa Credential Vault y no usa Session Agent.
- `LOGOFF_WINDOWS_SESSION` no tiene retry automatico ni reconciliacion nueva; si falta `OperationResult`, usar `OPERATION_RESULT_UNKNOWN`.
- `SUCCESS` de `LOGOFF_WINDOWS_SESSION` significa solicitud `WTSLogoffSession` aceptada, no finalizacion confirmada.
- El Client credential store es `managed-windows-credentials.dat` y siempre permanece separado del binding SID de cuentas administradas en `managed-windows-accounts.json`.
- Client credentials nunca van en `managed-windows-accounts.json`.
- El Client password store usa Windows DPAPI bajo LocalSystem con scope de usuario actual; no usar LocalMachine.
- Nunca hacer fallback a plaintext, LocalMachine u otra cuenta si DPAPI falla o si el proceso no corre como LocalSystem.
- Passwords Windows administradas del Client nunca salen por CLI, Local IPC, Protobuf, UI, logs, heartbeat ni diagnostics.
- Cada Client credential queda ligada al SID del binding vigente dentro del payload protegido.
- Rebind a un SID nuevo invalida logicamente la credencial anterior; no adoptarla ni borrarla automaticamente.
- Passwords Windows administradas solo viven durante operaciones explicitas; limpiar buffers mutables controlados con `CryptographicOperations.ZeroMemory` o equivalente.
- Passwords remotas solo pueden transportarse en operaciones explicitas secret-bearing sobre gRPC/mTLS autenticado, con Master esperado, trust `PAIRED`, no `REVOKED` y Device correcto.
- Nunca usar `string` Protobuf para passwords; usar `bytes` con encoding documentado.
- Requests secret-bearing no se loguean ni se cachean completos como protobuf serializado.
- El dedupe de credential provisioning nunca conserva password raw, password hash, fingerprint/checksum de password ni request secreto completo.
- Mismo `operationId + accountId` en credential provisioning devuelve el resultado original y no reaplica el secreto.
- Password recibida para provisioning se persiste inmediatamente via DPAPI en el Client credential store o se descarta.
- El buffer mutable controlado recibido para provisioning se limpia siempre en `finally`.
- El Session Agent nunca recibe passwords Windows administradas; el secreto pertenece al Agent Service.
- El Client no ofrece reveal/export/dump; reveal humano pertenece al Credential Vault del Master.
- No validar passwords provocando `LogonUser`, Credential Provider, Winlogon, LSA ni ningun logon durante storage/provisioning.
- No agregar fallback Local IPC, HTTP, plaintext socket, file share, clipboard ni temp file para passwords remotas.
- No agregar retry automatico, reconciliation ni receipt para credential provisioning; ante falta de resultado confirmado usar `OPERATION_RESULT_UNKNOWN`.
- El bridge interno Master Credential Vault -> credential provisioning debe usar `MasterAccessGuard.requireAuthorized()`, no `MasterUnlockAccessGuard`.
- El bridge interno acepta solo `vaultSessionToken`, `credentialId`, `deviceId`, `operationId` y `PRIMARY`/`SECONDARY`; no acepta password, master password, username, SID, domain ni accountReference desde el caller.
- Para provisioning desde vault, solo `WINDOWS_ACCOUNT` es provisionable; `GOOGLE_ACCOUNT` debe rechazarse antes de llamar al gateway.
- `credentialId` y vault session token nunca salen del Master Backend ni se agregan a Protobuf, `OperationRequest`, BatchOperation, SQLite, heartbeat, ClientHello, logs, exceptions ni `OperationResult`.
- El bridge debe codificar la password revelada internamente como UTF-16LE sin BOM/NUL, mantenerla en `byte[]` y limpiar esa copia controlada en `finally`; no debe trim/normalizar/cambiar el secreto.
- El bridge interno no es reveal humano y no devuelve passwords al caller; la visualizacion humana queda separada en la futura operacion explicita de Credential Vault Reveal.
- `SUCCESS` de credential provisioning no valida la password contra Windows ni cambia la password real de la cuenta.
- No usar DPAPI en heartbeat, startup, idle, timers, polling ni scans.
- No hacer enumeracion, polling, WMI, Registry SAM, scans de perfiles ni `C:\Users` scanning en idle para cuentas Windows administradas.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para login/logoff/switch Windows.
- El mecanismo productivo de login/cambio de usuario debe disenarse posteriormente con integracion soportada por Windows, contemplando Credential Provider.
- Mantener siempre una via estandar de acceso/recovery de Windows.
- Nunca reemplazar, ocultar ni filtrar Credential Providers estandar de Windows.
- Galtek Credential Provider debe ser aditivo: falla abierto hacia los mecanismos normales de login de Windows, pero cerrado respecto a autenticacion Galtek.
- No implementar `ICredentialProviderFilter`.
- Credential Provider debe ser DLL nativa; no cargar .NET runtime dentro de LogonUI.
- Credential Provider nunca habla con Master/red directamente y nunca abre TCP, HTTP, gRPC local, DNS ni sockets LAN.
- Credential Provider nunca lee stores Galtek directamente; no lee `managed-windows-accounts.json`, `managed-windows-credentials.dat`, `installation.json`, `license.dat`, `authorized-masters.json` ni `credential-vault.dat`.
- Agent Service sigue siendo la autoridad de binding, credenciales y activaciones de Credential Provider.
- Sin activation explicita pendiente no existe tile/login Galtek activo.
- Activation de Credential Provider es efimera, in-memory, de expiracion corta y lazy; no persistir activation en archivo, Registry, SQLite, DPAPI ni heartbeat.
- No persistir activation ni secretos para auto-login.
- El pipe Service <-> Credential Provider debe ser dedicado, no reutilizar Local IPC v1 ni Session Command v1.
- El pipe Service <-> Credential Provider debe ser accesible solo por LocalSystem y el Service debe validar PID real del pipe, image path real de `%SystemRoot%\System32\LogonUI.exe`, sesion esperada y token LocalSystem.
- No confiar en PID, SID, username, process name ni sessionId enviados por payload para autorizar al Credential Provider.
- La password almacenada en el Client solo puede salir del Agent Service hacia Galtek Credential Provider mediante `GaltekClassroom.CredentialProvider.v1`, con caller LogonUI validado y activation one-time vigente.
- Esta excepcion no habilita reveal, Local IPC generico, Session Command, red, Master, UI, temp files, Registry ni ningun canal alterno.
- Password Service -> Provider nunca viaja como JSON, Base64, hexadecimal, XML, protobuf ni string de contrato; la unica respuesta secreta vigente del bridge es binaria, versionada y acotada.
- Provider mantiene secretos en buffers mutables propios y usa `SecureZeroMemory`; no guardar password en `std::wstring`, `CString`, `BSTR`, singletons, globals ni fields permanentes.
- Una activation puede liberar credential como maximo una vez. El Service debe marcarla `CONSUMED` antes de revelar el secreto; fallo de envio, packing o auth package despues de consume no restaura activation.
- Provider nunca reintenta password automaticamente: una segunda llamada a `GetSerialization` sobre la misma credential no vuelve a adquirir secreto.
- Account identity del Provider se deriva del SID local por Agent Service y APIs Windows soportadas, nunca desde Master, username remoto ni `accountReference`.
- La primera identity exitosa fija el SID esperado de la activation; un rebind entre identity y acquire bloquea el secreto.
- `GetSerialization` usa formato Windows soportado con `KERB_INTERACTIVE_UNLOCK_LOGON`, password protection de Windows y paquete `Negotiate` resuelto por LSA; no implementar autenticacion propia ni crypto propia dentro del provider.
- Providers estandar de Windows permanecen disponibles aunque Galtek falle.
- Fallo del Agent/Galtek nunca debe impedir login estandar de Windows.
- Solo un Master localmente autorizado y con trust de pairing vigente podra ordenar logon/logoff/switch en Clients cuando existan comandos administrativos futuros sobre transporte seguro.
- El transporte gRPC/mTLS de Prompt 13 solo permite conexion, identificacion y heartbeat; no autoriza por si mismo comandos remotos.
- Cualquier extension del transporte debe exigir TLS/mTLS, trust `PAIRED`, no `REVOKED`, fingerprints coincidentes y fallo cerrado.
- No agregar fallback plaintext, reflection/debug gRPC abierto en produccion ni aceptacion de certificados arbitrarios.
- El Client debe iniciar conexiones persistentes hacia el Master; no depender de conexiones entrantes hacia cada PC Client.
- Prompt 14 construyo registro/capabilities y framework de operaciones sobre el transporte seguro existente, sin redisenar pairing/mTLS ni agregar comandos remotos genericos.
- `Network Identity != Pairing != Device != Student`; no mezclar esos conceptos en APIs, persistencia ni UI futura.
- El Master controla `deviceId`; no confiar en `deviceId` declarado por el Client.
- `device_network_bindings` solo vincula Devices con Network Identities paired; no reemplaza `paired-clients.json` como autoridad de trust.
- Un Client `REVOKED` no se registra ni administra aunque exista binding historico.
- Capabilities reportadas por `ClientHello` son operativas y no autorizan por si mismas.
- El heartbeat no debe escribir SQLite en cada ciclo; presencia viva debe mantenerse principalmente en memoria.
- Cualquier handler futuro de `OperationRequest` debe ser tipado, idempotente por `operationId` cuando aplique y debe fallar cerrado con `OPERATION_NOT_IMPLEMENTED` mientras no este implementado.
- Los perfiles `LEGACY`, `STANDARD` y `MASTER_BALANCED` son operacionales y nunca conceden autorizacion, trust, pairing ni permisos.
- `PRIMARY` y `SECONDARY` son Windows normal por default; no implementar modo kiosco, restricciones de aplicaciones, cambio automatico de sesion ni bloqueo de input al iniciar clase salvo alcance explicito futuro.
- El Master no debe enviar rutas ejecutables, rutas de workspace, rutas USB ni rutas absolutas arbitrarias; usar `applicationId` y destinos logicos.
- Para aplicaciones, `ApplicationDefinition` del Master no equivale a `ApplicationBinding` del Client. El Master solo envia `applicationId`; el Client resuelve localmente desde `application-bindings.json`.
- El endpoint Master de `OPEN_APPLICATION` debe resolver una `ApplicationDefinition` activa persistida y exigir asociacion `Classroom -> ApplicationDefinition` antes de crear batch o enviar a Devices.
- Nunca aceptar desde el Master `executablePath`, command line, argumentos, working directory, shell, PowerShell, `cmd`, scripts, shortcuts, MSI ni URI arbitraria para abrir aplicaciones.
- El catalogo local `application-bindings.json` vive en `<CommonApplicationData>\Galtek\Classroom\`, usa escritura durable, ACL sin write para usuarios normales y falla cerrado con `APPLICATION_BINDINGS_INVALID` ante corrupcion.
- Los launch types locales de aplicaciones son tipados. En Prompt 17A solo existen `APP_PATHS` y `ABSOLUTE_EXE`; cualquier tipo nuevo futuro debe agregarse explicitamente y no como comando generico.
- `OPEN_APPLICATION` productivo debe mantener `applicationId` como unico input funcional en Protobuf y Session Command. El Service hace preflight logico; el Session Agent vuelve a validar y resuelve el target fisico local antes de lanzar.
- `APP_PATHS` para launch se resuelve solo por HKLM App Paths, valor default, vistas Registry64/Registry32 cuando corresponda. No usar HKCU App Paths, PATH, Program Files, Start Menu, WindowsApps, uninstall keys, scans de disco ni procesos.
- `OPEN_APPLICATION` debe lanzar solo con `CreateProcessW`, `lpApplicationName` absoluto, `lpCommandLine = null`, sin argumentos, sin shell, sin `runas`, sin UAC intencional, sin `WaitForExit` ni monitoreo de proceso.
- No agregar auto-discovery de aplicaciones, scans de Program Files, Start Menu, Registry, discos, procesos, WMI, watchers, timers, heartbeat data ni writes idle para mantener el catalogo.
- No copiar perfiles Chrome crudos (`Login Data`, `Cookies`, `Local State`, tokens o secretos protegidos).
- No limpiar working copies locales antes de `SYNC -> VERIFY -> COMMIT CANONICAL -> CONFIRM`.
- Ante operacion sin ACK, red perdida o energia perdida, no asumir `SUCCESS`; conservar origen/local working copy y reportar estado recuperable.

## Dominio funcional y UX masiva

- El usuario principal es una maestra que administra alumnos pequenos.
- `CLASS_TIME_TO_READY` es el KPI principal del aula: equipos rapidos deben quedar disponibles primero y los lentos incorporarse progresivamente.
- Nunca esperar a que todos los Clients esten listos para permitir iniciar clase con los targets `READY`.
- Toda operacion repetitiva debe analizarse primero como operacion batch/grupo antes de disenar un flujo uno por uno.
- Nunca obligar a la maestra a repetir manualmente una operacion que ya tuvo exito en otros equipos.
- `PARTIAL_SUCCESS` debe manejarse explicitamente en operaciones masivas.
- Retry debe poder aplicarse unicamente a targets fallidos cuando el error sea recuperable.
- `Device` y `Student` son entidades independientes; no usar nombres de PC como identidad de alumno.
- Los archivos del alumno pertenecen a `StudentWorkspace`, no a `PC01`.
- Operaciones destructivas preservan el origen hasta verificar destino cuando haya transferencia de datos.
- El workspace canonico pertenece al Master; el Client conserva una working copy local mientras el alumno usa la PC.
- No disenar trabajo principal del alumno directamente sobre share SMB.
- `REMOVABLE_STORAGE` es destino logico futuro autorizado, no una ruta libre.
- Una distribucion grande, thumbnails o inventario no deben bloquear operaciones `CRITICAL` como `UNLOCK_INPUT` o `STOP_PROJECTION`.
- `LOCK_INPUT` y `UNLOCK_INPUT` deben ejecutarse fisicamente solo en `GaltekClassroom.Agent.Session` mediante `User32.dll BlockInput(BOOL)` y un owner thread dedicado; el Agent Service/Session 0 nunca debe llamar `BlockInput`.
- `UNLOCK_INPUT` es recovery-safe y no debe bloquearse por Commercial License inactiva, pero esa excepcion no salta mTLS, pairing trust, Device authorization ni autenticacion Session Command.
- `OPEN_URL` y `OPEN_WEB_CONTENT` deben preferir ejecucion local en el Client; no convertir YouTube en screen share por default.
- El endpoint Master `POST /api/classrooms/{classroomId}/open-url` debe seguir aceptando solo `url` y `targetDeviceIds`, con `MasterAccessGuard` antes de cualquier lectura escolar y safety estructural global antes de crear `BatchOperation`.
- Para `OPEN_URL` batch, el Master debe resolver una sola policy efectiva por target con `accountType = null` (`ANY` solamente), derivar `groupId` desde assignment actual y evaluar `EXACT_URL` directamente con `BrowserNavigationPolicyEvaluator`; no aplicar `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE` a `OPEN_URL` concreto.
- `OPEN_URL` batch no debe seleccionar browser/profile, no debe aplicar policies automaticamente, no debe reintentar automaticamente ni extender reconciliacion/status query para inferir pestañas abiertas.
- Las politicas administrativas de navegacion viven en el Master como fuente de verdad persistente separada de la safety estructural de URL. Una allowlist nunca autoriza esquemas inseguros rechazados por safety.
- Para navegacion web, resolver como maximo una policy efectiva por contexto usando precedencia determinista; no mezclar reglas de policies de scopes distintos.
- No usar regex arbitraria, JavaScript regex ni wildcards libres para reglas URL de administracion escolar.
- El enforcement productivo de navegacion Chrome/Edge debe usar operaciones tipadas Galtek y `URLBlocklist`/`URLAllowlist` en `HKEY_USERS\<SID>` del usuario interactivo real; no usar HKLM, extension, proxy, DNS, firewall, hosts, inspeccion HTTPS, shell, polling, browser automation ni matar/reiniciar navegadores para esta funcionalidad.
- Los endpoints Master que aplican browser policies deben aceptar solo `targetDeviceIds` explicitos y resolver siempre desde la fuente de verdad persistida; no aceptar `policyId`, rules, accountType, URLs, registry paths ni payload arbitrario desde la request de apply.
- En dispatch Master de browser policies, usar `accountType = null` hasta que una fase futura integre explicitamente el binding seguro `PRIMARY/SECONDARY -> Windows SID`; esto significa resolver solo policies `ANY`, no inferir `PRIMARY`.
- Antes de fanout remoto de browser policies, congelar todos los parametros Protobuf tipados por target y persistir una sola `BatchOperation`; fallos de preflight por target no cancelan targets listos.
- Las politicas administrativas de descarga de navegador viven en un dominio separado de navegacion. No agregar `DownloadRestrictions`, extension lists ni MIME lists dentro de `BrowserAccessPolicy`, `BrowserPolicyMode`, `BrowserUrlRule` o URL match types.
- Para descargas de navegador, resolver como maximo una policy efectiva por contexto con la misma precedencia que navegacion: `DEVICE` cuenta especifica, `DEVICE ANY`, `GROUP` cuenta especifica, `GROUP ANY`, `CLASSROOM` cuenta especifica, `CLASSROOM ANY`, o `NO_SPECIAL_RESTRICTIONS` implicito.
- El enforcement Agent-side de descargas debe usar solo `DownloadRestrictions` como `REG_DWORD` bajo `HKEY_USERS\<SID>` del usuario interactivo real para Chrome/Edge. Como es un value dentro de la parent key y no existe ACL por value, cualquier hardening ACL debe ser parent-only, conservador, sin propagacion recursiva y sin reescribir subkeys hijas.
- No modelar bloqueo arbitrario administrable por extension/MIME para Chrome/Edge Windows hasta que exista un mecanismo comun realmente enforceable. En `BLOCK_ALL`, la descarga futura autorizada por maestra debe ir por canal Galtek tipado/controlado hacia `StudentWorkspace`, no desbloqueando temporalmente el browser.
- Projection debe distinguir `SCREEN_SHARE`, `WHITEBOARD`, `POINTER`, `LOCAL_MEDIA` y `OPEN_WEB_CONTENT`.
- Chrome passwords, cookies y cache no se copian directamente como estrategia de portabilidad.
- Un Master se autoriza por Windows SID ligado, no solo por username ni por pertenecer a Administrators.
- Errores tecnicos deben mapearse a errores operacionales antes de llegar a UI.
- Las futuras UI deben priorizar acciones masivas, recuperacion y minima intervencion manual.
- El cambio futuro de cuenta Windows administrada debe ser batch-first: una sola accion sobre aula/grupo/devices, con `NO_CHANGE`, exitos, fallidos y retry solo de fallidos.

## Rendimiento y recursos

- Galtek Classroom se disena primero para Clients de 4 GB RAM, HDD y CPU de gama baja.
- Cuando Galtek no realiza trabajo solicitado, el Client debe quedar casi idle: CPU cercano a 0%, sin captura, sin overlays, sin UI, sin process scanning, sin filesystem scanning, sin WMI periodico, sin writes periodicos y sin logs por heartbeat/PING sano.
- Prioridad de recursos: Windows y aplicacion educativa del alumno, control critico de Galtek, preparacion de clase, operaciones normales, visual/observabilidad y tareas background no esenciales.
- `CLASS_TIME_TO_READY` sigue siendo el KPI principal; ninguna funcion visual o background debe competir contra comenzar clase.
- `LEGACY` es el default conservador para Clients de perfil desconocido.
- No implementar deteccion agresiva de hardware; si se modela inferencia futura debe ejecutarse una sola vez o muy raramente.
- Nunca usar polling WMI para determinar perfil ni para heartbeat, presencia, `ClientHello` repetitivo, Session Agent, UI o previews.
- Los budgets idle son objetivos de ingenieria: Agent Service <= aprox. 60 MB, Session Agent <= aprox. 40 MB y Client combinado <= aprox. 100 MB; superar aprox. 150 MB combinado requiere justificacion y revision.
- No crear unit tests que fallen por CPU, memoria o Working Set exacto dependiente de la maquina.
- No crear timers rapidos ni intervalos sub-segundo salvo durante una accion interactiva que lo necesite; toda tarea periodica debe justificar frecuencia.
- El heartbeat debe ser pequeno, persistente sobre gRPC/mTLS y sin writes persistentes ni logs sanos; no sustituirlo por polling HTTP.
- En HDD legacy evitar scans recursivos, reescrituras completas, temporales enormes, flush constante y pequenas escrituras aleatorias frecuentes.
- El Session Agent idle no debe capturar pantalla, pintar overlays, abrir UI, escanear procesos/filesystem ni consultar Windows de forma periodica costosa.
- Produccion debe usar `INFO` solo para eventos significativos; errores repetitivos deben rate-limitarse o coalescer conceptualmente.
- Diagnostico de performance solo on-demand: snapshot ligero, sin recoleccion constante, sin persistir telemetria y sin enviarla por heartbeat.
- Performance tuning adicional requiere medicion reproducible en hardware real.
- `DEGRADED` es estado de presion de recursos y no equivale a `OFFLINE`.
- Load shedding debe sacrificar prefetch, inventario no esencial, thumbnails, calidad/FPS de preview, transferencias no urgentes y background antes de control critico.
- Nunca sacrificar primero heartbeat/control basico, `UNLOCK_INPUT`, `STOP_PROJECTION`, recovery, proteccion de workspace o estado de sesion necesario para empezar clase.
- El Master debe mantenerse local y pequeno: SQLite, WAL, pool Hikari pequeno, queries batch-friendly y heap JVM objetivo inicial <= 512 MB salvo profiling real que justifique mas.
- No introducir Redis, Kafka, Elasticsearch, RabbitMQ, DB server separado ni infraestructura distribuida pesada para la operacion local normal.
- No convertir el Master en terminal server ni ejecutar Word/Chrome/apps de alumnos remotamente.

## Resiliencia, apagones y startup

- Tratar power loss, reboot abrupto, kill del proceso y boot storm de aula como condiciones normales.
- Priorizar startup rapido del plano de control sobre licencia comercial completa, WMI costoso, inventario, thumbnails, captura, proyeccion, transferencias grandes, filesystem sync o diagnostico pesado.
- Local IPC y estado seguro de dispositivo deben estar disponibles tan pronto exista identidad minima valida; no bloquearlos por trabajo diferible.
- El Master no debe esperar a que todos los Clients esten online para quedar operativo; proceso vivo + storage listo basta para control-plane ready.
- Nunca borrar ni recrear automaticamente `classroom.db`, `classroom.db-wal` o `classroom.db-shm` por detectar shutdown no limpio.
- No borrar, truncar ni adoptar silenciosamente archivos criticos corruptos: Installation Identity, Network Identity, binding, trust stores, licencias y llaves deben fallar cerrado o reportar recovery.
- Escribir archivos criticos con temp file en el mismo directorio, flush/fsync y move/replace atomico cuando aplique.
- Clasificar persistencia futura como `EPHEMERAL`, `NORMAL` o `CRITICAL_DURABLE`; heartbeat, presencia, preview state y telemetria no deben salir de memoria.
- Los markers de ejecucion se escriben al inicio y se eliminan en shutdown limpio; no deben convertirse en heartbeat persistente ni producir writes periodicos.
- Boot, Session Agent startup, `ClientHello`, pairing, registration, reconnect, heartbeat y `DEVICE_ONLINE` no deben iniciar captura, proyeccion, thumbnails, filesystem sync, inventario pesado ni scans recursivos.
- Una operacion remota sin ACK, sin `OperationResult` confirmado o interrumpida por energia/red no cuenta como `SUCCESS`; debe quedar `RECOVERY_REQUIRED`, `PENDING_SYNC` o reconciliacion equivalente.
- La reconexion masiva de Clients debe usar backoff y jitter acotado para evitar thundering herd, manteniendo conexiones salientes desde Clients.
- Recovery y control critico tienen prioridad sobre funciones visuales o background.

## Persistencia Master SQLite

- Usar Spring JDBC y repositories explicitos; no introducir JPA/Hibernate mientras el diseno siga siendo SQLite local y explicito.
- Ejecutar migraciones con Flyway desde `db/migration/sqlite`.
- No crear ni modificar tablas fuera de migraciones versionadas.
- No usar `AUTOINCREMENT` para identidades del dominio; usar IDs `TEXT` generados por la aplicacion.
- Guardar timestamps en UTC como texto estable.
- Habilitar foreign keys por conexion.
- Mantener WAL, `busy_timeout` y pool pequeno para SQLite local.
- `DeviceAssignment` es fuente de verdad de asignaciones actuales e historicas.
- Mantener constraints de un assignment actual por alumno y un assignment actual por device.
- Archivar antes que borrar entidades escolares con historial; no hacer hard-delete de alumnos, devices, aulas o assignments salvo que una fase futura defina retencion/borrado seguro.
- No borrar, sobrescribir ni recrear automaticamente una base corrupta; reportar `MASTER_DATABASE_CORRUPT` y preservar el archivo para diagnostico/recuperacion.
- Mapear excepciones SQLite a `ErrorCode`; no propagar mensajes SQL tecnicos a UI.
- Minimizar PII: guardar solo datos necesarios para aula, alumno, workspace, perfiles y operaciones.
- No persistir passwords, cookies, tokens, cache protegido ni secretos de navegador.
- No persistir credenciales ni passwords de cuentas Windows administradas en `classroom.db`.
- No persistir passwords Windows o Google escolares fuera de `credential-vault.dat` cifrado.
- No persistir `MasterWindowsBinding` en `classroom.db`; la autoridad final del SID autorizado es el Agent Service.
- Consultas de listados deben ser batch-friendly; evitar N+1 para classroom, group, devices, assignments y targets batch.
- Bootstrap y snapshot deben devolver modelos de lectura agregados para la UI, no entidades de persistencia.
- Las escrituras multi-tabla deben ser transaccionales y tener pruebas de rollback cuando afecten invariantes.
- Usar version optimista en updates mutables y mapear conflictos a `CONCURRENT_MODIFICATION`.

## UI futura

- React.
- Tauri.
- Sin Vite.
- Diseno moderno orientado a escritorio.
- Adaptable a diferentes resoluciones de monitor.
- Navegacion completa mediante teclado.
- Foco visible.
- Enter solo cuando sea seguro.
- Escape para cerrar o cancelar.
- Orden de tabulacion logico.
- Restaurar foco tras cerrar modales.
- Evitar trampas de foco.
- Minimizar pasos operativos.

## Entorno y producto

- El entorno de desarrollo principal es Windows.
- Galtek Classroom debe ser LAN/offline-first.
- No introducir dependencias de AWS/cloud para el funcionamiento normal local.
- Galtek Hub es independiente y genera las licencias comerciales.
- La aplicacion local no genera licencias.

## Memoria de agentes

Antes de terminar cualquier tarea, actualizar los archivos de memoria que correspondan:

- `docs/context/ARCHITECTURE.md` si cambia arquitectura o estado implementado.
- `docs/context/FUNCTIONAL_MODEL.md` si cambia el dominio escolar, operaciones, errores o reglas batch.
- `docs/agent/CURRENT_STATE.md` siempre.
- `docs/agent/DECISIONS.md` si hay decisiones vigentes nuevas.
- `docs/agent/HISTORY.md` siempre con una entrada compacta append-only.
