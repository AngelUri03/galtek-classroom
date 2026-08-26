# Modelo funcional

Este documento es obligatorio para agentes futuros antes de disenar funcionalidades operativas de Galtek Classroom.

Prompt 07 define el dominio funcional, modelos puros y planners de preflight. No implementa persistencia, red, filesystem real, browser automation, UI ni comandos remotos.

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

Prompt 07 solo modela la entidad y su configuracion. No hay persistencia SQLite todavia.

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

No usar IP, MAC ni hostname como identidad primaria o de seguridad. La identidad de red criptografica llegara en una fase posterior.

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

## MasterWindowsBinding

Un Master queda ligado a una cuenta concreta de Windows mediante SID.

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
Commercial License contiene MASTER
  + Installation Identity correcta
  + Windows SID actual == SID autorizado
  = MASTER LOCALMENTE AUTORIZADO
```

Username, display name o pertenencia al grupo Administrators no autorizan por si mismos. Otro administrador Windows no hereda automaticamente permisos Master.

Autoridad final planificada:

- Agent Service persiste/verifica binding local sensible.
- Master Backend consume estado derivado por IPC.
- UI no es autoridad.

Prompt 07 no agrega IPC write ni persistencia del binding. Java modela `CurrentWindowsIdentityProvider`, `MasterAuthorizationPolicy` y estados de autorizacion para pruebas puras.

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

## Action Catalog

Acciones previstas, todas tipadas:

Control:

```text
LOCK_INPUT
UNLOCK_INPUT
SHUTDOWN
RESTART
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

## Operaciones de contenido

`DISTRIBUTE_FILE` modela:

```text
sourceFile
logicalDestination
targets
conflictPolicy
```

`ConflictPolicy`:

```text
SKIP
REPLACE
RENAME
```

`CREATE_FOLDER` usa destinos logicos del workspace y debe ser idempotente cuando sea seguro.

`SET_WALLPAPER` y `RESTORE_WALLPAPER` quedan modeladas como acciones previstas, sin aplicar wallpapers reales.

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

La UI futura debe permitir reintentar solo fallidos y no repetir manualmente los exitosos.

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
- Authorization.
- Operation.

Separar siempre:

```text
errorCode
```

de:

```text
mensaje para usuario
```

La UI futura no debe decidir comparando textos ni mostrar excepciones tecnicas como `IOException`, `SocketException`, `Win32Exception` o stack traces.

## Retry e idempotencia

Errores potencialmente reintentables:

```text
DEVICE_OFFLINE
AGENT_UNAVAILABLE
SESSION_NOT_AVAILABLE temporal
TRANSFER_FAILED
FILE_WRITE_FAILED
```

Errores que requieren intervencion:

```text
INVALID_URL
APPLICATION_NOT_INSTALLED
TARGET_OCCUPIED
INSUFFICIENT_DISK_SPACE
```

No debe existir retry infinito.

Las operaciones futuras deben llevar `operationId` como idempotency key para tolerar reintentos sin duplicar efectos, por ejemplo `CREATE_FOLDER`.

## Operaciones destructivas

Acciones destructivas o de alto impacto:

```text
SHUTDOWN
REPLACE_FILE
MOVE_STUDENT
SWAP_STUDENTS
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

No hay SQLite ni auditoria persistente en Prompt 07.
