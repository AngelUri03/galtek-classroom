# Estado actual

## Ultima actualizacion

2026-08-25 - Prompt 05.

## Estado del proyecto

`GaltekClassroom.Agent.Service` ya puede ejecutarse en dos modos con el mismo ejecutable: consola para desarrollo y Windows Service real para produccion. El servicio se publica self-contained para `win-x64`, se instala en Program Files, corre como `LocalSystem`, arranca con Windows en modo `Automatic` y mantiene IPC local read-only.

Installation Identity y Commercial License siguen siendo autoridad exclusiva del Agent Service. Los datos persistentes permanecen en `<CommonApplicationData>\Galtek\Classroom\`; instalar, actualizar o desinstalar normalmente no borra `installation.json` ni `license.dat`.

El producto todavia no tiene funciones operativas de administracion remota, UI, comunicacion de red, gRPC, mTLS, mDNS, pairing, captura, bloqueo, comandos remotos ni autostart del Session Agent.

## Implementado

- Backend Master minimo en Java 21 + Spring Boot 3.x + Maven.
- Endpoint `GET /api/system/health`, independiente del Agent Service.
- Endpoints `GET /api/device/status` y `GET /api/device/machine-code`.
- Flujo Java `controller -> service -> LocalAgentClient -> Named Pipe transport`.
- Respuesta HTTP 503 con codigo `LOCAL_AGENT_UNAVAILABLE` cuando el Agent Service no esta disponible.
- Solucion .NET `GaltekClassroom.Agent.sln` con Service, Session y Shared.
- Agent Service con Worker Service / Generic Host .NET 8.
- Integracion oficial `Microsoft.Extensions.Hosting.WindowsServices`.
- Windows Service instalable `GaltekClassroomAgent`.
- Display Name `Galtek Classroom Agent Service`.
- Description `Servicio local de Galtek Classroom para identidad, licencia y administracion segura del equipo.`
- Cuenta de servicio `LocalSystem`.
- Startup type `Automatic`.
- Recovery configurado por instalador: restart en 5, 15 y 60 segundos; reset en 86400 segundos.
- Publicacion `Release`, `win-x64`, self-contained, sin single-file.
- Artifact publicado en `artifacts/windows/agent-service/`, ignorado por Git.
- Instalacion/actualizacion PowerShell en `%ProgramFiles%\Galtek\Classroom\Agent\`.
- Desinstalacion PowerShell que conserva `%ProgramData%\Galtek\Classroom\` por defecto.
- Opcion explicita `-PurgeData` para borrar Installation Identity y Commercial License.
- Version inicial del ejecutable en `agent/src/GaltekClassroom.Agent.Service/GaltekClassroom.Agent.Service.csproj`.
- Installation Identity permanente del equipo en `installation.json`.
- Ubicacion de datos en `<CommonApplicationData>\Galtek\Classroom\` con override `GALTEK_CLASSROOM_DATA_DIR`.
- Hashes SHA-256 de CPU, motherboard, MAC fisicas y discos.
- Machine Code Base64 generado por la implementacion existente del Agent Service.
- Commercial License local en `GaltekClassroom.Agent.Service`.
- Persistencia separada de licencia en `license.dat`.
- Validacion JWT con `System.IdentityModel.Tokens.Jwt` y llave publica RSA.
- `LicenseState` en memoria sin exponer JWT completo.
- Monitor de expiracion runtime cada 60 segundos sin recalcular WMI.
- CLI de desarrollo: `--machine-code`, `--license-status`, `--activate-license` y `--activate-license-file`.
- Local IPC API v1 read-only.
- Named Pipe server en el Agent Service con pipe `GaltekClassroom.Agent.v1`.
- Contratos IPC compartidos en C# Shared.
- Framing IPC de 4 bytes BIG ENDIAN mas JSON UTF-8.
- Limite de payload IPC de 64 KiB.
- Operaciones IPC v1: `PING`, `GET_DEVICE_STATUS`, `GET_MACHINE_CODE`.
- `GET_DEVICE_STATUS` expone solo campos seguros.
- `GET_MACHINE_CODE` funciona aunque la licencia no este activa.
- Cliente IPC C# en Session Agent.
- Comandos Session Agent `--ipc-status` y `--ipc-ping`.
- Cliente IPC Java en Master Backend.
- Especificacion canonica `protocol/local-ipc-v1.md`.
- Proyecto xUnit `GaltekClassroom.Agent.Service.Tests`.
- Pruebas Java para health, device endpoints, framing y cliente IPC.
- Documentacion de contexto, arquitectura, decisiones e historial.

## En progreso

- Ningun desarrollo activo dejado a medias dentro del Prompt 05.

## Pendiente inmediato

- Prompt 06: definir lifecycle/autostart del Session Agent en la sesion interactiva del usuario sin lanzar UI desde Session 0.
- Definir como se empaquetara la llave publica real de Galtek Hub para produccion.
- Disenar autorizacion local antes de cualquier operacion IPC write.

## Cambios aceptados

- Separacion Master backend / Agent / Protocol / Docs.
- Agent separado en Windows Service y Session Agent.
- Agent Service como autoridad local de Installation Identity.
- Agent Service como autoridad local de Commercial License.
- Agent Service como servidor unico de IPC local.
- Agent Service instalable como Windows Service real.
- Program Files contiene binarios; ProgramData contiene identidad, licencia y datos persistentes.
- Actualizar binarios no modifica ni regenera Installation Identity.
- Desinstalar conserva ProgramData por defecto.
- Master Backend no lee archivos locales del Agent.
- Master Backend no implementa JWT, hardware fingerprint, Installation Identity ni licencia.
- IPC v1 es read-only y no habilita control remoto.
- Acceso al pipe no equivale a autorizacion para operaciones privilegiadas futuras.

## Cambios rechazados / No repetir

- No implementar ejecucion remota arbitraria.
- No instalar ni autoarrancar `GaltekClassroom.Agent.Session` todavia.
- No lanzar procesos interactivos desde el Windows Service.
- No implementar activacion de licencia por IPC en Prompt 05.
- No implementar operaciones write por IPC en Prompt 05.
- No crear UI React/Tauri todavia.
- No implementar Galtek Hub, generacion de licencias, private keys comerciales, activacion online, revocacion online, descarga de public key, DPAPI, gRPC, mTLS, mDNS, pairing, captura, bloqueo, comandos remotos, SQLite ni WebRTC en Prompt 05.
- No agregar Commercial License ni llaves publicas al Master Backend.
- No confiar en una llave publica enviada junto con un JWT.
- No mezclar Commercial License dentro de `installation.json`.
- No usar MSI, WiX, Inno Setup, NSIS ni installer grafico todavia.

## Problemas conocidos

- El `dotnet` del PATH global puede apuntar solo al runtime; usar `C:\Users\angel\.dotnet\dotnet.exe` o ajustar PATH para acceder al SDK 8.0.424.
- El entorno tiene `DEBUG=release`; el backend fuerza `debug=false` salvo configuracion explicita por argumentos/JVM properties para evitar logs DEBUG accidentales.
- No existe todavia una llave publica real de Galtek Hub empaquetada; sin `GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH`, una licencia existente queda en `LICENSE_KEY_NOT_CONFIGURED`.
- `license.dat` no se cifra localmente en esta fase. Su integridad depende de la firma RS256, `installationId` y hardware 3 de 4; DPAPI/ACL hardening queda pendiente.
- La validacion real con Service Control Manager depende de que el entorno este elevado.

## Pruebas ejecutadas

- `C:\Users\angel\.dotnet\dotnet.exe build .\GaltekClassroom.Agent.sln` en `agent`: correcto, 0 advertencias, 0 errores.
- `C:\Users\angel\.dotnet\dotnet.exe test .\GaltekClassroom.Agent.sln` en `agent`: correcto, 42 pruebas superadas.
- `mvn clean verify` en `master-backend`: correcto, 14 pruebas superadas.
- Parser PowerShell de `installer/windows/*.ps1`: correcto.
- `.\installer\windows\publish-agent-service.ps1`: correcto; publica `Release`, `win-x64`, self-contained en `artifacts\windows\agent-service\`.
- Artifact publicado: `.exe` y dependencias presentes; `artifacts/` ignorado por Git.
- Ejecucion directa del `.exe` publicado con `GALTEK_CLASSROOM_DATA_DIR` temporal: correcto; crea identidad temporal, levanta IPC y responde `--ipc-ping` con `UP`.
- `--ipc-status` contra el `.exe` publicado: correcto; devuelve estado con `ACTIVATION_REQUIRED`.
- CLI del `.exe` publicado: `--machine-code` y `--license-status` terminan correctamente sin dejar proceso permanente.
- Validacion alternativa Master Backend contra `.exe` publicado en modo consola: `GET /api/system/health` correcto, `GET /api/device/status` correcto tras reintento, `GET /api/device/machine-code` correcto.
- Validacion de elevacion: entorno no elevado (`IsElevated: False`); `install-agent-service.ps1` y `uninstall-agent-service.ps1` fallan temprano con mensaje claro.
- Validacion real del Service Control Manager pendiente por falta de elevacion del entorno.

## Proximo paso recomendado

Implementar el lifecycle/autostart del Session Agent al inicio de sesion del usuario, manteniendo el Windows Service sin UI y sin comandos remotos.
