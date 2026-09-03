# Modelo funcional

Este documento es obligatorio para agentes futuros antes de disenar funcionalidades operativas de Galtek Classroom.

Prompt 07 define el dominio funcional, modelos puros y planners de preflight. Prompt 08 persiste ese dominio en SQLite local para el Master. Prompt 10 expone la primera API administrativa protegida sobre SQLite con bootstrap, snapshot, CRUD escolar, batches de alumnos y assignments de metadata. Prompt 9.6 formaliza cuentas Windows administradas futuras en Clients (`PRIMARY`/`SECONDARY`) y cambio masivo de sesion como dominio puro. Prompt 14.2 fija el modelo operativo real del aula, readiness progresiva, workspace canonico Master/local working copy Client, prioridades y modos de proyeccion como dominio puro. Prompt 14.4 fija resiliencia ante apagones, startup rapido, boot storm y semantica de recovery sin implementar filesystem real, browser automation, UI, login/logoff Windows, USB, captura ni proyeccion. Prompt 15A agrega `SHUTDOWN` y `RESTART` productivos en el Agent sobre el framework seguro existente. Prompt 15B agrega dispatch batch desde Master para esas dos operaciones. Prompt 15C agrega reconciliacion segura de resultados inciertos sin UI, retry automatico ni nuevas operaciones Windows. Prompt 16A agrega el canal local seguro Service -> Session para acciones interactivas futuras, Prompt 16B implementa `OPEN_URL` productivo Agent-side sin endpoint batch Master, sin bloqueo de URLs/descargas y sin UI, Prompt 16C agrega en el Master el modelo persistente de politicas administrativas de navegacion, Prompt 16D agrega enforcement Agent-side para Chrome/Edge usando `URLBlocklist`/`URLAllowlist` en el usuario interactivo real, Prompt 16E1 agrega en el Master el modelo persistente de politicas de descarga de navegador, Prompt 16E2A prepara el contrato tipado y compilador C# puro de descargas, Prompt 16E2B agrega enforcement Agent-side real de descargas mediante `DownloadRestrictions`, Prompt 16F1 agrega dispatch batch Master para aplicar policies de navegacion y descarga persistidas, Prompt 16F2 agrega dispatch batch Master para `OPEN_URL`, Prompt 17A agrega el catalogo local `application-bindings.json`, Prompt 17B implementa `OPEN_APPLICATION(applicationId)` productivo Agent-side, Prompt 17C agrega dispatch batch Master para `OPEN_APPLICATION`, Prompt 18A implementa `LOCK_INPUT`/`UNLOCK_INPUT` productivo Agent-side mediante Session Agent y `BlockInput`, y Prompt 18B1 agrega una autorizacion local read-only de proposito unico para el futuro `UNLOCK_INPUT` recovery desde Master sin exigir Commercial License local activa.

## Principio de producto

El usuario principal es una maestra que administra alumnos pequenos en kinder o primaria. Los alumnos pueden cerrar programas, perder archivos, guardar en lugares incorrectos, mover carpetas o requerir ayuda frecuente.

La experiencia esperada es batch-first:

```text
Maestra
  -> una accion
  -> salon / grupo / alumnos / equipos seleccionados
  -> Galtek resuelve PC por PC
  -> la UI muestra resumen y excepciones
```

No se debe disenar un flujo que obligue a repetir manualmente una accion exitosa equipo por equipo.

KPI principal:

```text
CLASS_TIME_TO_READY
```

El objetivo principal es minimizar el tiempo desde que la maestra llega/enciende equipos hasta que los alumnos pueden comenzar actividad. El sistema debe liberar primero a los equipos rapidos y permitir que los lentos se incorporen progresivamente.

## Hardware objetivo

Master de profesora:

- Intel Core i5 8a generacion aprox.
- 16 GB DDR4.
- SSD 256 GB.

Clients:

- Legacy aprox. 16: hardware heterogeneo muy limitado, principalmente 4 GB RAM + HDD, CPUs Core 2 Duo / Celeron / AMD antiguos, muy lentos.
- Renovados aprox. 10: Core i5 6a generacion, 8 GB DDR4, SSD 256 GB.

El Master absorbe orquestacion, almacenamiento canonico de trabajos, metadata escolar, manifests/checksums futuros, distribucion de contenido, coordinacion batch, recuperacion y estado del aula. Word, Chrome, Scratch, RoboMind, Office y aplicaciones interactivas de alumnos corren localmente en cada Client.

Regla permanente desde Prompt 14.3:

```text
Galtek Classroom se disena primero para 4 GB RAM + HDD + CPU de gama baja.
```

Si performance compite con una funcion secundaria, se degrada la funcion secundaria antes que afectar Windows, la aplicacion educativa del alumno o el control critico de la maestra.

## Performance budgets

Principio idle:

```text
GALTEK CLIENT SHOULD BE ALMOST IDLE
```

Cuando no hay trabajo solicitado, el Client no debe hacer captura, overlays, UI, process scanning, filesystem scanning, inventario periodico, WMI periodico, writes periodicos ni logs por heartbeat/PING sano. La conexion gRPC persistente y un heartbeat pequeno se conservan; no deben reemplazarse por polling HTTP.

Los budgets son objetivos de ingenieria, no garantias contractuales ni tests de Working Set exacto:

- Agent Service idle: preferiblemente <= aprox. 60 MB.
- Session Agent idle: preferiblemente <= aprox. 40 MB.
- Galtek Client combinado idle: objetivo <= aprox. 100 MB.
- Si una implementacion futura supera aprox. 150 MB combinado idle, requiere justificacion y revision.

CPU esperada en idle: cercana a 0%. No crear timers rapidos ni intervalos sub-segundo salvo durante una accion interactiva que realmente lo necesite. Toda tarea periodica debe justificar frecuencia.

Disco en idle: no periodic disk writes, no logs por heartbeat sano, no archivos de telemetria continua, no reescrituras completas y no scans recursivos innecesarios. En HDD legacy se deben evitar flush constante y pequenas escrituras aleatorias frecuentes.

## Recovery por apagones y startup

Regla permanente desde Prompt 14.4:

```text
POWER LOSS IS NORMAL
```

Un corte de energia, reinicio abrupto, kill del proceso, desconexion de red o arranque simultaneo de muchas PCs debe tratarse como condicion normal de aula. Recovery debe favorecer datos preservados, control rapido y estados explicitos de incertidumbre.

Principios funcionales:

- Fast startup antes que trabajo visual/pesado: Local IPC, identidad, trust, heartbeat/reconexion y acciones criticas futuras deben estar disponibles antes de captura, thumbnails, proyeccion, inventario, transferencias grandes o sync pesado.
- El Master queda control-plane ready cuando el proceso esta vivo y el almacenamiento local esta listo; no espera a que todos los Clients esten online.
- Cada Client se recupera de forma independiente. Un Client lento, apagado o en recovery no bloquea a otros.
- Boot, Session Agent startup, `ClientHello`, pairing, registration, reconnect, heartbeat y `DEVICE_ONLINE` no disparan captura automatica, proyeccion, thumbnails, filesystem sync ni inventario pesado.
- Una operacion remota sin ACK o sin resultado confirmado no es `SUCCESS`; debe conservarse como incertidumbre y solo reconciliarse cuando exista un mecanismo seguro y explicitamente soportado para ese tipo de operacion.
- Si energia/red caen antes de confirmar sync o commit canonico, se conserva la working copy local y se muestra `PENDING_SYNC` o `RECOVERY_REQUIRED`.
- Un marker de ejecucion solo detecta apagado no limpio; no debe reemplazar recovery propio de SQLite ni borrar WAL/SHM.
- En boot storm, reconexion saliente con backoff y jitter evita que todos los Clients golpeen al Master al mismo tiempo.

Clasificacion futura de durabilidad:

- `EPHEMERAL`: heartbeat, presencia `ONLINE`/`OFFLINE`, preview state y telemetria; vive en memoria.
- `NORMAL`: metadata reconstruible o recuperable.
- `CRITICAL_DURABLE`: cambios que no deben reconocerse como confirmados antes de durabilidad suficiente.

## Performance profiles

Perfiles operacionales de Client:

```text
DevicePerformanceProfile
  LEGACY
  STANDARD
```

`LEGACY` es el default conservador cuando el perfil del Client es desconocido. `STANDARD` permite un poco mas de concurrencia, pero nunca concurrencia ilimitada.

Perfil operacional del Master:

```text
MasterPerformanceProfile
  MASTER_BALANCED
```

Estos perfiles no son identidad, seguridad ni autorizacion. No se autoriza ninguna accion por hardware. Una inferencia futura de hardware debe ser conservadora, ejecutarse una sola vez o muy raramente, y nunca usar polling WMI.

Clases de trabajo de recursos:

```text
ResourceWorkClass
  CONTROL_CRITICAL
  CLASS_PREPARATION
  INTERACTIVE
  TRANSFER
  VISUAL
  BACKGROUND
```

`ResourceWorkClass` clasifica consumo y shedability; `OperationPriority` conserva la precedencia de operaciones (`CRITICAL > HIGH > NORMAL > LOW`). Una clase de trabajo puede mapear a un piso de prioridad, pero no reemplaza la prioridad operacional.

## Concurrencia y load shedding

Reglas iniciales:

- `LEGACY`: maximo 1 operacion pesada simultanea por Client.
- `STANDARD`: puede aceptar ligeramente mayor concurrencia, nunca ilimitada.
- `MASTER_BALANCED`: fanout limitado, colas, backpressure y prioridades.

