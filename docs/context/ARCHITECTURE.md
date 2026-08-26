# Arquitectura

## Estado general

Prompt 08 agrega persistencia SQLite local del dominio Master mediante Spring JDBC, Flyway programatico y repositories explicitos. El Master Backend ya puede crear, migrar y reabrir una base `classroom.db` con aulas, catalogo de aplicaciones, grupos, alumnos, devices, assignments, workspaces, perfiles de navegador y operaciones batch.

Prompt 07 agrega el modelo funcional completo de Galtek Classroom en el Master Backend: aula, devices, alumnos, grupos, workspaces de alumno, perfiles de navegador, binding local del Master por Windows SID, catalogo de acciones, errores operacionales y planners puros para assignment, move, swap y batch preflight.

Prompt 06 deja `GaltekClassroom.Agent.Service` como Windows Service real y agrega el ciclo de vida productivo de `GaltekClassroom.Agent.Session`. El Service se ejecuta en Session 0 como `LocalSystem`; el Session Agent arranca al logon mediante Windows Task Scheduler, se ejecuta con el token del usuario interactivo, usa privilegio limitado, permanece en background sin UI y se reconecta al Service por Local IPC.

Local IPC API v1 sigue siendo read-only sobre Windows Named Pipes. `GaltekClassroom.Agent.Service` expone estado seguro de dispositivo y Machine Code a `GaltekClassroom.Agent.Session` y al Master Backend Java sin duplicar Installation Identity ni Commercial License.

Las capacidades operativas de administracion remota siguen planificadas. Prompt 08 persiste metadata y resultados del dominio, pero no ejecuta transferencia real, Chrome, wallpapers, proyeccion, gRPC, mTLS, mDNS, pairing, UI, captura, bloqueo ni comandos remotos.

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
- Cliente `LocalAgentClient` con transporte Windows Named Pipe y framing IPC v1.
- Mapeo de Agent Service no disponible a HTTP 503 con codigo `LOCAL_AGENT_UNAVAILABLE`.
- Prueba automatica del health endpoint.
- Pruebas de framing IPC, cliente local, endpoints de dispositivo y salud.
- Dominio funcional puro en paquetes por contexto:
  - `classroom`: `Classroom`, configuracion y relacion de devices/students/groups.
  - `device`: `Device`, estados operacionales y capacidades.
  - `student`: `Student`, `SchoolGroup`, `DeviceAssignment`, policies y planners de move/swap.
  - `workspace`: `StudentWorkspace`, destinos logicos y recovery planificado.
  - `browser`: perfiles de alumno/Master y validacion conservadora de URL.
  - `application`: catalogo de aplicaciones por `applicationId`.
  - `operations`: catalogo de acciones, batch, preflight, resultados, errores, conflict policy y workflows.
  - `master`: `MasterWindowsBinding`, proveedor de SID actual y politica de autorizacion.
