# Estado actual

## Ultima actualizacion

2026-08-26 - Prompt 09.

## Estado del proyecto

Prompt 09 deja implementada la autoridad real de Master Windows Binding en `GaltekClassroom.Agent.Service`. El Service persiste `master-binding.json` en ProgramData, lo valida contra la Installation Identity actual, exige Commercial License activa con rol `MASTER` y compara el SID ligado contra el SID real del proceso conectado al Named Pipe mediante impersonation.

El Master Backend Java no lee `master-binding.json`, no conoce su ruta y no recalcula autorizacion local. Consume `GET_MASTER_AUTHORIZATION` por Local IPC v1, expone `GET /api/master/authorization` y agrega `MasterAccessGuard` para futuros endpoints administrativos.

El producto todavia no tiene UI, comunicacion de red, gRPC, mTLS, mDNS, pairing, captura, bloqueo, filesystem real, browser automation, wallpaper real ni comandos remotos.

## Implementado

- Backend Master en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code` via IPC local al Agent Service.
- Endpoint `GET /api/master/authorization` via IPC local al Agent Service.
- `MasterAccessGuard.requireAuthorized()` para futuros endpoints administrativos.
- Local IPC API v1 read-only sobre Windows Named Pipes.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`, `GET_MASTER_AUTHORIZATION`.
- Agent Service instalable como Windows Service `GaltekClassroomAgent`.
- Agent Service como autoridad local de Installation Identity, Commercial License y Master Windows Binding.
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
- Documentacion de contexto, arquitectura, decisiones, historial, README y protocolo IPC actualizada.

## En progreso

- Ningun desarrollo activo dejado a medias dentro del Prompt 09.

## Pendiente inmediato

- Prompt 10 debe elegir el siguiente alcance sin reabrir Prompt 09.
- UI futura para diagnosticar/configurar binding sin convertirse en autoridad.
- IPC write futuro solo cuando exista un diseno de autorizacion local adecuado.
- Endpoints administrativos futuros como `/api/classrooms`, `/api/students`, `/api/groups` y `/api/operations` deberan pasar por `MasterAccessGuard`.
- Implementar filesystem real de StudentWorkspace y recovery en fases posteriores.
- Implementar gRPC, mTLS, pairing, mDNS y Network Identity en fases posteriores.
- Empaquetar la llave publica real de Galtek Hub para produccion.

## Cambios aceptados

- Agent Service decide Master authorization; Java solo consume resultado derivado.
- El SID enviado por JSON, HTTP, UI o payload IPC no se considera prueba.
- La autorizacion local Master falla cerrado ante Agent down, binding invalido, licencia no activa, falta de rol `MASTER`, mismatch de instalacion o SID distinto.
- Otro administrador Windows no hereda acceso Master si su SID no esta ligado.
- Rebinding requiere `--replace-master-binding`.
- Update de binarios y uninstall normal preservan `master-binding.json`.
- `-PurgeData` elimina Installation Identity, Commercial License y Master Windows Binding.

## Cambios rechazados / No repetir

- No implementar IPC write para set/update/delete de binding en Prompt 09.
- No leer `master-binding.json` desde Java.
- No crear tabla `master_windows_binding` ni migration SQLite.
- No autorizar por username, display name, hostname, IP, MAC, session id o pertenencia a Administrators.
- No usar `whoami.exe`, PowerShell, WMI shell ni procesos externos para obtener SID.
- No exponer SID completo, JWT, hashes de hardware, rutas internas ni ACLs internas en respuestas IPC/HTTP.
- No implementar CRUD escolar, UI, gRPC, pairing, comandos remotos ni filesystem real en Prompt 09.
- No hacer commits automaticamente.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- No existe todavia una llave publica real de Galtek Hub empaquetada; si falta llave publica, la licencia queda en `LICENSE_KEY_NOT_CONFIGURED` y Master no autoriza.
- `license.dat` no se cifra localmente en esta fase.
- `classroom.db` no tiene cifrado at-rest, backup/restore automatico ni politica de retencion/borrado seguro de PII.
- La validacion productiva con Service Control Manager, Task Scheduler y CLI elevada depende de ejecutar en un entorno con permisos administrativos.
- No se creo una segunda cuenta Windows para prueba manual de SID distinto; ese caso queda cubierto por tests automatizados.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 88 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 59 pruebas superadas.

## Proximo paso recomendado

Prompt 10: crear los primeros endpoints read-only del dominio Master usando SQLite y proteger cualquier endpoint administrativo futuro con `MasterAccessGuard`.