Una operacion `CRITICAL` nunca espera detras de thumbnails, transferencias grandes, inventario o prefetch.

Orden conceptual de sacrificio bajo saturacion:

```text
PREFETCH
NON_ESSENTIAL_INVENTORY
THUMBNAILS
PREVIEW_QUALITY_OR_FPS
NON_URGENT_TRANSFER
BACKGROUND_JOB
```

Nunca sacrificar primero heartbeat/control basico, `UNLOCK_INPUT`, `STOP_PROJECTION`, recovery, proteccion de workspace ni estado de sesion necesario para comenzar clase.

Debe existir el concepto:

```text
ONLINE + DEGRADED
```

`DEGRADED` no equivale a `OFFLINE`: el Client puede seguir controlable aunque suspenda previews o baje calidad visual. No se debe implementar monitoreo continuo pesado para detectarlo.

## Diagnostico de performance

El diagnostico de rendimiento debe ser on-demand. Puede modelar snapshot ligero de process working set, CPU aproximado, threads y uptime, pero no debe recolectar constantemente, persistir telemetria ni enviarla en cada heartbeat.

## Flujo real de primaria

La maestra:

1. llega;
2. enciende las PCs;
3. abre Galtek en el Master;
4. selecciona aula/grupo;
5. asigna alumnos a PCs;
6. Galtek prepara cada PC independientemente;
7. los primeros equipos `READY` pueden empezar clase sin esperar a los lentos.

Nunca esperar a que todos los Clients esten listos. Una etapa fallida en PC07 no bloquea la preparacion de PC08.

## Classroom

`Classroom` representa un aula operativa, por ejemplo `Aula Primaria`.

Relaciona:

- Devices.
- Students.
- SchoolGroups.
- configuracion del aula.
- aplicaciones autorizadas.
- navegador predeterminado.
- politicas futuras.
- soporte planificado de recuperacion de workspace.

Desde Prompt 08, `Classroom` se persiste en SQLite con aplicaciones autorizadas mediante tabla de relacion. La lista de devices, students y groups se reconstruye desde sus repositories por `classroomId`.

API Prompt 10:

- `GET /api/master/bootstrap` devuelve aulas activas con conteos basicos para elegir contexto.
- `GET /api/classrooms/{id}/snapshot` devuelve aula, grupos, alumnos activos, devices, assignments actuales, aplicaciones y resumen sin requerir N+1 desde la UI.
- Archivar un aula con grupos, alumnos activos, devices, assignments actuales o aplicaciones asociadas falla con `CLASSROOM_HAS_ACTIVE_CONTENT`.

## Device

`Device` representa una computadora logica administrada por el Master.

Campos principales:

- `deviceId`: identidad logica del Master.
- `installationId`: instalacion fisica de Galtek Classroom.
- `displayName`: nombre visible, por ejemplo `PC01`.
- `hostname`: informativo.
- `status`: estado operacional.
- `lastSeen`: ultimo contacto conocido.
- `capabilities`: capacidades declaradas.
- `assignedStudentId`: alumno asignado actualmente, si aplica.

No usar IP, MAC ni hostname como identidad primaria o de seguridad. La identidad de red criptografica ya existe y el estado `ONLINE` solo debe derivarse de una conexion autenticada real, no de discovery.

Desde Prompt 14:

- `Network Identity != Pairing != Device != Student`.
- Un Client `PAIRED` puede existir sin `Device` registrado en un aula.
- `PAIRED + ONLINE + sin Device` se expone como `AVAILABLE_FOR_REGISTRATION`.
- `PAIRED + Device asociado` se expone como `REGISTERED`.
- `REVOKED` no es registrable ni administrable.
- El Master genera y controla `deviceId`; no se acepta `deviceId` declarado por el Client como identidad.
- El vinculo vigente entre Device y Network Identity se persiste en `device_network_bindings`.
- `paired-clients.json` sigue siendo la autoridad de trust; SQLite no reemplaza pairing.
- Capabilities productivas conocidas actuales: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1`, `SESSION_AGENT_AVAILABLE`, `POWER_CONTROL_V1`, `OPEN_URL_V1`, `OPEN_APPLICATION_V1`, `BROWSER_NAVIGATION_POLICY_V1`, `BROWSER_DOWNLOAD_POLICY_V1` e `INPUT_CONTROL_V1`.
- Capabilities desconocidas se ignoran y no otorgan permisos.
- El heartbeat mantiene presencia principalmente en memoria y no escribe SQLite cada 15 segundos.

Estados contemplados:

```text
ONLINE
OFFLINE
CONNECTING
UNLICENSED
LICENSE_BLOCKED
AGENT_UNAVAILABLE
SESSION_UNAVAILABLE
BUSY
ERROR
```

No reducir el estado operacional a un boolean `online`.

Persistencia Prompt 08:

- `Device` se persiste por `device_id` logico del Master.
- `installationId` tiene indice unico.
- `assignedStudentId` se deriva del assignment actual; no es la fuente de verdad.

## ManagedWindowsAccount

`ManagedWindowsAccount` representa una cuenta Windows administrada futura en un Client. Cada PC de alumnos tendra inicialmente dos slots logicos:

```text
PRIMARY
SECONDARY
```

Campos:

- `accountId`: identificador logico estable enviado en comandos futuros; debe ser `PRIMARY` o `SECONDARY`.
- `accountType`: `PRIMARY` o `SECONDARY`.
- `accountReference`: referencia informativa de cuenta local/dominio cuando exista; no es password.
- `configured`: indica si el slot esta configurado en el Client.
- `credentialConfigured`: indica si el Client tiene credencial usable.
- `status`: estado operacional del slot, por ejemplo `READY`, `NOT_CONFIGURED`, `CREDENTIAL_NOT_CONFIGURED` o `UNKNOWN`.

Reglas:

- El Master no almacena passwords de estas cuentas en `classroom.db`.
- El Master no envia passwords en comandos normales.
- La UI futura nunca recibe passwords.
- Logs nunca deben mostrar passwords ni material equivalente.
- La credencial real futura pertenece al Agent Service del Client.
- El almacenamiento futuro del secreto debe protegerse con mecanismos seguros de Windows.
- Los comandos remotos futuros solo enviaran `accountId` logico como `PRIMARY` o `SECONDARY`.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para iniciar sesion.
- El mecanismo productivo de login/cambio de usuario debe disenarse despues con integracion soportada por Windows, contemplando Credential Provider.
- Siempre debe conservarse una via estandar de acceso/recovery de Windows.

Prompt 9.6 solo agrega modelo puro; no implementa passwords, DPAPI, Credential Provider, login/logoff real ni almacenamiento de credenciales.

Prompt 14.2 aclara:

- `SECONDARY` no es kiosco.
- `PRIMARY` no es kiosco.
- Ambos conservan escritorio Windows, mouse, teclado y aplicaciones normales.
- Galtek Service/Session Agent pueden permanecer en background sin bloquear aplicaciones, cambiar archivos del alumno, forzar programas, restringir Windows, cambiar sesion automaticamente ni bloquear input al iniciar clase.
- Cualquier restriccion futura requiere accion administrativa explicita, tipada y auditada.

`ManagedWindowsAccountOperatingPolicy` deja ambos defaults como Windows normal: `restrictedMode=false`, `blockInputOnClassStart=false` y `forceSessionSwitchOnAssignment=false`.

## WindowsSessionState

`WindowsSessionState` representa el estado observado futuro de sesion Windows en un Client:

```text
NO_SESSION
PRIMARY_ACTIVE
SECONDARY_ACTIVE
OTHER_SESSION_ACTIVE
UNKNOWN
```

`OTHER_SESSION_ACTIVE` no debe tratarse como permiso para forzar una cuenta administrada sin preflight y reglas explicitas. `UNKNOWN` bloquea acciones automaticas hasta obtener estado confiable o reportar error operacional.

## ManagedAccountSwitchPlanner

`ManagedAccountSwitchPlanner` planifica una operacion futura `SWITCH_MANAGED_ACCOUNT(targetAccountType)` sobre devices. No inicia sesion, no cierra sesion, no toca Windows y no conoce passwords.

Decisiones por target:

- Cuenta objetivo ya activa: `READY` con accion `NO_CHANGE`.
- Otra cuenta administrada activa: `READY` con accion `SWITCH`.
- Sin sesion: `READY` con accion `LOGON`.
- Device offline/no disponible: `BLOCKED` con accion `PENDING` y error operacional del device, por ejemplo `DEVICE_OFFLINE`.
- Cuenta no configurada: `BLOCKED` con `ACCOUNT_NOT_CONFIGURED`.
- Credencial no configurada: `BLOCKED` con `MANAGED_CREDENTIAL_NOT_CONFIGURED`.
- Sesion desconocida u otra sesion no administrada: `BLOCKED` con `WINDOWS_SESSION_UNKNOWN`.

Ejemplo batch-first:

```text
Objetivo: PRIMARY

