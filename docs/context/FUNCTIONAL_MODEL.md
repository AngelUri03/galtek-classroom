# Modelo funcional

Este documento es obligatorio para agentes futuros antes de disenar funcionalidades operativas de Galtek Classroom.

Prompt 07 define el dominio funcional, modelos puros y planners de preflight. Prompt 08 persiste ese dominio en SQLite local para el Master. Prompt 10 expone la primera API administrativa protegida sobre SQLite con bootstrap, snapshot, CRUD escolar, batches de alumnos y assignments de metadata. Prompt 9.6 formaliza cuentas Windows administradas futuras en Clients (`PRIMARY`/`SECONDARY`) y cambio masivo de sesion como dominio puro. Prompt 14.2 fija el modelo operativo real del aula, readiness progresiva, workspace canonico Master/local working copy Client, prioridades y modos de proyeccion como dominio puro. No implementa filesystem real, browser automation, UI, login/logoff Windows, USB, captura, proyeccion ni comandos remotos funcionales.

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
- 8 GB RAM.
- SSD 500 GB.

Clients:

- Legacy aprox. 16: Celeron/Core Duo/Pentium o similar, 4 GB RAM, HDD 256 GB, muy lentos.
- Renovados aprox. 10: Core i5 6a generacion, 8 GB RAM, SSD 256 GB.

El Master absorbe orquestacion, almacenamiento canonico de trabajos, metadata escolar, manifests/checksums futuros, distribucion de contenido, coordinacion batch, recuperacion y estado del aula. Word, Chrome, Scratch, RoboMind, Office y aplicaciones interactivas de alumnos corren localmente en cada Client.

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
- Capabilities conocidas actuales: `HEARTBEAT_V1`, `OPERATION_FRAMEWORK_V1` y `SESSION_AGENT_AVAILABLE`.
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
```

Campos base:

- `operationId`.
- `operationType`.
- `targetDeviceId`.
- `protocolVersion`.

El contrato no incluye `command string`, `executablePath`, shell, PowerShell, `cmd`, argumentos arbitrarios ni payload JSON generico de comandos. El Agent deduplica por `operationId`, aplica timeout y devuelve resultados estructurados con `ErrorCode`. Mientras no exista un handler productivo para una operacion, el resultado debe ser `OPERATION_NOT_IMPLEMENTED` y no debe tocar Windows.

Para aceptar una operacion real futura deben cumplirse todas las condiciones: mTLS valido, Master correcto, trust `PAIRED`, no `REVOKED`, Device registrado y `operationType` conocido. Las capabilities informan lo que el Agent soporta; no autorizan la ejecucion.

## Open URL

`OPEN_URL` abre una URL localmente en cada cliente.

Modelo:

```text
url
browserProfileId
targets
```

Prompt 07 valida esquemas `http` y `https`; rechaza `file`, `javascript` y `data`.

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

- Classroom overview: thumbnails pequenos, baja frecuencia, solo Devices visibles y concurrencia limitada.
- Selected Device: elevar calidad/frecuencia temporalmente.
- No iniciar captura al boot ni por `ClientHello`.

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

Una transferencia grande nunca debe impedir una operacion `CRITICAL`. No hay scheduler real en Prompt 14.2.

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

API Prompt 10:

- `GET /api/operations`, `GET /api/operations/{id}` y `GET /api/operations/{id}/retryable-targets` exponen operaciones persistidas para la UI futura.
- `assignments/batch` registra una operacion `ASSIGN_STUDENT` con resultados por fila.

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

Prompt 14.2 no implementa recovery tecnico de apagones; eso queda para Prompt 14.4.

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
