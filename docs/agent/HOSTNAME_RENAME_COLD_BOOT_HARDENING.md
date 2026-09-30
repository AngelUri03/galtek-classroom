# 20E.0B4 - Hostname rename and cold-boot startup hardening

Estado de este bloque: **CODE/LOCAL VALIDATED**. La validacion fisica en PC14 sigue pendiente. Codex no reinicio, desplego, renombro ni inicio/detuvo servicios en PC14 durante este trabajo.

## Evidencia fisica que origino el cambio

Despues del rename `ICH11 -> PC14` y el reboot, Windows confirmo `hostname=PC14`, `DNSHostName=PC14` y `WORKGROUP`. `GaltekClassroomAgent` conservaba `Automatic (Delayed)`, `LocalSystem`, ImagePath correcto y recovery `5/15/60`, reset `86400`, `failureflag=true`, pero termino `Stopped`.

SCM registro 7009 por 30000 ms esperando que el proceso se conectara y luego 7000. WER registro `APPCRASH`, `e0434352`, para `GaltekClassroom.Agent.Service.exe` 0.5.0.0. Ese codigo solo demuestra una excepcion CLR administrada; el tipo exacto no esta demostrado. Google Update tambien obtuvo 7009/7000 en el mismo boot, por lo que existia presion general de arranque.

Sin cambiar hostname ni configuracion, un `Start-Service GaltekClassroomAgent` posterior paso en aproximadamente 21267 ms y conservo cuenta, modo e ImagePath. La evidencia no demuestra que `PC14` o el rename sean la causa; el rename provoco el reboot en el que reaparecio el defecto.

## Auditoria de la frontera SCM anterior

La ruta productiva anterior era:

```text
process entry
  -> bootstrap opt-in y parse de argumentos
  -> Host.CreateApplicationBuilder
  -> carga de configuracion/appsettings y logging
  -> registro completo de DI
  -> AddWindowsService
  -> registro de todos los hosted services
  -> builder.Build (DI, logging, EventLog y WindowsServiceLifetime)
  -> host.RunAsync
  -> Host.StartAsync
  -> WindowsServiceLifetime.WaitForStartAsync
  -> ServiceBase.Run / conexion SCM
```

Mover trabajo desde `Worker.StartAsync` hacia `ExecuteAsync` no protegia esta ruta: toda la creacion/configuracion/build del Generic Host aun ocurria antes de `ServiceBase.Run`. No se demostro una unica llamada lenta ni el tipo de excepcion administrada. El cuello probable y corregible era el conjunto no acotado de trabajo pre-SCM bajo contencion de cold boot.

La auditoria no encontro WMI/CIM, DNS, gRPC ni llamadas remotas explicitas antes de la frontera anterior, pero configuracion, filesystem implícito del host, logging/EventLog, construccion de DI, activacion de lifetime y carga de assemblies seguian expuestos antes de conectar con SCM.

## Arquitectura corregida

En modo Windows Service la ruta nueva es:

```text
process entry
  -> diagnostico opt-in sin I/O por default
  -> parse minimo de argumentos
  -> WindowsServiceHelpers.IsWindowsService
  -> DeferredWindowsService minimo
  -> ServiceBase.Run / conexion SCM
  -> OnStart: marker SCM_CONNECTED + schedule asincrono + return
  -> construccion/configuracion del Generic Host
  -> DI build
  -> Host.StartAsync
  -> readiness local: InstallationIdentity disponible
  -> AGENT_READY
  -> identidad de red / licencia / pipes / Master gRPC y retry normales
```

`DeferredWindowsService` conserva `Stop` y `Shutdown`, limita la espera de parada y usa `RequestAdditionalTime` solamente despues de que SCM ya esta conectado. `DeferredHostLifetime` evita instalar un segundo lifetime SCM dentro del Generic Host. El modo consola y los comandos administrativos conservan el builder normal existente.

Un fallo de construccion o startup registra solamente tipo de excepcion y HRESULT, marca exit code Win32 1064 y solicita la parada del servicio desde otro work item para no esperar el propio task de startup. Una terminacion interna inesperada del Generic Host se trata igual. No se persisten mensajes de excepcion, passwords, tokens ni material criptografico.

No se agrego `ServicesPipeTimeout`, timeout global, Scheduled Task sustituto, sleep productivo, watchdog, taskkill ni wrapper externo.

## SCM conectado versus Agent ready

`SCM_CONNECTED` significa que SCM ya invoco `OnStart`; no afirma que el Agent sea funcional. `AGENT_READY` se escribe solo despues de que `Host.StartAsync` termino y `AgentRuntimeState` recibio la `InstallationIdentity`. La identidad de red, DNS, Master y gRPC no forman parte del handshake SCM ni son requisito para que el servicio quede `Running`. El Master conserva la autoridad de ONLINE mediante el protocolo real.