PC01 PRIMARY    -> NO_CHANGE
PC02 SECONDARY  -> SWITCH
PC03 NO_SESSION -> LOGON
PC04 PRIMARY    -> NO_CHANGE
PC05 OFFLINE    -> PENDING
```

La maestra ejecuta una sola accion masiva. La UI futura debe permitir actuar solo sobre PCs que necesitan cambio y mostrar cuales quedaron sin cambio.

## Student

`Student` representa al alumno, independiente del equipo.

Campos principales:

- `studentId`: identidad estable.
- `firstName`.
- `lastName`.
- `displayName`: solo visible.
- `grade`.
- `group`.
- `active`.
- `workspaceId`.
- `browserProfileId`.
- `currentDeviceAssignment`.

`displayName` no es identidad.

Persistencia Prompt 08:

- `Student` se conserva aunque se archive.
- `active = false` lo excluye de listados activos, pero no borra historial ni assignments.
- `currentDeviceAssignment` se deriva de `device_assignments`.

API Prompt 10:

- `POST /api/classrooms/{id}/students/batch` registra listas completas y devuelve resultado independiente por fila.
- Un alumno invalido no cancela los demas alumnos del lote.
- Los nombres duplicados estan permitidos; `studentId` sigue siendo la identidad.
- Los listados aceptan filtros `groupId`, `active` y `search`.

## Device != Student

Decision vigente:

```text
Student
  -> DeviceAssignment
  -> Device
```

Un alumno puede cambiar de equipo durante el dia o entre clases. Un equipo puede quedar libre, ocupado o en transicion, pero no se debe mezclar grado/grupo en el nombre de computadora.

## SchoolGroup

`SchoolGroup` representa grupos escolares como `1 A`, `3 B` o `6 A`.

Relaciona:

```text
Group
  -> Students
```

El grupo escolar no debe ser derivado del hostname ni del display name de los equipos.

## DeviceAssignment

`DeviceAssignment` representa la asignacion `Student <-> Device`.

Campos principales:

- `studentId`.
- `deviceId`.
- `assignedAtUtc`.
- `status`.
- `source`.
- `current`.

Reglas:

- Un alumno no debe tener dos assignments actuales simultaneos en la misma sesion operativa.
- Un equipo no debe estar ocupado por dos alumnos simultaneamente.
- `TARGET_OCCUPIED` nunca debe resolverse sobrescribiendo o borrando al alumno que ocupa el equipo.
- Transiciones futuras deben ser explicitas y coordinadas.

`DeviceAssignmentPolicy` valida assignment nuevo contra assignments actuales.

Estrategias de asignacion formalizadas:

```text
LIST_ORDER
RANDOM
PREVIOUS
MANUAL
```

`DeviceAssignment` sigue siendo la fuente de verdad de `Student -> Device`, sin importar la estrategia usada para proponer la asignacion.

Al asignar un alumno a una PC, el flujo futuro conceptual queda:

```text
ASSIGNED
  -> PREPARING_WINDOWS_SESSION
  -> PREPARING_WORKSPACE
  -> PREPARING_BROWSER
  -> APPLYING_CLASS_CONTEXT
  -> READY
```

`StudentPreparationState` permite tambien `PARTIAL_READY`, `RECOVERY_REQUIRED` y `FAILED` por target. `ClassroomReadinessPlan` permite representar aulas parcialmente listas; no existe un unico boolean de aula lista.

Ejemplo:

```text
26 targets
18 READY
4 PREPARING
2 OFFLINE
1 RECOVERY_REQUIRED
1 FAILED
```

La maestra puede comenzar con los 18 `READY`.

Persistencia Prompt 08:

- `device_assignments` es la fuente de verdad del vinculo actual e historico.
- Indices unicos parciales garantizan como maximo un assignment `current = 1` por alumno y por device.
- Mover un assignment actual cierra el anterior y crea uno nuevo dentro de una transaccion.
- No se borran assignments historicos al archivar alumnos.

API Prompt 10:

- `POST /api/assignments` crea un assignment actual de metadata SQLite si el alumno y el device pertenecen al mismo aula y estan libres.
- `POST /api/assignments/{id}/close` cierra un assignment actual y conserva historial.
- `POST /api/assignments/batch` hace preflight completo antes de escribir, detecta conflictos internos del lote y nunca reemplaza automaticamente un target ocupado.
- La API no mueve carpetas ni archivos de `StudentWorkspace`.

## StudentWorkspace

`StudentWorkspace` pertenece al alumno, no a la computadora.

Conceptualmente contiene:

```text
StudentWorkspace
  studentId
  workspaceId
  Documents
  Homework
  Work
  Downloads controlados
  BrowserProfile reference
  metadata
```

Prompt 07 no implementa filesystem real. Solo modela identidad, estado, destinos logicos permitidos y recuperacion planificada.

Prompt 08 persiste metadata del workspace, destinos logicos permitidos y flags de recovery planificado. No crea carpetas reales ni toca archivos de alumno.

Prompt 14.2 fija la arquitectura de residency:

```text
MASTER CANONICAL WORKSPACE
        -> materialize/sync
CLIENT LOCAL WORKING COPY
        -> incremental sync
MASTER
```

No disenar trabajo directo del alumno sobre un share SMB como almacenamiento principal. La working copy local permite seguir trabajando durante fallos temporales de red.

Estados conceptuales:

```text
WorkspaceResidencyState:
NOT_MATERIALIZED
MATERIALIZING
READY
RECOVERY_REQUIRED
ERROR

WorkspaceSyncState:
SYNCED
DIRTY_LOCAL
SYNCING
PENDING_SYNC
RECOVERY_REQUIRED
CONFLICT
ERROR
```

Regla de integridad:

```text
SYNC
  -> VERIFY
  -> COMMIT CANONICAL
  -> CONFIRM
  -> CLEANUP CLIENT
```

Nunca:

```text
DELETE CLIENT
  -> COPY TO MASTER
```

Si desaparece red/energia antes de confirmar, la working copy local se conserva. El sistema debe reportar `PENDING_SYNC` o `RECOVERY_REQUIRED` y no asumir perdida ni `SUCCESS`.

## Destinos logicos

El Master no debe enviar rutas arbitrarias del filesystem a equipos remotos.

Operaciones futuras deben usar:

```text
WORKSPACE_ROOT
DOCUMENTS
HOMEWORK
WORK
DOWNLOADS
DESKTOP
CLASSROOM_SHARED
REMOVABLE_STORAGE
```

Queda prohibido aceptar como destino comun:

```text
C:\Windows\System32
..\..\Windows
\\servidor\...
```

sin una operacion especifica, autorizada y auditada.

Protecciones requeridas para Prompt 08+:

- bloquear path traversal.
- bloquear rutas absolutas arbitrarias.
- escribir solo dentro del scope permitido.
- mapear errores tecnicos a errores operacionales.

Zonas de escritura:

- La politica futura aplica a documentos visibles del alumno, no a bloquear todo Windows.
- Windows y aplicaciones conservan acceso normal a `AppData`, `Temp`, caches y configuracion interna.
- Destinos conceptualmente permitidos para documentos: `StudentWorkspace` y `REMOVABLE_STORAGE` autorizado.
- Destinos no permitidos para documentos: carpetas de otros alumnos, raiz `C:`, `Windows`, directorios administrativos y rutas arbitrarias.
- Office/Chrome/Scratch deberan dirigir guardados visibles al workspace cuando se implemente, sin interceptar todo el filesystem en Prompt 14.2.

## Workspace Recovery

Los workspaces deben soportar recuperacion futura porque los alumnos pequenos pueden borrar, mover, renombrar o sobrescribir archivos.

Capacidades planificadas:

- historial ligero.
- papelera/control de eliminaciones.
- snapshots o backup limitado.
- restaurar ultimo estado valido.
- recuperar archivos enviados por la maestra.

No hay recovery de filesystem real en Prompt 07.

## BrowserProfile

`BrowserProfile` representa configuracion logica del navegador de un alumno.

Campos principales:

- `browserProfileId`.
- `studentId`.
- `browserType`.
- `displayName`.
- `profileStrategy`.
- `profileReference`.
- `status`.
- `portability`.

Browser inicial contemplado: `CHROME`, sin cerrar el diseno a Chrome.

Estrategias:

```text
SYNCED_ACCOUNT
MANAGED_PROFILE
LOCAL_PROFILE
```

Estados/resultados:

```text
BROWSER_PROFILE_NOT_PORTABLE
BROWSER_PROFILE_REAUTH_REQUIRED
BROWSER_REAUTH_REQUIRED
```

Galtek Classroom no debe copiar directamente archivos como `Login Data`, `Cookies` o `Local State` ni extraer secretos. La portabilidad futura debe usar cuentas sincronizadas, perfiles administrados o mecanismos oficiales.

La portabilidad real de sesiones Google/Chrome puede requerir mecanismo seguro/oficial y reautenticacion. No prometer portabilidad copiando perfiles Chrome crudos.

Persistencia Prompt 08:

- Se persiste metadata de perfiles de alumno.
- No existen columnas para passwords, cookies, tokens, cache protegido ni secretos de navegador.

## MasterBrowserProfile

`MasterBrowserProfile` es separado del perfil de alumno.

Representa un perfil local de la maestra, por ejemplo:

```text
browser = CHROME
profile = Maestra Primaria
```

Puede servir para futuras acciones como:

```text
OPEN_URL browserProfileId = MASTER_PRIMARY
```

Galtek no almacena passwords ni cookies.

Persistencia Prompt 08:

- Se persiste metadata del perfil Master y owner SID informativo opcional.
- La base no almacena credenciales, cookies ni secretos.

## MasterWindowsBinding

Un Master queda ligado a una cuenta concreta de Windows mediante SID. Prompt 09 implementa la persistencia y verificacion real en `GaltekClassroom.Agent.Service`.

Modelo:

```text
MasterWindowsBinding
  installationId
  windowsSid
  accountDisplayName
  boundAtUtc
```

Regla de autorizacion local:

```text
Commercial License ACTIVE con rol MASTER
  + Installation Identity correcta
  + binding.installationId == installationIdentity.installationId
  + Windows SID real del caller IPC == SID autorizado
  = MASTER LOCALMENTE AUTORIZADO
