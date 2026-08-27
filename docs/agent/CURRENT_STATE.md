# Estado actual

## Ultima actualizacion

2026-08-27 - Prompt 12 / cierre 12.1.

## Estado del proyecto

Prompt 12 implementa pairing criptografico Master-Client sobre Network Identity. El Client conserva su Network Identity en `GaltekClassroom.Agent.Service`; el Master Backend agrega una Network Identity propia, private key cifrada fuera de SQLite/JSON plano y trust store local. El pairing requiere intencion explicita, usa challenge/response firmado, expira challenges, bloquea replay, persiste trust en ambos lados y permite revocacion.

Prompt 9.6 formaliza el requisito futuro de cuentas Windows administradas en Clients. Cada PC de alumnos podra tener dos cuentas logicas, `PRIMARY` y `SECONDARY`, y el Master podra planificar una sola accion masiva para dejar un aula/grupo/seleccion en la cuenta objetivo con resultados `NO_CHANGE`, `SUCCESS`, `FAILED` y retry solo de fallidos.

El Master Backend Java sigue sin leer `master-binding.json` ni `network-identity.json`, no conoce sus rutas y no recalcula autorizacion local. Consume `GET_MASTER_AUTHORIZATION` por Local IPC v1, mantiene publico `GET /api/master/authorization` para diagnostico y usa `MasterAccessGuard` en endpoints administrativos.

El producto todavia no tiene UI, comunicacion de red real, gRPC real, mTLS real, mDNS, certificados, discovery real, captura, bloqueo, filesystem real, browser automation, wallpaper real, login/logoff Windows real, cambio real de usuario ni comandos remotos.

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
- Endpoints protegidos de aplicaciones y operaciones.
- `RestControllerAdvice` uniforme para errores HTTP: validacion 400, no encontrado 404, conflicto/version 409, Master no autorizado 403, Agent/storage no disponible 503.
- Modelos puros Java para cuentas Windows administradas: `ManagedWindowsAccount`, `ManagedWindowsAccountType`, `ManagedWindowsAccountStatus` y `WindowsSessionState`.
- `ManagedAccountSwitchPlanner` puro para decidir `NO_CHANGE`, `LOGON`, `SWITCH`, `PENDING` o `BLOCKED` por device.
- Operaciones futuras tipadas `GET_WINDOWS_SESSION_STATE`, `LOGON_MANAGED_ACCOUNT`, `LOGOFF_WINDOWS_SESSION` y `SWITCH_MANAGED_ACCOUNT`.
- `TargetExecutionStatus.NO_CHANGE` tratado como exito no retryable en `BatchOperation`.
- Errores estructurados para cuentas/sesion Windows administrada: `ACCOUNT_NOT_CONFIGURED`, `MANAGED_CREDENTIAL_NOT_CONFIGURED`, `WINDOWS_SESSION_UNKNOWN`, `WINDOWS_LOGON_FAILED`, `WINDOWS_LOGOFF_FAILED`, `SESSION_SWITCH_FAILED` y `CREDENTIAL_PROVIDER_UNAVAILABLE`.
- Contratos compartidos C# para operaciones, tipos de cuenta, estados de sesion y acciones de switch administrado.
- Local IPC API v1 read-only sobre Windows Named Pipes.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`.
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
- No existe tabla `master_windows_binding` en SQLite.
- Session Agent background/autostart via Scheduled Task `GaltekClassroomSessionAgent`; sin cambios funcionales en Prompt 09.
- Documentacion de API, contexto, arquitectura, decisiones, historial y README actualizada.

## En progreso

- Ningun desarrollo activo dejado a medias dentro del Prompt 12.

## Pendiente inmediato

- Prompt 13 debe implementar transporte seguro usando el trust ya establecido, sin redisenar pairing ni convertir discovery en autorizacion.
- UI futura para diagnosticar/configurar binding sin convertirse en autoridad.
- IPC write futuro solo cuando exista un diseno de autorizacion local adecuado.
- Mantener cualquier nuevo endpoint administrativo bajo `MasterAccessGuard`.
- Disenar posteriormente almacenamiento seguro de credenciales administradas en el Agent Service del Client.
- Disenar posteriormente login/logoff/switch con integracion soportada por Windows, contemplando Credential Provider.
- Implementar filesystem real de StudentWorkspace y recovery en fases posteriores.
- Implementar gRPC real, mTLS real, mDNS/discovery real, certificados y comandos remotos en fases posteriores.
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
- `students/batch` permite parcialidad por fila; un alumno invalido no cancela los demas.
- `assignments/batch` preflight completo antes de writes; `TARGET_OCCUPIED` no reemplaza automaticamente.
- El Master no almacena ni envia passwords de cuentas Windows administradas; la UI no recibe secretos.
- Los comandos futuros de cuentas administradas enviaran solo `accountId` logico (`PRIMARY`/`SECONDARY`).
- `SWITCH_MANAGED_ACCOUNT(PRIMARY)` puede producir targets `NO_CHANGE`, `SUCCESS` y `FAILED`; el retry posterior solo aplica a fallidos retryable.

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
- No redisenar ni reimplementar pairing en Prompt 13; usar el trust ya persistido.
- No guardar private key de Network Identity en JSON, logs, SQLite ni archivos planos.
- No usar Commercial License, IP, MAC ni hostname como Network Identity, trust ni autorizacion.
- No usar SendKeys, scripts, PowerShell, `cmd`, autologon inseguro ni ejecucion arbitraria para automatizar sesiones Windows.
- No hacer commits automaticamente.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- No existe todavia una llave publica real de Galtek Hub empaquetada; si falta llave publica, la licencia queda en `LICENSE_KEY_NOT_CONFIGURED` y Master no autoriza.
- `license.dat` no se cifra localmente en esta fase.
- `classroom.db` no tiene cifrado at-rest, backup/restore automatico ni politica de retencion/borrado seguro de PII.
- La validacion productiva con Service Control Manager, Task Scheduler y CLI elevada depende de ejecutar en un entorno con permisos administrativos.
- La creacion real de la llave CNG de Network Identity requiere el contexto del Service como `LocalSystem` o una consola elevada; una prueba manual desde shell no elevado devuelve acceso denegado.
- No se creo una segunda cuenta Windows para prueba manual de SID distinto; ese caso queda cubierto por tests automatizados.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 110 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 86 pruebas superadas.
- Parser PowerShell de `installer/windows/uninstall-agent-service.ps1`: correcto.
- `--network-identity-status` con `GALTEK_CLASSROOM_DATA_DIR` temporal vacio: correcto, devuelve `NOT_CONFIGURED` y no crea directorio ni llave.

## Proximo paso recomendado

Prompt 13: transporte seguro usando el trust ya establecido por pairing. No avanzar a UI, comandos remotos ni login/switch Windows real hasta definir ese transporte.
