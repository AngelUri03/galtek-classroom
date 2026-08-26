# Estado actual

## Ultima actualizacion

2026-08-25 - Prompt 06.

## Estado del proyecto

`GaltekClassroom.Agent.Service` ya puede ejecutarse como Windows Service real en Session 0 con cuenta `LocalSystem`, inicio `Automatic`, recovery y Local IPC v1 read-only.

`GaltekClassroom.Agent.Session` ya tiene lifecycle productivo: modo `--background`, arranque por Scheduled Task al logon del usuario, ejecucion con token interactivo y privilegio limitado, una instancia por sesion, supervisor IPC con reconexion y polling saludable. El Session Agent no lee ProgramData ni identity/licencia directamente; todo pasa por Named Pipe hacia el Service.

Installation Identity y Commercial License siguen siendo autoridad exclusiva del Agent Service. Los datos persistentes permanecen en `<CommonApplicationData>\Galtek\Classroom\`; instalar, actualizar o desinstalar normalmente no borra `installation.json` ni `license.dat`.

El producto todavia no tiene funciones operativas de administracion remota, UI, comunicacion de red, gRPC, mTLS, mDNS, pairing, captura, bloqueo ni comandos remotos.

## Implementado

- Backend Master minimo en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code`.
- Flujo Java `controller -> service -> LocalAgentClient -> Named Pipe transport`.
- Respuesta HTTP 503 con codigo `LOCAL_AGENT_UNAVAILABLE` cuando el Agent Service no esta disponible.
- Solucion .NET `GaltekClassroom.Agent.sln` con Service, Session, Shared y tests.
- Agent Service con Worker Service / Generic Host .NET 8.
- Integracion oficial `Microsoft.Extensions.Hosting.WindowsServices`.
- Windows Service instalable `GaltekClassroomAgent`.
- Display Name `Galtek Classroom Agent Service`.
- Description `Servicio local de Galtek Classroom para identidad, licencia y administracion segura del equipo.`
- Cuenta de servicio `LocalSystem`.
- Startup type `Automatic`.
- Recovery configurado por instalador: restart en 5, 15 y 60 segundos; reset en 86400 segundos.
- Publicacion del Agent Service `Release`, `win-x64`, self-contained, sin single-file.
- Artifact Service en `artifacts/windows/agent-service/`, ignorado por Git.
- Instalacion/actualizacion Service en `%ProgramFiles%\Galtek\Classroom\Agent\`.
- Desinstalacion Service que conserva `%ProgramData%\Galtek\Classroom\` por defecto.
- Opcion explicita `-PurgeData` para borrar Installation Identity y Commercial License.
- Installation Identity permanente del equipo en `installation.json`.
- Ubicacion de datos en `<CommonApplicationData>\Galtek\Classroom\` con override `GALTEK_CLASSROOM_DATA_DIR`.
- Hashes SHA-256 de CPU, motherboard, MAC fisicas y discos.
- Machine Code Base64 generado por la implementacion existente del Agent Service.
- Commercial License local en `GaltekClassroom.Agent.Service`.
- Persistencia separada de licencia en `license.dat`.
- Validacion JWT con `System.IdentityModel.Tokens.Jwt` y llave publica RSA.
- `LicenseState` en memoria sin exponer JWT completo.
- Monitor de expiracion runtime cada 60 segundos sin recalcular WMI.
- CLI Service: `--machine-code`, `--license-status`, `--activate-license` y `--activate-license-file`.
- Local IPC API v1 read-only.
- Named Pipe server en el Agent Service con pipe `GaltekClassroom.Agent.v1`.
- Contratos IPC compartidos en C# Shared.
- Framing IPC de 4 bytes BIG ENDIAN mas JSON UTF-8.
- Limite de payload IPC de 64 KiB.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`.
- `GET_DEVICE_STATUS` expone solo campos seguros.
- `GET_MACHINE_CODE` funciona aunque la licencia no este activa.
- Cliente IPC C# en Session Agent.
- Comandos Session Agent `--ipc-status` y `--ipc-ping` como operaciones one-shot.
- Session Agent productivo como `WinExe` para autostart sin consola visible.
- Modo Session Agent `--background`; sin argumentos equivale a background.
- Background lifecycle con estados internos `STARTING`, `WAITING_FOR_SERVICE`, `CONNECTED`, `READY`, `STOPPING`.
- Single instance por sesion con named mutex local `Local\GaltekClassroom.Agent.Session`.
- Reconexion al Agent Service con backoff acotado `2s`, `5s`, `10s`, `30s`.
- Polling saludable con `PING` cada 15 segundos.
- El Session Agent permanece vivo si el Service cae, reinicia o si la licencia no esta activa.
- Publicacion Session Agent `Release`, `win-x64`, self-contained, sin single-file.
- Artifact Session en `artifacts/windows/agent-session/`, ignorado por Git.
- Instalacion Session Agent en `%ProgramFiles%\Galtek\Classroom\Agent\Session\`.
- Scheduled Task `GaltekClassroomSessionAgent` con trigger `AtLogon`, principal `S-1-5-32-545`, `RunLevel Limited` y `MultipleInstances Parallel`.
- Scripts `publish-agent-session.ps1`, `install-session-agent.ps1` y `uninstall-session-agent.ps1`.
- Orquestadores `publish-agent.ps1`, `install-agent.ps1` y `uninstall-agent.ps1`.
- Cliente IPC Java en Master Backend.
- Especificacion canonica `protocol/local-ipc-v1.md`.
- Proyecto xUnit `GaltekClassroom.Agent.Service.Tests`.
- Proyecto xUnit `GaltekClassroom.Agent.Session.Tests`.
- Pruebas Java para health, device endpoints, framing y cliente IPC.
- Documentacion de contexto, arquitectura, decisiones e historial.

## En progreso

- Ningun desarrollo activo dejado a medias dentro del Prompt 06.

## Pendiente inmediato

- Prompt 07: definir el siguiente contrato funcional sin agregar ejecucion remota arbitraria.
- Definir como se empaquetara la llave publica real de Galtek Hub para produccion.
- Disenar autorizacion local antes de cualquier operacion IPC write.

## Cambios aceptados

- Separacion Master backend / Agent / Protocol / Docs.
- Agent separado en Windows Service y Session Agent.
- Agent Service como autoridad local de Installation Identity.
- Agent Service como autoridad local de Commercial License.
- Agent Service como servidor unico de IPC local.
- Agent Service instalable como Windows Service real.
- Session Agent como proceso de usuario interactivo iniciado por Task Scheduler.
- Program Files contiene binarios; ProgramData contiene identidad, licencia y datos persistentes.
- Session Agent instalado debajo de `Agent\Session\` para separar su lifecycle del Service.
- Actualizar binarios no modifica ni regenera Installation Identity.
- Desinstalar conserva ProgramData por defecto.
- Master Backend no lee archivos locales del Agent.
- Master Backend no implementa JWT, hardware fingerprint, Installation Identity ni licencia.
- IPC v1 es read-only y no habilita control remoto.
- Acceso al pipe no equivale a autorizacion para operaciones privilegiadas futuras.
- Futuras comunicaciones Service <-> Session deberan distinguir `sessionId`, user context y estado active/interactive, pero esos datos no seran seguridad por si solos.

## Cambios rechazados / No repetir

- No implementar ejecucion remota arbitraria.
- No lanzar procesos interactivos desde el Windows Service.
- No usar `CreateProcessAsUser`, `WTSQueryUserToken`, `CreateProcessWithTokenW`, token stealing ni trucos de Session 0 para iniciar el Session Agent.
- No instalar Session Agent en AppData, Desktop, Startup folder ni repositorio.
- No ejecutar el Session Agent como `LocalSystem` ni elevarlo artificialmente.
- No usar mutex global que impida agentes por sesion.
- No implementar activacion de licencia por IPC.
- No implementar operaciones write por IPC.
- No crear tray icon, ventana, toast ni UI React/Tauri todavia.
- No implementar Galtek Hub, generacion de licencias, private keys comerciales, activacion online, revocacion online, descarga de public key, DPAPI, gRPC, mTLS, mDNS, pairing, captura, bloqueo, comandos remotos, SQLite ni WebRTC.
- No agregar Commercial License ni llaves publicas al Master Backend.
- No confiar en una llave publica enviada junto con un JWT.
- No mezclar Commercial License dentro de `installation.json`.
- No usar MSI, WiX, Inno Setup, NSIS ni installer grafico todavia.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- El entorno tiene `DEBUG=release`; el backend fuerza `debug=false` salvo configuracion explicita por argumentos/JVM properties para evitar logs DEBUG accidentales.
- No existe todavia una llave publica real de Galtek Hub empaquetada; sin `GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH`, una licencia existente queda en `LICENSE_KEY_NOT_CONFIGURED`.
- `license.dat` no se cifra localmente en esta fase. Su integridad depende de la firma RS256, `installationId` y hardware 3 de 4; DPAPI/ACL hardening queda pendiente.
- La validacion real con Service Control Manager y Task Scheduler productivo depende de que el entorno este elevado.
- Como el Session Agent productivo es `WinExe`, los diagnosticos del `.exe` publicado deben invocarse con espera explicita si se necesita capturar salida desde PowerShell; en desarrollo puede usarse `dotnet GaltekClassroom.Agent.Session.dll --ipc-ping`.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 56 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 14 pruebas superadas.
- Parser PowerShell de `installer/windows/*.ps1`: correcto.
- `.\installer\windows\publish-agent-session.ps1`: correcto; publica `Release`, `win-x64`, self-contained en `artifacts\windows\agent-session\`.
- Artifact Session publicado: `.exe` y dependencias presentes; `artifacts/` ignorado por Git.
- `.\installer\windows\publish-agent.ps1`: correcto; publica Service y Session.
- `install-session-agent.ps1` y `uninstall-session-agent.ps1` validan elevacion y fallan temprano en entorno no elevado.
- Session Agent `--background` publicado: primera instancia queda ejecutando; segunda instancia en la misma sesion termina; `SessionId = 12`, no Session 0.
- Prueba local de restart con Service publicado en modo consola: Session Agent sigue vivo cuando el Service cae y sigue vivo tras levantar nuevamente el Service.
- Session Agent `--ipc-ping` contra Service publicado: correcto, devuelve `UP`.
- Session Agent `--ipc-status` contra Service publicado: correcto, devuelve estado seguro `ACTIVATION_REQUIRED`.

## Proximo paso recomendado

Definir en Prompt 07 el contrato Service <-> Session para identificar sesiones interactivas sin agregar todavia control remoto ni captura.