```

Username, display name o pertenencia al grupo Administrators no autorizan por si mismos. Otro administrador Windows no hereda automaticamente permisos Master. El `accountDisplayName` es informativo; la identidad fuerte es el SID.

Autoridad final implementada:

- Agent Service persiste/verifica binding local sensible.
- Agent Service obtiene el SID real del cliente conectado al Named Pipe mediante impersonation.
- Master Backend consume estado derivado por IPC y no recalcula autorizacion con datos sueltos.
- UI no es autoridad.

Persistencia Prompt 09:

- Archivo `master-binding.json` en `<CommonApplicationData>\Galtek\Classroom\`.
- Schema v1 con `schemaVersion`, `installationId`, `windowsSid`, `accountDisplayName` y `boundAtUtc`.
- Una instalacion Master tiene exactamente cero o un binding.
- Binding ausente produce `NOT_CONFIGURED` y el Service sigue corriendo.
- Binding corrupto, incompleto, schema desconocido o SID invalido produce `MASTER_BINDING_INVALID` y preserva el archivo.
- Binding de otra instalacion produce `MASTER_BINDING_INSTALLATION_MISMATCH`; no se adopta el `installationId` del archivo.
- La CLI administrativa `--bind-master-current-user` y `--bind-master-account <WINDOWS_ACCOUNT>` requiere elevacion.
- Rebinding requiere `--replace-master-binding`.

Prompt 08 no persiste `MasterWindowsBinding` en SQLite. La base `classroom.db` no debe tener tabla `master_windows_binding`; la autoridad final vive en Agent Service.

## ApplicationDefinition

`ApplicationDefinition` formaliza aplicaciones abribles por catalogo:

```text
applicationId
displayName
type
availability
launchPolicy
```

El Master no debe enviar rutas ejecutables arbitrarias como `C:\algo.exe`. Las operaciones futuras deben enviar `applicationId`.

Las aplicaciones continuan ejecutandose localmente en los Clients. Ejemplos futuros reales: Conejito Lector, Nimbus/libros digitales, RoboMind, Scratch, Word, Excel, PowerPoint y Chrome.

Prompt 08 persiste el catalogo por `applicationId` y permite asociarlo a aulas. No ejecuta aplicaciones.

## ApplicationBinding

`ApplicationBinding` formaliza la vinculacion fisica local del Client:

```text
applicationId
launchType
appPathExecutableName?
executablePath?
enabled
createdAtUtc
updatedAtUtc
```

`ApplicationDefinition` y `ApplicationBinding` son conceptos distintos. El Master conoce y autoriza aplicaciones logicas para el aula; cada Client conserva localmente como abrir esa aplicacion en ese equipo.

Prompt 17A persiste `application-bindings.json` en `<CommonApplicationData>\Galtek\Classroom\` desde `GaltekClassroom.Agent.Service`, separado de identidades, licencia, trust stores y browser policy state. No ejecuta aplicaciones, no agrega Protobuf, no agrega Session Command y no expone paths al Master.

Launch types iniciales:

```text
APP_PATHS
ABSOLUTE_EXE
```

`APP_PATHS` acepta solo un nombre de ejecutable `.exe` sin path, comillas, espacios, argumentos ni separadores. `ABSOLUTE_EXE` acepta solo una ruta local Windows absoluta a `.exe`, no UNC, no relativa, sin `..`, ADS, control chars, comillas, argumentos, wildcards ni placeholders de entorno; al crear o reemplazar se verifica existencia puntual del archivo.

Un `applicationId` puede tener como maximo un binding local. Reemplazar requiere intencion explicita con `--replace-application-binding`; duplicado sin replace devuelve `APPLICATION_BINDING_ALREADY_EXISTS`. Un binding disabled se conserva y una futura fase de launch debera responder `APPLICATION_DISABLED` sin abrir procesos.

Si el archivo esta corrupto, tiene schema desconocido, IDs duplicados o campos incompatibles, el resultado es `APPLICATION_BINDINGS_INVALID`. El Client preserva el archivo y falla cerrado, sin regenerar ni adoptar entradas parciales.

## Action Catalog

Acciones previstas, todas tipadas:

Control:

```text
LOCK_INPUT
UNLOCK_INPUT
SHUTDOWN
RESTART
```

Sesion Windows administrada:

```text
GET_WINDOWS_SESSION_STATE
LOGON_MANAGED_ACCOUNT
LOGOFF_WINDOWS_SESSION
SWITCH_MANAGED_ACCOUNT
```

Aplicaciones:

```text
OPEN_APPLICATION
```

Navegacion:

```text
OPEN_URL
```

Proyeccion:

```text
START_PROJECTION
STOP_PROJECTION
```

Contenido:

```text
DISTRIBUTE_FILE
CREATE_FOLDER
SET_WALLPAPER
RESTORE_WALLPAPER
```

Alumnos:

```text
ASSIGN_STUDENT
MOVE_STUDENT
SWAP_STUDENTS
SYNC_STUDENT_WORKSPACE
RESTORE_STUDENT_WORKSPACE
```

No crear `EXECUTE_COMMAND`, `RUN_COMMAND`, `RUN_POWERSHELL`, `RUN_CMD` ni `EXECUTE_PATH`.

## Remote Operation Framework

Desde Prompt 14 existe un framework Protobuf v1 para operaciones remotas futuras:

```text
OperationRequest
OperationAccepted
OperationResult
OperationStatusQuery
OperationStatusReport
```

Campos base:

- `operationId`.
- `operationType`.
- `targetDeviceId`.
- `protocolVersion`.

El contrato no incluye `command string`, `executablePath`, shell, PowerShell, `cmd`, argumentos arbitrarios ni payload JSON generico de comandos. El Agent deduplica por `operationId`, aplica timeout y devuelve resultados estructurados con `ErrorCode`. Mientras no exista un handler productivo para una operacion, el resultado debe ser `OPERATION_NOT_IMPLEMENTED` y no debe tocar Windows.

Desde Prompt 15A, `SHUTDOWN` y `RESTART` son las primeras operaciones productivas del Agent. Se ejecutan en el Agent Service mediante power control nativo de Windows, sin Session Agent, sin shell, sin procesos externos, sin force-close y sin payload arbitrario enviado por Master. `SUCCESS` en estas operaciones significa que Windows acepto la solicitud con countdown fijo inicial, no que la PC ya se apago o reinicio. Si Windows no acepta la solicitud, el resultado debe ser `FAILED` con un error operacional estructurado como `POWER_CONTROL_UNAVAILABLE` o `POWER_CONTROL_FAILED`.

Desde Prompt 15B, el Master puede enviar `SHUTDOWN` y `RESTART` mediante `POST /api/classrooms/{classroomId}/power-control`. La request acepta solo `type` (`SHUTDOWN` o `RESTART`) y `targetDeviceIds` explicitos, obligatorios, no vacios y sin duplicados. No acepta comandos, rutas, argumentos, timeout, force, mensajes ni payload libre.

El preflight Master es por Device y no cancela los demas targets: el Device debe pertenecer al aula solicitada, estar registrado, conservar binding vigente con Network Identity, tener trust `PAIRED`, no estar `REVOKED`, estar `ONLINE` por conexion autenticada y anunciar `POWER_CONTROL_V1`. La capability solo indica soporte tecnico; no autoriza. Los targets bloqueados quedan como `FAILED` estructurado y los `READY` se envian.

El Master persiste primero una unica `BatchOperation` con el `operationId` del batch y targets `PENDING` o fallidos de preflight. El mismo `operationId` se envia a cada Agent objetivo; la correlacion del Master usa `(deviceId, operationId)` para evitar colisiones entre Devices. `OperationAccepted` no es exito; solo `OperationResult SUCCESS` marca el target como `SUCCESS`.

`OPERATION_REJECTED` puede producirse cuando el Agent responde `OperationAccepted` con estado `REJECTED` o `UNSPECIFIED`, o cuando un `OperationResult` trae el error tipado `OPERATION_REJECTED`. Actualmente no es retryable. No equivale a `OPERATION_RESULT_UNKNOWN`: rechazo significa respuesta tipada del Agent, mientras resultado desconocido significa que no hubo resultado confirmado despues del envio. La decision se toma por enums/codigos tipados, no por comparar texto del mensaje.

Si el Device estaba offline antes de enviar, el target usa `DEVICE_OFFLINE` retryable. Si la request fue enviada pero falta `OperationResult` por timeout, stream cerrado o desconexion, el target falla con `OPERATION_RESULT_UNKNOWN`, no retryable, porque Windows pudo haber aceptado la accion.

Desde Prompt 15C, la reconciliacion de `OPERATION_RESULT_UNKNOWN` para `SHUTDOWN`/`RESTART` consulta read-only el resultado original mediante `OperationStatusQuery(protocolVersion, operationId, targetDeviceId)` sobre el mismo stream autenticado. El Agent responde `OperationStatusReport KNOWN` solo si conserva el `OperationResult` original en cache o un receipt durable minimo de power control aceptado; responde `UNKNOWN` cuando no tiene evidencia. La query nunca llama al handler, nunca modifica Windows, nunca crea un `OperationRequest` nuevo y no renueva indefinidamente la retencion del cache.

Desde Prompt 16A, las operaciones remotas que requieran accion dentro de la sesion interactiva del usuario deben usar el canal local separado `Session Command v1` entre Agent Service y Session Agent. Local IPC v1 permanece read-only y no recibe comandos write. Prompt 16B agrega `OPEN_URL` como comando de sesion tipado: el Service valida URL y envia el comando al Session Agent; el Session Agent valida otra vez y ejecuta la accion visible en la sesion interactiva. Las politicas de navegacion y descarga se aplican desde el Agent Service mediante Registry del usuario interactivo real, no mediante Session Command. Desde Prompt 17B, `OPEN_APPLICATION` tambien usa `Session Command v1` de forma tipada y transporta solo `applicationId`. Desde Prompt 18A, `LOCK_INPUT` y `UNLOCK_INPUT` usan el mismo canal mediante comandos tipados sin payload funcional; las demas acciones interactivas continuan pendientes.

El Master puede reconciliar manualmente con `POST /api/operations/{operationId}/reconcile` o de forma ligera al reconnect autenticado del mismo Device. Solo targets `FAILED + OPERATION_RESULT_UNKNOWN` de operaciones `SHUTDOWN`/`RESTART` pueden cambiar. `KNOWN SUCCESS` cambia el target a `SUCCESS`; `KNOWN FAILED` conserva `FAILED` pero reemplaza el error desconocido por el error real. `UNKNOWN`, timeout de query u offline conservan `OPERATION_RESULT_UNKNOWN`.

Reglas permanentes:

- Nunca reenviar automaticamente `SHUTDOWN` o `RESTART` para comprobar si funciono.
- Nunca inferir `SUCCESS` porque un Device quedo `OFFLINE`.
- Nunca inferir `SUCCESS` porque un Device reconecto despues de `RESTART`.
- No existe `LIKELY_SUCCESS`; la evidencia valida es `OperationResult` o `OperationStatusReport KNOWN`.

Para aceptar una operacion real futura deben cumplirse todas las condiciones: mTLS valido, Master correcto, trust `PAIRED`, no `REVOKED`, Device registrado y `operationType` conocido. Las capabilities informan lo que el Agent soporta; no autorizan la ejecucion.

## Open URL

`OPEN_URL` abre una URL localmente en cada cliente. Desde Prompt 16B es productivo del lado Agent cuando llega como `OperationRequest` tipado sobre el transporte seguro y pasa por `RemoteOperationDispatcher` con Commercial License activa. Desde Prompt 16F2 el Master expone `POST /api/classrooms/{classroomId}/open-url` para despacharlo batch-first.

Modelo:

```text
url
targets
```

El contrato remoto transporta `OpenUrlOperationParameters.url`. El Agent acepta solo URL absoluta `http://` o `https://`, con host no vacio, sin caracteres de control/CR/LF, sin username/password embebidos y longitud maxima inicial de 4096 caracteres. Rechaza `file:`, `javascript:`, `data:`, `ftp:`, `shell:`, handlers `ms-*`, UNC/rutas locales, rutas `C:\...` y URLs relativas. No bloquea `localhost`, IPs privadas ni dominios LAN porque el producto es LAN/offline-first.