- `DeviceAssignmentPolicy` para detectar alumno ya asignado y equipo ocupado.
- `StudentMovePlanner` para preflight de `MOVE_STUDENT` sin mover archivos.
- `StudentSwapPlanner` para preflight de `SWAP_STUDENTS` sin transferencias ni cambios de assignment.
- `BatchOperationPlanner` para clasificar targets `READY`, `WARNING`, `BLOCKED`.
- Modelo central `ErrorCode` con categorias y bandera retryable, separado de mensajes para usuario.
- `BatchOperation` con estados `SUCCESS`, `PARTIAL_SUCCESS`, `FAILED`, `CANCELLED`, `ROLLED_BACK` y retry solo de fallidos retryable.
- `OpenUrlPolicy` que permite `http`/`https` y rechaza esquemas inseguros como `file`, `javascript` y `data`.
- Pruebas Java de assignment, move, swap, batch, URL y autorizacion Master.
- Persistencia SQLite local del dominio Master con Spring JDBC.
- Dependencias `spring-boot-starter-jdbc`, `flyway-core` y `sqlite-jdbc` en el backend Master.
- Flyway programatico para migraciones SQLite desde `classpath:db/migration/sqlite`.
- Migracion `V1__create_master_domain.sql` con tablas del dominio Master.
- Configuracion local `galtek.classroom.master.storage.*`.
- Resolucion de datos del Master a `<CommonApplicationData>\Galtek\Classroom\Master\` con override `GALTEK_CLASSROOM_MASTER_DATA_DIR`.
- Base local `classroom.db` ignorada por Git, junto con archivos WAL/SHM.
- `PRAGMA foreign_keys=ON`, WAL, `synchronous=NORMAL` y `busy_timeout` configurado.
- Pool Hikari pequeno para SQLite local.
- `MasterDatabaseInitializer` con `PRAGMA quick_check` antes/despues de migrar cuando corresponde.
- Estado de almacenamiento `MasterStorageState` con `READY`, `UNAVAILABLE`, `CORRUPT` y `MIGRATION_FAILED`.
- Repositories explicitos para `Classroom`, `ApplicationDefinition`, `SchoolGroup`, `Student`, `Device`, `DeviceAssignment`, `StudentWorkspace`, `BrowserProfile`, `MasterBrowserProfile` y `BatchOperation`.
- Servicios transaccionales de administracion de aula, alumnos, devices, assignments, workspaces, perfiles, catalogo y batch.
- Control de version optimista mediante columna `version` y error `CONCURRENT_MODIFICATION`.
- `device_assignments` como fuente de verdad de asignaciones actuales e historicas.
- Indices unicos parciales para un assignment actual por alumno y por device.
- Persistencia de batch operations con targets, estados, errores, attempts y retry de fallidos retryable.
- Mapeo de errores SQLite a `MASTER_DATABASE_UNAVAILABLE`, `MASTER_DATABASE_CORRUPT`, `MASTER_DATABASE_MIGRATION_FAILED`, `MASTER_DATABASE_BUSY`, `MASTER_STORAGE_FULL`, `PERSISTENCE_CONSTRAINT_VIOLATION` y `CONCURRENT_MODIFICATION`.
- Pruebas de integracion SQLite para creacion, migracion, reapertura, constraints, historial de assignments, batch retry, rollback, versionado y base corrupta.

PLANIFICADO:

- React + Tauri para UI de escritorio, sin Vite.
- Persistencia/verificacion final de Master Windows Binding mediante Agent Service y estado derivado por IPC.
- gRPC/Protobuf para comunicacion con Agents.
- Visualizacion de equipos, miniaturas y estado.
- UI batch-first para grupos, alumnos y equipos con partial success y retry de fallidos.
- Integracion real de workspaces, navegador, transferencia, wallpaper, proyeccion y auditoria.
- Auditoria administrativa.

NO IMPLEMENTADO:

- UI.
- Autenticacion.
- gRPC funcional.
- Descubrimiento.
- Commercial License en Java.
- Llaves publicas o JWT dentro del Master Backend.
- Ejecucion real de `OPEN_APPLICATION`, `OPEN_URL`, `DISTRIBUTE_FILE`, `CREATE_FOLDER`, `SET_WALLPAPER`, `MOVE_STUDENT` o `SWAP_STUDENTS`.

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
- `MasterWindowsBinding` no se persiste en SQLite por decision de seguridad; la autoridad final sigue planificada en Agent Service.
- IDs del dominio como `TEXT`, generados por la aplicacion; no usar `AUTOINCREMENT` para identidades funcionales.
- Timestamps como `TEXT` UTC producido desde `Instant.toString()`.
- Booleans como `INTEGER` `0/1` con `CHECK`.
- Enum sets serializados como JSON textual de nombres de enum ordenados; no hay serializacion binaria Java.
- Foreign keys habilitadas por conexion.
- WAL habilitado para mejorar lectura local concurrente.
- `busy_timeout` default 5000 ms.
- `maximum-pool-size` default 4.
- Corruption check con `PRAGMA quick_check`.

NO IMPLEMENTADO:

- Cifrado at-rest de `classroom.db`.
- Backup/restore automatico.
- Auditoria persistente de acciones administrativas reales.
- Borrado seguro/retencion configurable de PII.
- Persistencia de Master Windows Binding en Agent Service.

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
- Publicacion productiva inicial: `Release`, `win-x64`, self-contained, carpeta no single-file.
- Binarios instalados en `<ProgramFiles>\Galtek\Classroom\Agent\`.
- Scripts PowerShell en `installer/windows/` para publicar, instalar/actualizar y desinstalar.
- `uninstall-agent-service.ps1` conserva ProgramData por defecto y solo borra identidad/licencia con `-PurgeData`.
- Version inicial del ejecutable controlada en `agent/src/GaltekClassroom.Agent.Service/GaltekClassroom.Agent.Service.csproj`.
- Registra inicio, estado activo y detencion limpia.
- Es autoridad local de Installation Identity.
- Es autoridad local de Commercial License.
- Resuelve `installation.json` al arrancar.
- Crea una identidad permanente cuando no existe.
- Reutiliza el mismo `installationId` cuando el archivo existe y es valido.
- Falla de forma controlada si `installation.json` esta corrupto o incompleto.
- Genera Machine Code Base64 en modo de desarrollo con `--machine-code`.
- Valida licencias JWT firmadas con RSA / RS256.
- Rechaza algoritmos distintos a RS256 antes de confiar en el token.
- Exige issuer `galtek-hub`, audience `galtek-classroom`, product `GALTEK_CLASSROOM` y `schemaVersion = 1`.
- Exige `sub == installationId` sin modificar ni adoptar `installation.json`.
- Valida hardware actual 3 de 4 contra `cpuHash`, `motherboardHash`, `macHash` y `diskHash`.
- Valida expiracion en UTC y mantiene el Service vivo si la licencia esta vencida.
- Persiste el JWT comercial en `license.dat`, separado de `installation.json`, con escritura temporal y reemplazo/movimiento.
- Expone CLI de desarrollo para `--license-status`, `--activate-license` por STDIN y `--activate-license-file <ruta>`.
- Mantiene `LicenseState` en memoria sin exponer el JWT completo.
- Monitor ligero de expiracion runtime cada 60 segundos, sin recalcular WMI.
- Local IPC API v1 read-only mediante Windows Named Pipes.
- Named Pipe server versionado `GaltekClassroom.Agent.v1`.
- Framing IPC con prefijo de longitud de 4 bytes BIG ENDIAN mas JSON UTF-8.
- Limite maximo de mensaje de 64 KiB.
- Operaciones IPC permitidas: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`.
- `GET_DEVICE_STATUS` expone solo estado seguro y no expone JWT ni hashes de hardware.
- `GET_MACHINE_CODE` reutiliza la implementacion existente de Machine Code y no exige licencia activa.
- ACL actual del pipe: `LocalSystem` y `BuiltinAdministrators` con `FullControl`; `Authenticated Users` con `ReadWrite | Synchronize`.

