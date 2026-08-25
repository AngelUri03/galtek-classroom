# Arquitectura

## Estado general

Prompt 02 implementa la Installation Identity local del Agent y la generacion de Machine Code para desarrollo. Cualquier capacidad operativa real de administracion remota sigue planificada o no implementada.

## Master

IMPLEMENTADO:

- Backend minimo en `master-backend/`.
- Java 21.
- Spring Boot 3.x.
- Maven.
- Package base `com.galtek.classroom`.
- Endpoint `GET /api/system/health`.
- Prueba automatica del health endpoint.

PLANIFICADO:

- React + Tauri para UI de escritorio, sin Vite.
- SQLite para almacenamiento local del Master.
- gRPC/Protobuf para comunicacion con Agents.
- Visualizacion de equipos, miniaturas y estado.
- Auditoria administrativa.

NO IMPLEMENTADO:

- UI.
- Autenticacion.
- Base de datos.
- Dispositivos/aulas.
- gRPC funcional.
- Descubrimiento.
- Licencias.

## Agent

El Agent se divide en dos procesos para separar privilegios de sistema y trabajo dentro de la sesion interactiva.

### Galtek Classroom Service

IMPLEMENTADO:

- Proyecto C# `GaltekClassroom.Agent.Service`.
- Worker Service / Generic Host en .NET 8.
- Puede arrancarse desde consola para desarrollo.
- Registra inicio, estado activo y detencion limpia.
- Es autoridad local de Installation Identity.
- Resuelve `installation.json` al arrancar.
- Crea una identidad permanente cuando no existe.
- Reutiliza el mismo `installationId` cuando el archivo existe y es valido.
- Falla de forma controlada si `installation.json` esta corrupto o incompleto.
- Genera Machine Code Base64 en modo de desarrollo con `--machine-code`.

PLANIFICADO:

- Ejecucion como Windows Service.
- Validacion local de licencia.
- Comunicacion segura.
- Heartbeat.
- Recepcion de comandos estructurados.
- Operaciones privilegiadas.
- Coordinacion con Session Agent.

NO IMPLEMENTADO:

- Instalacion automatica como Windows Service.
- Comandos remotos.
- Comunicacion de red.
- Licenciamiento.
- Activacion.
- IPC local con Master Backend.

### Galtek Classroom Session Agent

IMPLEMENTADO:

- Proyecto C# `GaltekClassroom.Agent.Session`.
- Ejecutable minimo de consola.
- Registra inicio y cierre limpio.

PLANIFICADO:

- Proceso dentro de la sesion interactiva del usuario.
- Captura de pantalla.
- Recepcion de proyeccion.
- Interaccion con escritorio.
- Bloqueo de entrada.
- Ejecucion controlada de aplicaciones.
- Overlays.
- IPC local con Windows Service mediante Named Pipes.

NO IMPLEMENTADO:

- UI.
- Captura de pantalla.
- Bloqueo de teclado/mouse.
- Named Pipes.
- Proyeccion.

### GaltekClassroom.Agent.Shared

IMPLEMENTADO:

- Proyecto C# `GaltekClassroom.Agent.Shared`.
- Constantes minimas de producto.
- Modelos compartidos de Installation Identity.
- Modelo de Machine Code.
- Constantes de `schemaVersion` y archivo `installation.json`.

PLANIFICADO:

- Estados.
- Contratos internos.
- Abstracciones de licenciamiento.
- Contratos IPC.

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

## IPC local futuro

PLANIFICADO:

- La comunicacion entre Windows Service y Session Agent usara Named Pipes.

NO IMPLEMENTADO:

- Contratos IPC.
- Named Pipe server/client.
- Autorizacion local entre procesos.

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

PLANIFICADO:

- Validacion tolerante futura: 3 de 4 componentes de hardware deben coincidir contra Commercial License.

NO IMPLEMENTADO:

- Validacion contra licencia.
- Comparacion 3 de 4 contra JWT.
- Reparacion automatica de identidades corruptas.

### Commercial License

PLANIFICADO:

- Galtek Hub genera licencias.
- Galtek Classroom solo valida JWT firmados con RSA / RS256 usando llave publica.
- Claims previstos: `iss`, `aud`, `sub`, `jti`, `product`, `schemaVersion`, `organizationId`, hashes de hardware, `roles`, `features`, `iat`, `exp`.
- `sub` representa el `installationId` permanente.
- Roles previstos: `CLIENT`, `MASTER`.

NO IMPLEMENTADO:

- Galtek Hub.
- Generacion de licencias.
- Validacion JWT.
- Persistencia de licencias.

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
- Archivo actual: `installation.json`.
- Escritura de identidad con archivo temporal y reemplazo/movimiento para evitar JSON a medio escribir en cierres inesperados.

PLANIFICADO:

- Posibles archivos futuros: `license.dat`, `classroom.db` o configuracion local, logs.

NO IMPLEMENTADO:

- Base de datos local.
- Logs persistentes en disco.

## Limites de seguridad

VIGENTE DESDE AHORA:

- No permitir ejecucion remota arbitraria.
- No aceptar `cmd.exe /c`, PowerShell arbitrario, shell remota ni rutas arbitrarias enviadas por un Master.
- Usar comandos futuros explicitos y estructurados, por ejemplo `LOCK_INPUT`, `UNLOCK_INPUT`, `OPEN_APPLICATION` con `appId`, `SHUTDOWN`, `RESTART`, `START_PROJECTION`, `STOP_PROJECTION`.
- Las aplicaciones abribles remotamente deben pertenecer a un catalogo configurado previamente.
- IP y MAC no son identidad de autorizacion.
- Licencia MASTER valida no equivale a permiso automatico para controlar clientes de la LAN.