El endpoint Master acepta solo `url` y `targetDeviceIds`. La safety estructural de la URL es global: si falla, se rechaza toda la request con `INVALID_URL` y no se crea `BatchOperation`. La URL validada se conserva para el Agent; la normalizacion se usa para comparar policy sin eliminar silenciosamente query o fragment de la navegacion visible.

Antes del fanout, el Master resuelve todos los targets, deriva `groupId` desde el assignment actual `Device -> Student -> Student.groupId`, resuelve una sola policy efectiva con `accountType = null` (`ANY` solamente), evalua `BrowserNavigationPolicyEvaluator` y congela `OpenUrlOperationParameters.url`. Un target bloqueado por policy queda `FAILED URL_BLOCKED_BY_POLICY` y no recibe `OperationRequest`; otros targets continuan. `EXACT_URL` si es evaluable para un `OPEN_URL` concreto y no produce `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`.

El preflight para targets permitidos exige aula correcta, binding vigente, trust `PAIRED`, no `REVOKED`, conexion gRPC/mTLS autenticada `ONLINE` y capability `OPEN_URL_V1`. No exige Chrome, Edge, navegador instalado ni `SESSION_AGENT_AVAILABLE` como sustituto de disponibilidad runtime.

La accion visible la ejecuta el Session Agent mediante el handler registrado de HTTP/HTTPS de la sesion interactiva. El Agent Service, que corre como LocalSystem/Session 0, nunca abre el navegador directamente.

`OPEN_URL SUCCESS` significa que Windows acepto la solicitud de abrir la URL. No significa que Internet funciona, DNS resolvio, la pagina cargo, HTTP devolvio 200 ni que Chrome/Edge mostro contenido correctamente.

Si el Master envio `OperationRequest` al Agent pero no obtuvo `OperationResult`, el resultado es `OPERATION_RESULT_UNKNOWN`. Si el Service ya envio `OPEN_URL` al Session Agent y pierde/expira la respuesta, usa `SESSION_COMMAND_RESULT_UNKNOWN`. Si el Service no puede enviar el comando al Session Agent, usa `SESSION_AGENT_UNAVAILABLE`. Ninguno dispara retry automatico para evitar duplicar pestanas. Desde 16D, despues de la safety estructural y antes del Session Command, el Agent evalua la policy Galtek aplicada al usuario interactivo actual; si bloquea la URL devuelve `URL_BLOCKED_BY_POLICY` y no llama al Session Agent.

Desde Prompt 16D existe enforcement Agent-side para Chrome/Edge mediante registry policy de usuario. Desde Prompt 16E1 existe fuente de verdad persistente del Master para politicas de descarga de navegador. Desde Prompt 16E2B existe handler productivo Agent-side para descargas mediante `DownloadRestrictions` en Chrome/Edge del usuario interactivo real. Desde Prompt 16F1 existe endpoint batch Master para aplicar policies de navegacion y descarga persistidas. Desde Prompt 16F2 existe endpoint batch Master para `OPEN_URL`; no existe seleccion de browser/profile.

## Browser Access Policy

`BrowserAccessPolicy` define que navegacion web permite administrativamente Galtek para un aula. Es distinta de la safety estructural de `OPEN_URL`: una policy nunca puede autorizar `file:`, `javascript:`, `data:` u otro esquema inseguro rechazado por la validacion base.

Campos conceptuales:

```text
policyId
classroomId
name
mode
scopeType
schoolGroupId?
deviceId?
accountScope
active
version
createdAtUtc
updatedAtUtc
```

Modos:

```text
UNRESTRICTED  -> Galtek no restringe por esta policy.
BLOCKLIST     -> permite por default salvo reglas bloqueadas.
ALLOWLIST     -> bloquea por default salvo reglas permitidas.
```

Scopes:

```text
CLASSROOM -> schoolGroupId = null, deviceId = null
GROUP     -> schoolGroupId obligatorio, deviceId = null
DEVICE    -> deviceId obligatorio, schoolGroupId = null
```

Cada policy pertenece siempre a un classroom. `GROUP` y `DEVICE` se validan por IDs y deben pertenecer al classroom. No existe scope `STUDENT` en 16C.

Account scopes:

```text
ANY
PRIMARY
SECONDARY
```

`PRIMARY` y `SECONDARY` no implican modo restringido por si solos. Si el contexto no conoce una cuenta administrada, solo aplican policies `ANY`; no se infiere `PRIMARY`.

Precedencia determinista de una sola policy efectiva:

```text
1. DEVICE + cuenta especifica
2. DEVICE + ANY
3. GROUP + cuenta especifica
4. GROUP + ANY
5. CLASSROOM + cuenta especifica
6. CLASSROOM + ANY
7. ninguna policy -> UNRESTRICTED implicito
```

La policy mas especifica reemplaza a la menos especifica. No se combinan reglas entre policies distintas.

`BrowserUrlRule` contiene:

```text
ruleId
policyId
action: ALLOW | BLOCK
matchType: HOST_EXACT | HOST_SUFFIX | URL_PREFIX | EXACT_URL
pattern
enabled
description?
createdAtUtc
updatedAtUtc
```

No hay regex arbitraria ni wildcards libres. `HOST_SUFFIX` respeta frontera de label DNS: `youtube.com` coincide con `www.youtube.com`, pero no con `evilyoutube.com` ni `youtube.com.evil.test`. `URL_PREFIX` y `EXACT_URL` usan URLs absolutas seguras normalizadas, sin fragment. `EXACT_URL` conserva query para permitir contenido concreto.

Evaluacion pura:

```text
URL invalida        -> BLOCK / INVALID_URL
sin policy          -> ALLOW / NO_POLICY
UNRESTRICTED        -> ALLOW / UNRESTRICTED
ALLOW matching      -> ALLOW / EXPLICIT_ALLOW
BLOCK matching      -> BLOCK / EXPLICIT_BLOCK
BLOCKLIST sin match -> ALLOW / DEFAULT_ALLOW
ALLOWLIST sin match -> BLOCK / DEFAULT_BLOCK
```

Dentro de una sola policy, gana el filtro mas especifico por host, scheme/port, path y query; solo ante igual especificidad `ALLOW` explicito gana a `BLOCK` explicito. Esto permite bloquear `youtube.com` y autorizar una URL/prefix mas especifica, pero tambien permite bloquear una URL/prefix mas especifica aunque exista un allow broad del host.

`EXACT_URL` permanece persistible en Master, pero no es enforceable por Chrome/Edge nativo en 16D porque no existe equivalencia general byte-for-byte segura en `URLBlocklist`/`URLAllowlist`. Un apply activo que incluya `EXACT_URL` devuelve `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`.

`accountScope = ANY` aplica al usuario Windows interactivo actual. `PRIMARY` y `SECONDARY` siguen modelados, pero en 16D devuelven `BROWSER_ACCOUNT_SCOPE_UNRESOLVED` porque aun no existe binding productivo `PRIMARY/SECONDARY -> Windows SID`.

