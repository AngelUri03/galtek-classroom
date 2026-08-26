# Estado actual

## Ultima actualizacion

2026-08-26 - Prompt 08.

## Estado del proyecto

Prompt 08 deja persistido en SQLite el dominio funcional del Master Backend creado en Prompt 07. El backend Master ya puede crear, migrar y reabrir `classroom.db` con aulas, aplicaciones, grupos, alumnos, devices, assignments, workspaces, perfiles de navegador y operaciones batch. La persistencia usa Spring JDBC, repositories explicitos, Flyway programatico y tests de integracion con directorios temporales.

`GaltekClassroom.Agent.Service` sigue siendo la autoridad local de Installation Identity y Commercial License. `GaltekClassroom.Agent.Session` mantiene el lifecycle background/autostart por Task Scheduler y conexion al Service por Local IPC v1 read-only.

El producto todavia no tiene UI, comunicacion de red, gRPC, mTLS, mDNS, pairing, captura, bloqueo, filesystem real, browser automation, wallpaper real ni comandos remotos.

## Implementado

- Backend Master en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code` via IPC local al Agent Service.
- Dominio Java por contextos:
  - `classroom`: `Classroom`, `ClassroomConfiguration`.
  - `device`: `Device`, `DeviceStatus`, `DeviceCapability`.
  - `student`: `Student`, `SchoolGroup`, `DeviceAssignment`, policies y planners.
  - `workspace`: `StudentWorkspace`, destinos logicos y recovery planificado.
  - `browser`: perfiles de alumno/Master y validacion de URL.
  - `application`: catalogo de aplicaciones.
  - `operations`: acciones tipadas, batch, preflight, errores, requests y workflows.
  - `master`: Master Windows Binding, SID provider y autorizacion.
- Persistencia SQLite local del dominio Master con Spring JDBC.
- Dependencias `spring-boot-starter-jdbc`, `flyway-core` y `sqlite-jdbc`.
- Configuracion `galtek.classroom.master.storage.*`.
- Ruta default del Master: `<CommonApplicationData>\Galtek\Classroom\Master\classroom.db`.
- Override de datos del Master: `GALTEK_CLASSROOM_MASTER_DATA_DIR`.
- Migraciones SQLite en `master-backend/src/main/resources/db/migration/sqlite/`.
- Migracion `V1__create_master_domain.sql`.
- Tablas: `classrooms`, `application_definitions`, `classroom_applications`, `school_groups`, `students`, `devices`, `student_workspaces`, `browser_profiles`, `master_browser_profiles`, `device_assignments`, `batch_operations`, `batch_target_results`.
- No existe tabla `master_windows_binding` en SQLite.
- PRAGMAs: foreign keys ON, WAL, synchronous NORMAL, busy timeout.
- `PRAGMA quick_check` al arrancar para base existente y despues de migrar.
- Hikari con pool pequeno para SQLite local.
- Repositories explicitos para cada agregado persistido.
- Servicios transaccionales para aulas, alumnos, devices, assignments, workspaces, perfiles, catalogo y batch.
- Version optimista con `version` y error `CONCURRENT_MODIFICATION`.
- `device_assignments` como fuente de verdad de assignments actuales e historicos.
- Constraints SQLite para un assignment actual por alumno y por device.
- Batch operations persistentes con targets, attempts, errores y retry de fallidos retryable.
- Mapeo de errores SQLite a `ErrorCode` de persistencia.
- Tests de integracion SQLite para migracion, reapertura, constraints, corrupcion, rollback, batch y versionado.
- Local IPC API v1 read-only sobre Windows Named Pipes.
- Agent Service instalable como Windows Service `GaltekClassroomAgent`.
- Agent Service como autoridad local de Installation Identity y Commercial License.
- Session Agent background/autostart via Scheduled Task `GaltekClassroomSessionAgent`.
- Solucion .NET con Service, Session, Shared y tests.
- Documentacion de contexto, arquitectura, decisiones e historial.

## En progreso

- Ningun desarrollo activo dejado a medias dentro del Prompt 08.

## Pendiente inmediato

- Prompt 09: integrar la persistencia SQLite con endpoints/API o el siguiente alcance que se defina.
- Persistir/verificar Master Windows Binding en Agent Service y exponer estado derivado por IPC.
- Definir IPC write y autorizacion local antes de operaciones privilegiadas.
- Implementar UI React + Tauri cuando corresponda.
- Implementar filesystem real de StudentWorkspace y recovery en fases posteriores.
- Implementar gRPC, mTLS, pairing, mDNS y Network Identity en fases posteriores.
- Empaquetar la llave publica real de Galtek Hub para produccion.

## Cambios aceptados

- Separacion Master backend / Agent / Protocol / Docs.
- Agent separado en Windows Service y Session Agent.
- Agent Service como autoridad local de Installation Identity.
- Agent Service como autoridad local de Commercial License.
- Agent Service como servidor unico de IPC local.
- Master Backend no lee `installation.json`, `license.dat`, JWT ni hashes de hardware.
- Master Backend consulta Agent Service solo mediante IPC local read-only.
- Program Files contiene binarios; ProgramData contiene identidad, licencia y datos persistentes.
- Master Backend persiste su dominio local en SQLite.
- Master data dir default: `<CommonApplicationData>\Galtek\Classroom\Master\`.
- `GALTEK_CLASSROOM_MASTER_DATA_DIR` existe para desarrollo/tests.
- Spring JDBC + Flyway + SQLite JDBC son la base de persistencia Master.
- Flyway se ejecuta programaticamente para controlar salud, migracion y mapping de errores.
- Device y Student quedan separados por decision de dominio.
- `StudentWorkspace` pertenece al alumno.
- `DeviceAssignment` es fuente de verdad de ocupacion actual e historial.
- Operaciones futuras deben ser batch-first, con partial success y retry solo de fallidos.
- Operaciones de contenido usan destinos logicos, no rutas arbitrarias.
- Browser portability no copia passwords/cookies/cache.
- Master Windows Binding se modela por SID, pero no se persiste en SQLite.
- La autoridad final del binding debe vivir en Agent Service.
- Corrupt DB se preserva y se reporta como `MASTER_DATABASE_CORRUPT`.

## Cambios rechazados / No repetir

- No implementar ejecucion remota arbitraria.
- No lanzar procesos interactivos desde el Windows Service.
- No implementar operaciones write por IPC sin autorizacion local disenada.
- No crear tabla `master_windows_binding` en SQLite.
- No persistir passwords, cookies, tokens ni cache protegido de navegador.
- No borrar automaticamente `classroom.db` si esta corrupta.
- No usar IP, MAC, hostname, username ni pertenencia a Administrators como identidad/autorizacion suficiente.
- No usar rutas absolutas arbitrarias ni path traversal para operaciones de contenido.
- No implementar transferencia real de archivos, Chrome automation, wallpaper real, `OPEN_URL` remoto, `DISTRIBUTE_FILE` remoto, `CREATE_FOLDER` remoto, `MOVE_STUDENT` real ni `SWAP_STUDENTS` real en Prompt 08.
- No implementar Galtek Hub, private keys comerciales, activacion online, revocacion online, gRPC, mTLS, mDNS, pairing, captura, bloqueo, WebRTC, React, Tauri ni UI en Prompt 08.
- No usar JPA/Hibernate para la persistencia SQLite actual.
- No usar `AUTOINCREMENT` como identidad de dominio.
- No hard-delete de alumnos, devices, aulas o assignments con historial salvo que una fase futura defina retencion/borrado seguro.
- No hacer commits automaticamente.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- El entorno tiene `DEBUG=release`; el backend fuerza `debug=false` salvo configuracion explicita por argumentos/JVM properties para evitar logs DEBUG accidentales, pero Spring puede emitir logs verbosos en pruebas lanzadas por builder.
- Durante el desarrollo de Prompt 08 se detecto una escritura accidental inicial a `C:\ProgramData\Galtek\Classroom\Master\classroom.db` antes de corregir el override de tests. La suite final usa directorios temporales y la base no fue versionada.
- No existe todavia una llave publica real de Galtek Hub empaquetada.
- `license.dat` no se cifra localmente en esta fase.
- `classroom.db` no tiene cifrado at-rest, backup/restore automatico ni politica de retencion/borrado seguro de PII.
- La validacion real con Service Control Manager y Task Scheduler productivo depende de que el entorno este elevado.
- Master Windows Binding esta modelado en Java, pero su persistencia/verificacion final debe vivir en Agent Service.

## Pruebas ejecutadas

- `mvn clean verify` en `master-backend`: correcto, 50 pruebas superadas.
- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 59 pruebas superadas.

## Proximo paso recomendado

Prompt 09: exponer o consumir la persistencia SQLite desde los endpoints/API del Master que correspondan, sin implementar todavia red, filesystem real ni ejecucion remota.