PLANIFICADO:

- Empaquetar la llave publica real de Galtek Hub como recurso/mecanismo productivo.
- Revalidacion completa explicita invocable por IPC.
- Comunicacion segura.
- Heartbeat.
- Recepcion de comandos estructurados.
- Operaciones privilegiadas.
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
- Autorizacion MASTER para comandos.
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

PLANIFICADO:

- Contratos de red cuando se definan los `.proto`.
- Contratos concretos Service <-> Session para ejecucion controlada, cuando exista autorizacion local e IPC write disenado.

## Comunicacion futura de red

PLANIFICADO:

- Los Clientes iniciaran conexiones persistentes autenticadas hacia el Master.
- El protocolo sera gRPC con Protobuf.
- La confianza de red usara mTLS y certificados de dispositivo.
- El descubrimiento usara mDNS/DNS-SD.

NO IMPLEMENTADO:

- Protocolos `.proto` definitivos.
- Servidores o clientes gRPC.
- mTLS.
- Certificados.
- Pairing.
- Descubrimiento real.

Nota de seguridad: descubrir un equipo no significa confiar en el.

## IPC local

IMPLEMENTADO:

- Windows Named Pipe `GaltekClassroom.Agent.v1`.
- `GaltekClassroom.Agent.Service` es el servidor IPC.
- `GaltekClassroom.Agent.Session` consume `PING` y `GET_DEVICE_STATUS` en CLI one-shot y en supervisor background.
- Master Backend Java consume `GET_DEVICE_STATUS` y `GET_MACHINE_CODE`.
- Protocolo documentado en `protocol/local-ipc-v1.md`.
- `protocolVersion = 1`.
- Mensajes JSON UTF-8 con prefijo de longitud de 4 bytes BIG ENDIAN.
- Limite de payload de 64 KiB.
- Operaciones permitidas en v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`.
- IPC v1 es read-only.
- Version desconocida devuelve `IPC_PROTOCOL_UNSUPPORTED`.
- Operacion desconocida devuelve `IPC_OPERATION_NOT_SUPPORTED`.
- JSON malformado, longitud invalida y desconexiones de clientes se manejan sin detener el Service.

NO IMPLEMENTADO:

- Autorizacion local para operaciones privilegiadas futuras.
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
- Network Identity.

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
- Autorizacion MASTER.

### Master Windows Binding

IMPLEMENTADO en Prompt 07:

- Modelo `MasterWindowsBinding` con `installationId`, `windowsSid`, `accountDisplayName` y `boundAtUtc`.
- Modelo `MasterAuthorizationState` con `NOT_CONFIGURED`, `AUTHORIZED`, `CURRENT_ACCOUNT_NOT_AUTHORIZED` y `MASTER_LICENSE_REQUIRED`.
- `MasterAuthorizationPolicy` que exige licencia activa con rol `MASTER`, `installationId` correcto y SID actual igual al SID ligado.
- Abstraccion `CurrentWindowsIdentityProvider`.
- Implementacion Java `JdkCurrentWindowsIdentityProvider` que obtiene SID mediante API del JDK por reflexion, sin comandos shell ni JNA.
- Pruebas puras que no dependen de la cuenta Windows real.
- Prompt 08 confirma que `classroom.db` no persiste `MasterWindowsBinding` ni crea tabla `master_windows_binding`.

PLANIFICADO:

- Persistir y verificar el binding final en el Agent Service, porque es autoridad local para datos sensibles.
- Exponer al Master Backend solo estado derivado por IPC local.
- UI futura para configurar/diagnosticar binding, sin ser autoridad.

NO IMPLEMENTADO:

- Persistencia del binding.
- IPC write para crear o modificar binding.
- Reemplazo de Network Identity, pairing o mTLS.
- Autorizacion remota entre equipos.

### Network Identity

PLANIFICADO:

- Identidad criptografica de red separada de la licencia comercial.
- Par de claves del dispositivo.
- Certificado.
- Pairing.
- mTLS.

NO IMPLEMENTADO:

- Claves.
- Certificados.
- Pairing.
- Autorizacion Master-Agent.

## Almacenamiento local del Agent

IMPLEMENTADO:

- Usar `<CommonApplicationData>\Galtek\Classroom\`.
- Obtener la ruta mediante APIs de .NET, sin hardcodear `C:\ProgramData`.
- Permitir override de desarrollo y tests con `GALTEK_CLASSROOM_DATA_DIR`.
- Archivo `installation.json` para Installation Identity.
- Archivo `license.dat` para Commercial License.
- Escritura de identidad y licencia con archivo temporal y reemplazo/movimiento para evitar archivos parciales.
- `license.dat` guarda solo el JWT recibido.
- Los scripts de instalacion separan binarios en `<ProgramFiles>\Galtek\Classroom\Agent\` y datos persistentes en `<CommonApplicationData>\Galtek\Classroom\`.
- Actualizar o desinstalar normalmente no borra `installation.json` ni `license.dat`.

NO IMPLEMENTADO:

- Base de datos local.
- Logs persistentes en disco.
- DPAPI/ACL hardening avanzado para `license.dat`.

Nota de seguridad: en esta fase `license.dat` no depende de confidencialidad para integridad. El JWT esta firmado, ligado a `installationId` y ligado al hardware por regla 3 de 4. El cifrado o endurecimiento local queda para una fase posterior.

## Limites de seguridad

VIGENTE DESDE AHORA:

- No permitir ejecucion remota arbitraria.
- No aceptar `cmd.exe /c`, PowerShell arbitrario, shell remota ni rutas arbitrarias enviadas por un Master.
- Usar comandos futuros explicitos y estructurados, por ejemplo `LOCK_INPUT`, `UNLOCK_INPUT`, `OPEN_APPLICATION` con `appId`, `SHUTDOWN`, `RESTART`, `START_PROJECTION`, `STOP_PROJECTION`.
- Las aplicaciones abribles remotamente deben pertenecer a un catalogo configurado previamente.
- Operaciones de contenido deben usar destinos logicos de `StudentWorkspace`; el Master no debe enviar rutas absolutas arbitrarias ni path traversal.
- `Device` y `Student` son entidades independientes; mover un alumno es un workflow de alumno/workspace, no una copia manual de una carpeta de PC a PC.
- Browser profiles modelan portabilidad sin copiar passwords, cookies ni cache protegido.
- Batch-first es obligatorio: targets pueden ser classroom, group, students, devices o items individuales cuando la accion tenga sentido.
- `PARTIAL_SUCCESS` y retry solo de fallidos deben formar parte del modelo de cualquier operacion masiva.
- `TARGET_OCCUPIED` nunca debe sobrescribir ni borrar al alumno que ocupa el equipo.
- IP y MAC no son identidad de autorizacion.
- Licencia comercial no reemplaza pairing, certificados ni autorizacion de red.
- Licencia MASTER valida no equivale a permiso automatico para controlar clientes de la LAN.
- IPC v1 es read-only; acceso al pipe no equivale a autorizacion para futuras operaciones privilegiadas.
- Las operaciones futuras que aumenten control requeriran licencia activa.
- Las operaciones futuras de recuperacion, como `UNLOCK_INPUT` y `STOP_PROJECTION`, no deben bloquearse por expiracion para evitar dejar equipos atrapados.