Desde Prompt 16F1, `POST /api/classrooms/{classroomId}/browser-policies/apply` aplica la policy efectiva persistida por target. La request acepta solo `targetDeviceIds` explicitos, obligatorios, no vacios, sin blancos, sin duplicados y sin campos adicionales. El Master usa `accountType = null`, por lo que solo resuelve `ANY`; deriva `groupId` desde el assignment actual del Device hacia su Student; congela los parametros Protobuf tipados antes del fanout; persiste una sola `BatchOperation`; y envia solo a targets que pasan preflight de aula, binding vigente, trust `PAIRED` no `REVOKED`, conexion autenticada `ONLINE` y capability `BROWSER_NAVIGATION_POLICY_V1`. Un `EXACT_URL` habilitado en la policy efectiva bloquea solo ese target con `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE` antes de enviar.

No confundir con `START_PROJECTION`, donde el Master reproduce y transmite su pantalla.

Para YouTube o contenido web, preferir:

```text
Master envia URL
  -> cada Client abre Chrome localmente
```

No preferir:

```text
Master reproduce
  -> captura 30 FPS
  -> transmite a 26 PCs
```

## Browser Download Policy

`BrowserDownloadPolicy` define restricciones administrativas de descargas de navegador para un aula. Es un dominio distinto de `BrowserAccessPolicy`: navegacion decide cargas de URL; descarga decide el comportamiento futuro de browser downloads cubiertos por `DownloadRestrictions`.

Campos conceptuales:

```text
policyId
classroomId
name
restrictionMode
scopeType
schoolGroupId?
deviceId?
accountScope
active
version
createdAtUtc
updatedAtUtc
```

Restriction modes:

```text
NO_SPECIAL_RESTRICTIONS
BLOCK_DANGEROUS
BLOCK_POTENTIALLY_DANGEROUS
BLOCK_ALL
BLOCK_MALICIOUS
```

Mapping nativo de `DownloadRestrictions`: `NO_SPECIAL_RESTRICTIONS = 0`, `BLOCK_DANGEROUS = 1`, `BLOCK_POTENTIALLY_DANGEROUS = 2`, `BLOCK_ALL = 3`, `BLOCK_MALICIOUS = 4`. La API expone el enum Galtek, no el numero Chromium como autoridad.

`NO_SPECIAL_RESTRICTIONS` significa que Galtek no agrega restricciones especiales de descarga; no significa desactivar Safe Browsing ni toda seguridad del navegador.

16E2A agrega la operacion remota tipada `APPLY_BROWSER_DOWNLOAD_POLICY` con parametros Protobuf `policy_id`, `policy_version`, `implicit_no_special_restrictions`, `restriction_mode` y `account_scope`. 16E2B registra el handler productivo Agent-side. No acepta JSON generico, valores nativos 0-4 enviados directamente, Registry path/value/key, SID, username, browser executable, command, arguments ni script.

`ChromiumDownloadPolicyCompiler` traduce el enum Galtek a valor nativo y genera un hash determinista de la semantica efectiva. El hash diferencia `NO_SPECIAL_RESTRICTIONS` implicito de una policy explicita con valor nativo `0`.

Desde Prompt 16F1, `POST /api/classrooms/{classroomId}/browser-download-policies/apply` aplica la policy efectiva persistida por target. La request acepta solo `targetDeviceIds` explicitos, obligatorios, no vacios, sin blancos, sin duplicados y sin campos adicionales. El Master usa `accountType = null`, por lo que solo resuelve `ANY`; deriva `groupId` desde el assignment actual del Device hacia su Student; congela `ApplyBrowserDownloadPolicyOperationParameters` antes del fanout; conserva la diferencia entre ausencia de policy (`implicit_no_special_restrictions = true`) y policy explicita `NO_SPECIAL_RESTRICTIONS`; persiste una sola `BatchOperation`; y envia solo a targets que pasan preflight de aula, binding vigente, trust `PAIRED` no `REVOKED`, conexion autenticada `ONLINE` y capability `BROWSER_DOWNLOAD_POLICY_V1`.

Scopes:

```text
CLASSROOM -> schoolGroupId = null, deviceId = null
GROUP     -> schoolGroupId obligatorio, deviceId = null
DEVICE    -> deviceId obligatorio, schoolGroupId = null
```

`GROUP` y `DEVICE` deben pertenecer al classroom de la policy. No existe scope `STUDENT`.

Account scopes:

```text
ANY
PRIMARY
SECONDARY
```

Si el contexto no conoce una cuenta administrada, solo aplican policies `ANY`; no se infiere `PRIMARY`.

Precedencia determinista de una sola policy efectiva:

```text
1. DEVICE + cuenta especifica
2. DEVICE + ANY
3. GROUP + cuenta especifica
4. GROUP + ANY
5. CLASSROOM + cuenta especifica
6. CLASSROOM + ANY
7. ninguna policy -> NO_SPECIAL_RESTRICTIONS implicito
```

La policy mas especifica reemplaza completamente a la menos especifica. No se combinan valores numericos.

Si no existe policy efectiva, el contrato usa `implicit_no_special_restrictions = true` y `restrictionMode = NO_SPECIAL_RESTRICTIONS`; el compilador devuelve `RemoveGaltekPolicy = true` y no produce valor nativo. Si existe una policy explicita `NO_SPECIAL_RESTRICTIONS`, el compilador devuelve `RemoveGaltekPolicy = false` y `NativeDownloadRestrictionsValue = 0`.

`accountScope = ANY` aplica al usuario Windows interactivo real. `PRIMARY` y `SECONDARY` permanecen tipados pero devuelven `BROWSER_ACCOUNT_SCOPE_UNRESOLVED` hasta existir binding seguro Managed Account -> Windows SID.

`BROWSER_DOWNLOAD_POLICY_V1` se anuncia en `ClientHello` desde 16E2B porque `ApplyBrowserDownloadPolicyOperationHandler` esta registrado y verifica Registry/state de forma productiva.

El handler escribe `DownloadRestrictions` como `REG_DWORD` bajo `HKEY_USERS\<SID>\Software\Policies\Google\Chrome` y `HKEY_USERS\<SID>\Software\Policies\Microsoft\Edge`, preservando policies externas. Descargas mantiene state y journal separados de navegacion: `browser-download-policy-state.json` y `browser-download-policy-apply.json`.

Como `DownloadRestrictions` es un value dentro de la parent key, no existe ACL por value. Galtek endurece solo la parent key cuando puede demostrar que es seguro, nunca reescribe ACL recursivamente ni toca child subkeys. `URLBlocklist`/`URLAllowlist` solo cuentan como contenido permitido cuando ownership de navegacion 16D lo demuestra.

`APPLY_BROWSER_DOWNLOAD_POLICY SUCCESS` significa valor aplicado/removido en Chrome y Edge, read-back correcto, durable state confirmado y journal limpio. No significa navegador instalado/abierto, tabs refrescadas, descarga historica eliminada ni DLP completo. Recovery por power loss es lazy en el siguiente apply, sin startup scan, timer ni polling.

`BLOCK_ALL` solo cubre las descargas administradas por `DownloadRestrictions`. No promete DLP ni bloquea Save Page As, Print to PDF, clipboard, filesystem writes o network traffic por fuera de esa policy.

16E1 no modela `blockedExtensions`, `allowedExtensions`, `blockedMimeTypes` ni `allowedMimeTypes`. Galtek necesita semantica equivalente en Chrome y Edge sobre Windows, y no existe actualmente una policy nativa comun soportada en Windows para bloqueo arbitrario por extension en ambos navegadores. Edge tiene `DownloadBlockedForFileTypes`, pero no esta soportada en Windows actualmente.

En modo `BLOCK_ALL`, una descarga futura autorizada por maestra no debe implementarse desbloqueando temporalmente el navegador ni automatizando clicks. La direccion permanente es entregar contenido por una operacion Galtek tipada/controlada hacia un destino logico autorizado de `StudentWorkspace`, posiblemente reutilizando `DISTRIBUTE_FILE`.

## Operaciones de contenido

`DISTRIBUTE_FILE` modela:

```text
sourceFile
logicalDestination
targets
conflictPolicy
openAfterDistribution
```

`ConflictPolicy`:

```text
SKIP
REPLACE
RENAME
```

`CREATE_FOLDER` usa destinos logicos del workspace y debe ser idempotente cuando sea seguro.

`SET_WALLPAPER` y `RESTORE_WALLPAPER` quedan modeladas como acciones previstas, sin aplicar wallpapers reales.

La distribucion futura debe ser batch-first:

```text
Teacher Content
  -> uno/muchos Students
  -> StudentWorkspace
  -> opcional OPEN_AFTER_DISTRIBUTION
```

Una PC fallida no cancela las demas. La arquitectura futura debe contemplar hash/checksum, skip si ya existe, resume, staging, verify, atomic commit y concurrencia limitada. El Master no debe enviar pesadamente el mismo contenido de forma ingenua si puede cachearse o reutilizarse.

Politica de transferencia por performance:

- `LEGACY`: una transferencia pesada por vez, chunks moderados y rate/concurrency limitada.
- `STANDARD`: puede aceptar mayor throughput, siempre acotado.
- `MASTER_BALANCED`: limita fanout global, evita saturar switch/disco y permite que HIGH/CRITICAL preempte o throttlee trabajo LOW.

## Projection modes

`ProjectionMode` diferencia costos:

