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
- Todo endpoint administrativo nuevo del Master Backend debe llamar a `MasterAccessGuard` antes de leer o escribir datos escolares.
- Solo quedan publicos sin `MasterAccessGuard` los endpoints de diagnostico `GET /api/system/health`, `GET /api/device/status`, `GET /api/device/machine-code` y `GET /api/master/authorization`.
- Nunca confiar en un SID declarado por JSON, UI, request HTTP o payload IPC.
- El SID local del caller debe derivarse del token real del cliente Named Pipe mediante APIs Windows soportadas.
- Cualquier error o duda en autorizacion Master debe fallar cerrado con `authorized=false`.
- Un administrador Windows distinto no hereda Master si su SID no esta ligado.
- `MasterWindowsBinding` nunca va en SQLite ni dentro de `installation.json` o `license.dat`.
- Rebinding de Master siempre debe ser explicito.
- El Master no debe almacenar passwords de cuentas Windows administradas en `classroom.db`.
- El Master no debe enviar passwords en comandos normales.
- La UI nunca debe recibir passwords ni secretos de cuentas administradas.
- Los logs nunca deben mostrar passwords ni material equivalente.
- Los comandos futuros de cuentas Windows administradas deben enviar solo `accountId` logico (`PRIMARY`/`SECONDARY`).
- La credencial real futura pertenece al Agent Service del Client y debe protegerse con mecanismos seguros de Windows.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para login/logoff/switch Windows.
- El mecanismo productivo de login/cambio de usuario debe disenarse posteriormente con integracion soportada por Windows, contemplando Credential Provider.
- Mantener siempre una via estandar de acceso/recovery de Windows.
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
- `OPEN_URL` y `OPEN_WEB_CONTENT` deben preferir ejecucion local en el Client; no convertir YouTube en screen share por default.
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