Los diagnostics siguen siendo opt-in mediante `GALTEK_BOOTSTRAP_STARTUP_DIAGNOSTICS=1`; apagados no resuelven ProgramData ni escriben archivos. Los markers son `PROCESS_ENTER`, `SCM_CONNECT_BEGIN`, `SCM_CONNECTED`, `HOST_INIT_BEGIN`, `HOST_INIT_COMPLETE`, `AGENT_START_BEGIN` y `AGENT_READY`, con `elapsedMs` monotono. Fallos criticos usan EventLog solamente si la fuente ya existe.

## Gate local de rendimiento

`DeferredHostCoordinatorTests` programa una inicializacion post-conexion de 45 segundos y demuestra que el callback que modela `OnStart` retorna por debajo de 5 segundos. La ejecucion local del 2026-09-29 midio `SCM_CALLBACK_RETURN_ELAPSED_MS=0.111`; la inicializacion seguia pendiente y `HOST_INIT_COMPLETE` no se habia emitido. Esto valida el limite interno posterior a `SCM_CONNECTED`; el tiempo fisico completo `PROCESS_ENTER -> SCM_CONNECTED` requiere el retest en PC14.

Antes del cambio, la evidencia fisica era un timeout SCM a 30000 ms sin conexion exitosa. No se inventa un tiempo local equivalente para la arquitectura anterior.

## Rename e identidades persistentes

El hostname es display/observacion, no autoridad de seguridad:

| Store o dato | Autoridad estable | Efecto de `ICH11 -> PC14` |
|---|---|---|
| `installation.json` | `installationId` + hashes de hardware | mismos bytes; machine code puede mostrar el hostname live |
| `network-identity.json` y private key CNG | installation/network identity IDs, key ID y fingerprint | sin recreacion ni rewrite por hostname |
| `authorized-masters.json` | IDs criptograficos, fingerprints y certificado | sin re-pair ni rewrite |
| `license.dat` | token firmado ligado a la Installation Identity | sin relicenciamiento ni rewrite |
| `managed-windows-accounts.json` | `accountId` + Windows SID | PRIMARY, SECONDARY y ADMIN siguen vinculados por el mismo SID |
| `managed-windows-credentials.dat` | installation ID + accountId; uso validado contra SID del binding | mismo ciphertext; no reprovisiona password |

`accountReference` es metadata. Inventario/status resuelve el SID live y puede mostrar `PC14\usuario` aunque el catalogo aun contenga `ICH11\usuario`, sin modificar el binding ni la credencial. Una actualizacion durable futura solo puede ocurrir con evidencia live y nunca cambia la autoridad SID.

Pruebas nuevas cubren identidad persistida byte a byte, hostname live del machine code, Network Identity, authorized master y licencia byte a byte, display actualizado por SID, los tres roles por SID, deteccion de sesion y adquisicion de credencial tras rename sin reescribir binding ni ciphertext. Las pruebas existentes de Network Identity, trust/pairing y licencia siguen cubriendo sus validadores individuales.

## Independencia de red y Session Agent

`MasterConnectionHostedService` hace yield antes de todo trabajo, espera identidades despues del startup y usa el retry/backoff saliente existente. Las pruebas cubren Master inicialmente no disponible, `IOException`, `HttpRequestException`, desconexion y recuperacion, offline sostenido y cancelacion. Ethernet, DHCP, DNS o Master no participan en el callback SCM.

El Session Agent permanece en Task Scheduler. `SessionAgentSupervisor` trata servicio ausente/inicializando como `WaitingForService`, aplica backoff acotado, no termina ni bloquea login y llega a `Ready` cuando el IPC aparece. No es requisito para que el Service entre en `Running`.

## Plan fisico obligatorio para el candidato 0.0.5

Cada caso comienza con cold boot y no permite `Start-Service` manual para declarar PASS:

1. PC14, Ethernet conectado: `GaltekClassroomAgent` debe quedar `Running` automaticamente.
2. PC14, Ethernet desconectado: debe quedar `Running` automaticamente.
3. PC14, Master apagado: debe quedar `Running` automaticamente.
4. Tras reconectar red/encender Master: el Agent debe conectar por retry normal sin restart manual.

En todos los casos recolectar servicio/cuenta/start mode/ImagePath/recovery, eventos SCM 7009/7000, WER nuevo por EventTime, diagnostics opt-in con elapsed y hashes/contenido logico de los seis stores antes/despues. Verificar PRIMARY, SECONDARY y ADMIN, credenciales y session detection. Esta pagina no declara ese retest fisico completado.