```text
SCREEN_SHARE
WHITEBOARD
POINTER
LOCAL_MEDIA
OPEN_WEB_CONTENT
```

Reglas:

- `SCREEN_SHARE`: stream real futuro.
- `WHITEBOARD`: enviar eventos/vector drawing, no frames completos.
- `POINTER`: enviar coordenadas/eventos ligeros.
- `OPEN_WEB_CONTENT`: abrir URL localmente en Chrome del Client cuando sea posible.
- `LOCAL_MEDIA`: preferir distribucion/cache local y reproduccion sincronizada cuando sea posible.

No tratar todo como captura de pantalla.

## Preview de PCs

Politica futura:

- Boot/idle: 0 capturas.
- Classroom overview: thumbnails pequenos, baja frecuencia, solo Devices visibles y concurrencia limitada.
- `LEGACY`: thumbnail muy pequeno y frecuencia baja.
- `STANDARD`: frecuencia ligeramente superior si hay capacidad real.
- Selected Device: elevar calidad/frecuencia temporalmente.
- No iniciar captura al boot ni por `ClientHello`.
- Nunca mantener 26 PCs x 30 FPS siempre.

No hay captura implementada en Prompt 14.2.

## Prioridades operacionales

`OperationPriority` formaliza:

```text
CRITICAL
HIGH
NORMAL
LOW
```

Orden:

```text
CRITICAL > HIGH > NORMAL > LOW
```

Ejemplos:

- `CRITICAL`: `UNLOCK_INPUT`, `STOP_PROJECTION`, recovery de control.
- `HIGH`: preparar `PRIMARY`, asignar alumno, recuperar/sincronizar workspace necesario para iniciar clase.
- `NORMAL`: distribuir actividad, `OPEN_APPLICATION`, `OPEN_URL`.
- `LOW`: thumbnails, inventario, metadata secundaria, prefetch.

## OPEN_APPLICATION

`OPEN_APPLICATION` abre una aplicacion autorizada localmente en el Client mediante un `applicationId` logico. El Master/Protobuf, Agent Service y Session Command no transportan rutas ejecutables, command line, argumentos, working directory, shell, URI ni payload generico.

Desde Prompt 17C, el Master expone `POST /api/classrooms/{classroomId}/open-application`. La request acepta solo `applicationId` y `targetDeviceIds`; rechaza paths, executable names, launch types, argumentos, command line, working directory, shell, PowerShell, `cmd`, registry path, account/student/group context y payload libre. `MasterAccessGuard.requireAuthorized()` se ejecuta antes de leer Classroom, `ApplicationDefinition`, asociacion aula/aplicacion, Devices o bindings de red.

El Master resuelve el `applicationId` contra una `ApplicationDefinition` activa persistida y exige que el `classroomId` tenga esa app autorizada por `classroom_applications`. Si la app no existe, esta archivada/inactiva o no esta asociada al Classroom, la request completa se rechaza antes de crear `BatchOperation` y no se envia a ningun Device. `availability` y `launchPolicy` permanecen metadata descriptiva porque no existe enforcement de dominio vigente que bloquee launch por esos campos.

El preflight por target exige Device del aula, binding de red vigente, trust `PAIRED`, no `REVOKED`, conexion gRPC/mTLS autenticada `ONLINE` y capability `OPEN_APPLICATION_V1`. No exige `SESSION_AGENT_AVAILABLE`: la disponibilidad real del Session Agent se confirma durante la ejecucion Agent-side y puede devolver `SESSION_AGENT_UNAVAILABLE`.

El flujo productivo Agent-side es:

```text
OperationRequest OPEN_APPLICATION(applicationId)
  -> RemoteOperationDispatcher con licencia comercial activa
  -> OpenApplicationOperationHandler
  -> ApplicationBindingStore local
  -> Session Command OPEN_APPLICATION(applicationId)
  -> Session Agent
  -> read-only application-bindings.json
  -> resolucion fisica segura
  -> CreateProcessW en la sesion interactiva
```

`APPLICATION_BINDING_NOT_FOUND` cubre catalogo ausente o `applicationId` inexistente. `APPLICATION_BINDINGS_INVALID` cubre JSON/schema/catalogo corrupto. `APPLICATION_DISABLED` no resuelve target ni comprueba executable. `APPLICATION_EXECUTABLE_NOT_FOUND` significa que el `.exe` autorizado no existe al momento de ejecucion. `APPLICATION_LAUNCH_FAILED` significa que Windows rechazo la creacion del proceso con el token normal del Session Agent.

`APP_PATHS` solo usa HKLM App Paths, valor default, Registry64/Registry32 cuando aplica, y falla cerrado si ambas vistas resuelven distinto. No se consulta HKCU, PATH, Program Files, Start Menu, WindowsApps, uninstall keys, procesos ni discos. `ABSOLUTE_EXE` se revalida como path Windows local absoluto `.exe` y se comprueba con `File.Exists` justo antes del launch.

`SUCCESS` solo significa que Windows acepto crear el proceso; no implica ventana visible, foreground, app estable, documento cargado, respuesta de la app ni instancia unica. No hay polling, dedupe por proceso, monitoring ni `WaitForExit`. Duplicados del mismo `operationId + applicationId` devuelven el resultado cacheado; mismo `operationId` con otro `applicationId` falla como conflicto vigente. Si el Session Command fue enviado y se pierde la respuesta, el resultado es `SESSION_COMMAND_RESULT_UNKNOWN` y no hay retry automatico.

El Master persiste una sola `BatchOperation` `OPEN_APPLICATION` antes del fanout, con payload minimo `schemaVersion = 1` y JSON `{"schemaVersion":1,"applicationId":"..."}`. Usa el mismo `operationId` para todos los Agents y envia `OpenApplicationOperationParameters.applicationId` tipado por `MasterRemoteOperationGateway`. `OPERATION_RESULT_UNKNOWN` significa que el Master envio al Agent pero no recibio `OperationResult`; `SESSION_COMMAND_RESULT_UNKNOWN` significa que el Agent envio al Session Agent pero no confirmo la respuesta. No hay retry automatico ni `OperationStatusQuery`/reconciliacion para `OPEN_APPLICATION`.

Una transferencia grande nunca debe impedir una operacion `CRITICAL`. No hay scheduler real en Prompt 14.2.

## INPUT_CONTROL

`LOCK_INPUT` y `UNLOCK_INPUT` controlan conjuntamente teclado y mouse del usuario interactivo actual. No hay modos separados de teclado, mouse, touch, duracion, mensaje, lease, overlay ni timeout configurable.

El flujo productivo Agent-side es:

```text
OperationRequest LOCK_INPUT / UNLOCK_INPUT
  -> RemoteOperationDispatcher
  -> LockInputOperationHandler / UnlockInputOperationHandler
  -> Session Command LOCK_INPUT / UNLOCK_INPUT
  -> Session Agent
  -> WindowsInputBlockCoordinator
  -> User32.dll BlockInput(BOOL)
```

`LOCK_INPUT` aumenta control sobre el usuario y requiere Commercial License `ACTIVE`. `UNLOCK_INPUT` revierte control y queda recovery-safe: no se bloquea por licencia expirada/inactiva/no resuelta, pero sigue exigiendo mTLS valido, Master esperado, trust `PAIRED`, no `REVOKED`, Device registrado/correcto, operacion conocida, dispatcher seguro y Session Command autenticado LocalSystem -> Session Agent.

Desde Prompt 18B1, el origen Master tambien tiene una primitive interna recovery-safe: `GET_MASTER_UNLOCK_AUTHORIZATION` por Local IPC v1. La evalua exclusivamente el Agent Service local con Installation Identity valida, Master Windows Binding existente y valido, `installationId` coincidente y SID real del caller Named Pipe obtenido por impersonation. Esta autorizacion no exige Commercial License local Master `ACTIVE`, no lee claims/roles de una licencia invalida, no permite a cualquier administrador desbloquear y no acepta SID declarado por payload, headers, username o parametros. La respuesta contiene solo `status`, `authorized` y `configured`.

En el Master Backend, `MasterAccessGuard.requireAuthorized()` conserva la autorizacion administrativa normal con licencia activa. `MasterUnlockAccessGuard.requireUnlockAuthorized()` es separado, sin fallback entre guards, y solo puede usarse en acciones que reducen control y hayan sido declaradas recovery-safe; actualmente eso significa unicamente el futuro `UNLOCK_INPUT`. Prompt 18B1 no agrega endpoint HTTP, batch, dispatch gRPC ni UI para input control.

El Agent Service corre como LocalSystem/Session 0 y nunca llama `BlockInput`. El Session Agent es la autoridad fisica. `WindowsInputBlockCoordinator` crea un worker dedicado solo al primer lock real; ese mismo thread llama `BlockInput(TRUE)`, permanece vivo bloqueado/event-driven mientras hay lock Galtek activo, procesa locks repetidos en el mismo thread para reassertar tras `CTRL+ALT+DEL`, llama `BlockInput(FALSE)` para unlock y termina. Estado desbloqueado normal: cero threads extra, cero polling, cero timer, cero writes.

`LOCK_INPUT SUCCESS` significa que Windows confirmo `BlockInput(TRUE)` o que el owner thread confirmo el estado deseado en un lock repetido. `UNLOCK_INPUT SUCCESS` significa que no habia lock Galtek activo o que Windows confirmo `BlockInput(FALSE)`. `INPUT_LOCK_FAILED` e `INPUT_UNLOCK_FAILED` son errores estructurados; si unlock falla, el worker termina igualmente para favorecer el fail-safe de Windows por salida de thread/proceso.

