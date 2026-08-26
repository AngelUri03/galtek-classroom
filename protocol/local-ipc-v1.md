# Local IPC API v1

Esta es la especificacion canonica cross-language para el IPC local entre:

- `GaltekClassroom.Agent.Service`
- `GaltekClassroom.Agent.Session`
- `master-backend`

IPC v1 es estrictamente read-only. No define activacion de licencia ni comandos operativos.

## Transporte

- Plataforma objetivo: Windows.
- Transporte: Windows Named Pipe.
- Pipe name: `GaltekClassroom.Agent.v1`
- Ruta Java equivalente: `\\.\pipe\GaltekClassroom.Agent.v1`
- Servidor: `GaltekClassroom.Agent.Service`
- Clientes actuales: Session Agent C# y Master Backend Java.

## Framing

Cada mensaje usa:

```text
4 bytes length prefix BIG ENDIAN
+
JSON UTF-8
```

El prefijo indica solamente el tamano del payload JSON en bytes. No incluye los 4 bytes del prefijo.

## Limite

- Tamano maximo de payload JSON: `64 KiB` (`65536` bytes).
- Longitudes `<= 0` son invalidas.
- Longitudes mayores al limite son invalidas y deben rechazarse antes de reservar el payload.

## JSON

El JSON usa UTF-8 y nombres de propiedades estilo camelCase.

## Request

```json
{
  "protocolVersion": 1,
  "requestId": "11111111-1111-1111-1111-111111111111",
  "operation": "GET_DEVICE_STATUS",
  "payload": {}
}
```

Reglas:

- `protocolVersion` debe ser `1`.
- `requestId` debe ser un UUID y debe conservarse exactamente en la respuesta.
- `operation` debe pertenecer al conjunto permitido de IPC v1.
- `payload` existe para compatibilidad futura, pero en IPC v1 las operaciones permitidas no requieren datos.

## Response

```json
{
  "protocolVersion": 1,
  "requestId": "11111111-1111-1111-1111-111111111111",
  "success": true,
  "errorCode": null,
  "payload": {}
}
```

Reglas:

- `protocolVersion` se mantiene en `1`.
- `requestId` debe ser el mismo UUID recibido.
- `success = false` debe incluir `errorCode`.
- `payload` contiene el resultado cuando `success = true`.

## Operaciones Permitidas

### PING

Verifica disponibilidad local del Agent Service.

Payload de respuesta:

```json
{
  "service": "Galtek Classroom Agent Service",
  "status": "UP",
  "protocolVersion": 1
}
```

### GET_DEVICE_STATUS

Devuelve estado seguro derivado del estado interno del Agent Service.

Payload de respuesta:

```json
{
  "product": "GALTEK_CLASSROOM",
  "installationId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "hostname": "PC-AULA-07",
  "licenseStatus": "ACTIVATION_REQUIRED",
  "active": false,
  "licenseId": null,
  "organizationId": null,
  "expiresAtUtc": null,
  "lastValidatedAtUtc": "2026-08-25T15:00:00Z",
  "roles": [],
  "features": {}
}
```

Campos permitidos:

- `product`
- `installationId`
- `hostname`
- `licenseStatus`
- `active`
- `licenseId`
- `organizationId`
- `expiresAtUtc`
- `lastValidatedAtUtc`
- `roles`
- `features`

Campos sensibles prohibidos:

- JWT o `license.dat`
- `cpuHash`
- `motherboardHash`
- `macHash`
- `diskHash`
- seriales crudos de hardware
- public key
- private key
- rutas internas locales

### GET_MACHINE_CODE

Devuelve el Machine Code producido por la implementacion existente del Agent Service.

Payload de respuesta:

```json
{
  "machineCode": "base64-json"
}
```

Esta operacion debe funcionar aunque `licenseStatus != ACTIVE`, porque el Machine Code se necesita para activacion futura.

## Errores

Codigos actuales:

- `IPC_PROTOCOL_UNSUPPORTED`
- `IPC_OPERATION_NOT_SUPPORTED`
- `IPC_MALFORMED_REQUEST`
- `IPC_INVALID_REQUEST`
- `IPC_INTERNAL_ERROR`
- `IPC_RESPONSE_MISMATCH`

El Master Backend mapea indisponibilidad del Agent Service a HTTP `503` con:

```json
{
  "code": "LOCAL_AGENT_UNAVAILABLE"
}
```

## Seguridad

IPC v1 es read-only. Las unicas operaciones permitidas son:

- `PING`
- `GET_DEVICE_STATUS`
- `GET_MACHINE_CODE`

No estan permitidas operaciones write como activacion, bloqueo, apagado, proyeccion, apertura de aplicaciones ni ejecucion de comandos.

ACL actual del pipe:

- `LocalSystem`: `FullControl`
- `BuiltinAdministrators`: `FullControl`
- `Authenticated Users`: `ReadWrite | Synchronize`

Este acceso permite que procesos locales necesarios consulten estado desde sesiones de usuario estandar. Acceso al pipe no equivale a autorizacion para futuras operaciones privilegiadas. Antes de agregar operaciones write debe existir una fase especifica de autorizacion local.

## Resiliencia

La implementacion debe:

- rechazar JSON malformado sin detener el Agent Service;
- rechazar tamanos invalidos sin reservar buffers peligrosos;
- tolerar clientes que se desconectan;
- soportar varios clientes locales;
- respetar cancelacion del host;
- validar `requestId` en clientes.
