# Local IPC API v1

Esta es la especificacion canonica cross-language para el IPC local entre:

- `GaltekClassroom.Agent.Service`
- `GaltekClassroom.Agent.Session`
- `master-backend`

IPC v1 es estrictamente read-only. No define activacion de licencia, cambios de binding, escritura de configuracion ni comandos operativos.

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
- Ninguna request de IPC v1 debe enviar SID de Windows para autorizacion.

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

### GET_MASTER_AUTHORIZATION

Devuelve el estado de autorizacion local Master calculado por el Agent Service.

Payload de request:

```json
{}
```

El caller no declara su SID. El Agent Service obtiene el SID real del cliente conectado al Named Pipe usando impersonation del pipe y evalua:

```text
Commercial License ACTIVE
+
rol MASTER
+
binding.installationId == installationIdentity.installationId
+
SID real del cliente == SID ligado
```

Payload de respuesta:

```json
{
  "status": "AUTHORIZED",
  "authorized": true,
  "configured": true,
  "boundAccountDisplayName": "AULA\\MaestraPrimaria",
  "currentAccountDisplayName": "AULA\\MaestraPrimaria"
}
```

Estados actuales:

- `NOT_CONFIGURED`
- `AUTHORIZED`
- `CURRENT_ACCOUNT_NOT_AUTHORIZED`
- `MASTER_LICENSE_REQUIRED`
- `MASTER_BINDING_INVALID`
- `MASTER_BINDING_INSTALLATION_MISMATCH`

Campos permitidos:

- `status`
- `authorized`
- `configured`
- `boundAccountDisplayName`
- `currentAccountDisplayName`

Campos sensibles prohibidos:

- SID completo del binding o del caller
- JWT o `license.dat`
- ruta de `master-binding.json`
- ACLs internas

Esta respuesta solo responde si la cuenta local actual puede usar esta instalacion Master. No autoriza control de clientes remotos, pairing, mTLS ni confianza de red.

### GET_MASTER_UNLOCK_AUTHORIZATION

Devuelve una autorizacion local de proposito unico para la accion recovery-safe `UNLOCK_INPUT`.

Payload de request:

```json
{}
```

El caller no declara su SID. El Agent Service obtiene el SID real del cliente conectado al Named Pipe usando impersonation del pipe y evalua:

```text
Installation Identity valida
+
Master Windows Binding existente y estructuralmente valido
+
binding.installationId == installationIdentity.installationId
+
SID real del cliente == SID ligado
```

Esta operacion NO exige Commercial License `ACTIVE`, NO lee claims/roles de una licencia invalida para autorizar recovery y NO convierte licencia expirada en permiso para cualquier administrador local. Si el binding falta, esta corrupto, usa schema desconocido, pertenece a otra instalacion, el SID real no puede obtenerse o el SID no coincide, devuelve `authorized = false`.

Payload de respuesta:

```json
{
  "status": "AUTHORIZED",
  "authorized": true,
  "configured": true
}
```

Estados reutilizados:

- `NOT_CONFIGURED`
- `AUTHORIZED`
- `CURRENT_ACCOUNT_NOT_AUTHORIZED`
- `MASTER_BINDING_INVALID`
- `MASTER_BINDING_INSTALLATION_MISMATCH`

Campos permitidos:

- `status`
- `authorized`
- `configured`

Campos sensibles prohibidos:

- SID completo del binding o del caller
- JWT o `license.dat`
- `LicenseState`, roles o raw claims
- `installationId` completo
- ruta de `master-binding.json`
- ACLs internas
- username/display name
- key material

Esta respuesta no significa "puede administrar Galtek", "puede ejecutar operaciones remotas" ni "puede saltarse la licencia". Solo significa que el caller Windows real corresponde al Master Windows Binding valido de esta instalacion para solicitar una accion declarada recovery-safe que reduce control. En 18B1 la unica accion prevista era `UNLOCK_INPUT`; desde Prompt 18B2 existen `POST /api/classrooms/{classroomId}/input-control/unlock` y `BatchOperation UNLOCK_INPUT`. `GET_MASTER_UNLOCK_AUTHORIZATION` sigue siendo Local IPC read-only interno, no tiene endpoint publico, no se generaliza a otras acciones y `MasterUnlockAccessGuard` permanece especifico de `UNLOCK_INPUT`.

### GET_RUNTIME_DIAGNOSTICS

Devuelve un snapshot ligero de runtime del proceso `GaltekClassroom.Agent.Service` en el momento de la solicitud. No inicia timer, no persiste telemetria, no escribe SQLite y no se envia por heartbeat.

Payload de request:

```json
{}
```

Payload de respuesta:

```json
{
  "product": "GALTEK_CLASSROOM",
  "component": "Galtek Classroom Agent Service",
  "capturedAtUtc": "2026-08-30T18:00:00Z",
  "samplingMode": "ON_DEMAND",
  "processId": 1234,
  "processName": "GaltekClassroom.Agent.Service",
  "startedAtUtc": "2026-08-30T17:45:00Z",
  "uptime": "00:15:00.0000000",
  "uptimeMs": 900000,
  "totalProcessorTime": "00:00:01.2340000",
  "totalProcessorTimeMs": 1234,
  "workingSetBytes": 52428800,
  "privateMemoryBytes": 67108864,
  "threadCount": 12,
  "managedMemoryBytes": 8388608
}
```

Campos permitidos:

- `product`
- `component`
- `capturedAtUtc`
- `samplingMode`
- `processId`
- `processName`
- `startedAtUtc`
- `uptime`
- `uptimeMs`
- `totalProcessorTime`
- `totalProcessorTimeMs`
- `workingSetBytes`
- `privateMemoryBytes`
- `threadCount`
- `managedMemoryBytes`

Los valores son aproximados y dependen del sistema operativo/runtime. No son contrato de producto ni deben convertirse en unit tests de memoria exacta.

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
- `GET_MASTER_AUTHORIZATION`
- `GET_MASTER_UNLOCK_AUTHORIZATION`
- `GET_RUNTIME_DIAGNOSTICS`

No estan permitidas operaciones write como activacion, set/update/delete de Master binding, bloqueo, apagado, proyeccion, apertura de aplicaciones ni ejecucion de comandos.

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
