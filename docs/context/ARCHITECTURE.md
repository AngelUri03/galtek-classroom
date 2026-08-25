# Arquitectura

## Estado general

Prompt 01 implementa solo la fundacion tecnica. Cualquier capacidad operativa real de administracion remota sigue planificada o no implementada.

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

PLANIFICADO:

- Ejecucion como Windows Service.
- Identidad de dispositivo.
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
- Fingerprint de hardware.

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

PLANIFICADO:

- Modelos comunes.
- Constantes compartidas.
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

PLANIFICADO:

- Identidad permanente de la instalacion.
- Componentes conceptuales: `installationId`, `cpuHash`, `motherboardHash`, `macHash`, `diskHash`.
- Hash local con SHA-256.
- Validacion tolerante: 3 de 4 componentes de hardware deben coincidir.

NO IMPLEMENTADO:

- Generacion de identidad.
- Fingerprint de hardware.
- Persistencia local.

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

PLANIFICADO:

- Usar `C:\ProgramData\Galtek\Classroom\`.
- Posibles archivos futuros: `installation.json`, `license.dat`, `classroom.db` o configuracion local, logs.

NO IMPLEMENTADO:

- Escritura de archivos locales del Agent.
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