`CTRL+ALT+DEL` es un escape nativo de Windows y no se intenta bloquear. Si el Session Agent muere, Windows libera input; al reiniciar empieza logicamente `UNLOCKED`. Si solo reinicia el Agent Service y Session Agent sigue vivo, el lock puede seguir activo y un `UNLOCK_INPUT` posterior puede desbloquearlo. No hay persistence, startup lock ni reconstruccion desde disco.

## MOVE_STUDENT

La maestra piensa:

```text
Alicia -> PC12
```

No:

```text
copiar carpetas de PC03 a PC12
```

Workflow planificado:

```text
PLANNED
PREFLIGHT
COPYING
VERIFYING
PREPARING_TARGET
COMMITTING
COMPLETED
FAILED
ROLLING_BACK
ROLLED_BACK
```

Regla futura:

```text
COPY -> VERIFY -> COMMIT -> CLEANUP
```

Nunca:

```text
DELETE SOURCE -> COPY
```

Si falla la transferencia, la fuente se preserva y debe existir retry.

`StudentMovePlanner` solo planifica. Detecta como minimo:

- `STUDENT_NOT_ASSIGNED`.
- `TARGET_OCCUPIED`.
- `SOURCE_DEVICE_UNAVAILABLE`.
- `TARGET_DEVICE_UNAVAILABLE`.
- `WORKSPACE_BUSY`.

Mover `Santiago PC07 -> PC18` debe significar conceptualmente:

1. preservar/sincronizar PC07 cuando sea posible;
2. usar la ultima copia canonica segura;
3. preparar PC18;
4. actualizar assignment solo siguiendo workflow consistente;
5. no bloquear toda la clase si PC07 esta muerta.

Si hay cambios locales potencialmente no sincronizados, reportarlo claramente. No borrar datos para completar un move.

## SWAP_STUDENTS

`SWAP_STUDENTS` es una operacion compuesta/transaccional, no dos moves sin coordinacion.

Workflow conceptual:

```text
Preflight ambos
  -> preparar staging
  -> copiar ambos workspaces
  -> verificar ambos
  -> commit assignments
  -> cleanup
```

Si falla antes de commit, no cambian assignments.

`StudentSwapPlanner` valida:

- ambos asignados.
- estudiantes distintos.
- devices distintos.
- devices disponibles.
- workspaces no ocupados por otra operacion.

## BatchOperation

Toda operacion masiva debe poder terminar como:

```text
SUCCESS
PARTIAL_SUCCESS
FAILED
CANCELLED
ROLLED_BACK
```

`BatchOperation` contiene:

```text
operationId
type
requestedBy
createdAtUtc
targetCount
status
targets
```

Cada target contiene:

```text
targetId
status
errorCode
message
attempt
```

`NO_CHANGE` es un resultado valido por target para operaciones idempotentes como `SWITCH_MANAGED_ACCOUNT(PRIMARY)`: significa que el equipo ya estaba en el estado objetivo, cuenta como exito y no entra en retry.

La UI futura debe permitir reintentar solo fallidos y no repetir manualmente los exitosos.

Prompt 08 persiste `BatchOperation` y `BatchTargetResult` con `operationId`, targets, estados, errores, mensaje operacional, `attempt` y payload JSON versionado. El retry se calcula solo sobre targets fallidos cuyo `ErrorCode` sea retryable.

Prompt 15B reutiliza esa persistencia para power control. Una accion de maestra crea una sola `BatchOperation` `SHUTDOWN` o `RESTART`, aun cuando se envie a varios Devices. Al finalizar, todos los targets `SUCCESS` producen batch `SUCCESS`; mezcla de exitos y fallos produce `PARTIAL_SUCCESS`; todos fallidos produce `FAILED`.

API Prompt 10:

- `GET /api/operations`, `GET /api/operations/{id}` y `GET /api/operations/{id}/retryable-targets` exponen operaciones persistidas para la UI futura.
- `assignments/batch` registra una operacion `ASSIGN_STUDENT` con resultados por fila.
- `POST /api/classrooms/{classroomId}/power-control` registra operaciones batch `SHUTDOWN`/`RESTART` y `GET /api/operations/{id}` puede leer el resultado persistido.

Ejemplo futuro:

```text
SWITCH_MANAGED_ACCOUNT(PRIMARY) sobre 25 PCs

21 NO_CHANGE
3 SUCCESS
1 FAILED
```

El retry posterior debe actuar solo sobre el target `FAILED` si su `errorCode` es retryable; nunca debe repetir los 21 `NO_CHANGE` ni los 3 `SUCCESS`.

## Preflight general

Antes de ejecutar una operacion masiva, el sistema debe clasificar targets:

```text
READY
WARNING
BLOCKED
```

Ejemplo:

```text
22 READY
2 OFFLINE
1 LICENSE_NOT_ACTIVE
```

La maestra puede ejecutar en los disponibles sin resolver primero todos los bloqueados.

`BatchOperationPlanner` modela esta clasificacion para devices.

## Manejo de errores

Los errores se modelan con `ErrorCode`, categoria y bandera `retryable`.

Categorias:

- Device.
- License.
- Student.
- Workspace.
- Browser.
- Application.
- Content.
- Windows Account.
- Windows Session.
- Authorization.
- Operation.
- Persistence.

Separar siempre:

```text
errorCode
```

de:

```text
mensaje para usuario
```

La UI futura no debe decidir comparando textos ni mostrar excepciones tecnicas como `IOException`, `SocketException`, `Win32Exception` o stack traces.

SQLite debe mapear excepciones tecnicas a codigos operacionales como `MASTER_DATABASE_UNAVAILABLE`, `MASTER_DATABASE_CORRUPT`, `MASTER_DATABASE_MIGRATION_FAILED`, `MASTER_DATABASE_BUSY`, `MASTER_STORAGE_FULL`, `PERSISTENCE_CONSTRAINT_VIOLATION` o `CONCURRENT_MODIFICATION`.

La API administrativa de Prompt 10 usa un `RestControllerAdvice` uniforme: validacion `400`, no encontrado `404`, conflictos/version `409`, Master no autorizado `403`, Agent/storage no disponible `503`.

Errores de cuentas/sesion Windows administrada formalizados en Prompt 9.6:

```text
ACCOUNT_NOT_CONFIGURED
MANAGED_CREDENTIAL_NOT_CONFIGURED
WINDOWS_SESSION_UNKNOWN
WINDOWS_LOGON_FAILED
WINDOWS_LOGOFF_FAILED
SESSION_SWITCH_FAILED
CREDENTIAL_PROVIDER_UNAVAILABLE
```

`DEVICE_OFFLINE` sigue siendo el error correcto cuando el Client no esta disponible.

## Retry e idempotencia

Errores potencialmente reintentables:

```text
DEVICE_OFFLINE
AGENT_UNAVAILABLE
SESSION_NOT_AVAILABLE temporal
TRANSFER_FAILED
FILE_WRITE_FAILED
POWER_CONTROL_FAILED
WINDOWS_SESSION_UNKNOWN
WINDOWS_LOGON_FAILED
WINDOWS_LOGOFF_FAILED
SESSION_SWITCH_FAILED
```

Errores que requieren intervencion:

```text
INVALID_URL
APPLICATION_NOT_INSTALLED
TARGET_OCCUPIED
INSUFFICIENT_DISK_SPACE
ACCOUNT_NOT_CONFIGURED
MANAGED_CREDENTIAL_NOT_CONFIGURED
CREDENTIAL_PROVIDER_UNAVAILABLE
POWER_CONTROL_UNAVAILABLE
CAPABILITY_NOT_SUPPORTED
OPERATION_NOT_IMPLEMENTED
OPERATION_REJECTED
OPERATION_RESULT_UNKNOWN
```

No debe existir retry infinito.

Las operaciones futuras deben llevar `operationId` como idempotency key para tolerar reintentos sin duplicar efectos, por ejemplo `CREATE_FOLDER`.

## Failure model

Condiciones normales desde ahora:

- Client offline.
- Perdida de red.
- Master temporalmente no disponible.
- Client reiniciado.
- Operacion sin ACK.
- Corte electrico.
- HDD extremadamente lento.
- Almacenamiento lleno.
- Archivo parcialmente transferido.

Los workflows futuros deben ser idempotentes cuando aplique, reconciliar estado, no asumir `SUCCESS` sin confirmacion, conservar origen antes de commit, permitir partial success y permitir retry solo donde corresponde.

Prompt 14.4 agrega base tecnica de recovery/startup y modelos de semantica incierta. Prompt 15C implementa reconciliacion productiva para power control incierto; los workflows reales de filesystem, sync y transferencias siguen pendientes para fases posteriores.

## Operaciones destructivas

Acciones destructivas o de alto impacto:

```text
SHUTDOWN
REPLACE_FILE
MOVE_STUDENT
SWAP_STUDENTS
LOGOFF_WINDOWS_SESSION
SWITCH_MANAGED_ACCOUNT
PURGE/DELETE futuro
```

deben:

- requerir preflight.
- confirmar una sola vez por operacion masiva.
- preservar datos cuando sea posible.
- permitir rollback cuando corresponda.
- registrar resultado.

Acciones de recuperacion que liberan al usuario, como `UNLOCK_INPUT` y `STOP_PROJECTION`, no deben bloquearse unicamente porque la licencia haya expirado.

## Auditoria futura

Toda accion administrativa futura debe poder registrar:

```text
who
what
when
targets
result
```

Prompt 08 persiste metadata de operaciones batch y resultados por target, pero no implementa auditoria administrativa completa ni ejecucion real.
