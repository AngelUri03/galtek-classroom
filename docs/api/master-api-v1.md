# Master API v1

Estado: implementado inicial en Prompt 10; ampliado en Prompt 14 con Clients de red y registro de Devices; ampliado en Prompt 15B con dispatch batch de power control.

Esta API es local al Master Backend y existe para la futura UI React/Tauri. No ejecuta comandos remotos arbitrarios y no mueve `StudentWorkspace` en filesystem. Prompt 14 permite registrar como `Device` persistente a un Client ya paired; el Master genera el `deviceId` y vincula el Device con la Network Identity en SQLite. Prompt 15B permite enviar solo `SHUTDOWN`/`RESTART` tipados por el framework gRPC seguro. Las asignaciones `Student -> Device` solo modifican metadata SQLite.

## Proteccion

Todos los endpoints de esta API administrativa llaman primero a `MasterAccessGuard`, que consulta `GET_MASTER_AUTHORIZATION` al Agent Service por Named Pipe. El backend no acepta SID, username ni identidad declarada por HTTP.

Endpoints publicos de diagnostico que no usan `MasterAccessGuard`:

- `GET /api/system/health`
- `GET /api/device/status`
- `GET /api/device/machine-code`
- `GET /api/master/authorization`

Respuestas de proteccion:

- Agent Service caido: HTTP `503`, `code = LOCAL_AGENT_UNAVAILABLE`.
- Master no autorizado: HTTP `403`, `code = <authorization.status>`.
- Storage SQLite no disponible: HTTP `503`, por ejemplo `MASTER_DATABASE_UNAVAILABLE`.

## Errores

Formato uniforme:

```json
{
  "code": "CONCURRENT_MODIFICATION",
  "message": "The record was modified concurrently.",
  "detail": null
}
```

Codigos HTTP usados:

- `200`: lectura o mutacion exitosa.
- `201`: creacion.
- `400`: validacion de request.
- `403`: Master no autorizado.
- `404`: recurso no encontrado.
- `409`: conflicto, version optimista o assignment ocupado.
- `503`: Agent Service o storage local no disponible.

## Bootstrap

`GET /api/master/bootstrap`

Devuelve autorizacion derivada por Agent Service, estado de storage y aulas activas con conteos basicos.

```json
{
  "authorization": {
    "status": "AUTHORIZED",
    "authorized": true,
    "configured": true
  },
  "storage": {
    "status": "READY",
    "errorCode": null
  },
  "classrooms": [
    {
      "classroomId": "uuid",
      "displayName": "Aula Primaria",
      "active": true,
      "version": 0,
      "counts": {
        "groupCount": 2,
        "activeStudentCount": 25,
        "archivedStudentCount": 0,
        "deviceCount": 24,
        "currentAssignmentCount": 23,
        "applicationCount": 3
      }
    }
  ]
}
```

## Network Clients

- `GET /api/network/clients`
- `POST /api/classrooms/{classroomId}/devices/register`

Estos endpoints estan protegidos por `MasterAccessGuard`.

Conceptos obligatorios:

```text
Network Identity != Pairing != Device != Student
```

Reglas:

- `paired-clients.json` sigue siendo la autoridad de pairing/trust.
- SQLite no reemplaza trust; `device_network_bindings` solo vincula un Device persistente con una Network Identity paired.
- El Master genera y controla `deviceId`.
- `deviceId`, `displayName`, hostname, IP y MAC enviados o inferidos del Client son informativos y no son identidad de seguridad.
- Capabilities reportadas por `ClientHello` son informacion operativa; no autorizan acciones.
- `REVOKED` no es registrable ni administrable.

Estados de registro expuestos:

```text
PAIRED + ONLINE + sin Device -> AVAILABLE_FOR_REGISTRATION
PAIRED + binding vigente     -> REGISTERED
REVOKED                      -> REVOKED
```

List clients:

```json
[
  {
    "networkIdentityId": "uuid",
    "trustStatus": "PAIRED",
    "connectionStatus": "ONLINE",
    "registrationStatus": "AVAILABLE_FOR_REGISTRATION",
    "registered": false,
    "deviceId": null,
    "classroomId": null,
    "displayName": "PC01",
    "agentVersion": "0.5.0",
    "capabilities": ["HEARTBEAT_V1", "OPERATION_FRAMEWORK_V1"],
    "lastSeenUtc": "2026-08-28T18:00:00Z",
    "lastConnectedUtc": "2026-08-28T18:00:00Z"
  }
]
```

Register device:

```json
{
  "networkIdentityId": "uuid",
  "displayName": "PC01"
}
```

Respuesta:

```json
{
  "deviceId": "uuid-generated-by-master",
  "classroomId": "uuid",
  "networkIdentityId": "uuid",
  "installationId": "uuid",
  "displayName": "PC01",
  "agentVersion": "0.5.0",
  "capabilities": ["HEARTBEAT_V1", "OPERATION_FRAMEWORK_V1"],
  "registeredAtUtc": "2026-08-28T18:00:00Z",
  "lastConnectedUtc": "2026-08-28T18:00:00Z"
}
```

Errores relevantes:

- `403 MASTER_NOT_PAIRED`: el Client no esta paired con este Master.
- `403 CLIENT_REVOKED`: el trust del Client fue revocado.
- `404 CLASSROOM_NOT_FOUND`: el aula no existe.
- `409 NETWORK_IDENTITY_ALREADY_REGISTERED`: la Network Identity ya tiene Device vigente.
- `409 DEVICE_ALREADY_REGISTERED`: la Installation Identity ya pertenece a un Device activo o binding vigente.
- `503 MASTER_DATABASE_UNAVAILABLE` u otro codigo de storage: SQLite no esta disponible.

## Power Control

`POST /api/classrooms/{classroomId}/power-control`

Endpoint protegido por `MasterAccessGuard`. Ejecuta una operacion batch remota tipada para `SHUTDOWN` o `RESTART` sobre Devices explicitos del aula.

Request:

```json
{
  "type": "SHUTDOWN",
  "targetDeviceIds": [
    "device-1",
    "device-2"
  ]
}
```

Reglas de request:

- `type` solo acepta `SHUTDOWN` o `RESTART`.
- `targetDeviceIds` es obligatorio, no puede estar vacio y no puede contener duplicados.
- No existe `all=true`; la UI futura resuelve la seleccion y envia IDs explicitos.
- No se aceptan comandos, `executablePath`, argumentos arbitrarios, timeout, force, mensaje, shell ni payload JSON libre.

Preflight por target:

- El Device debe pertenecer al aula solicitada.
- El Device debe estar registrado y tener binding vigente con Network Identity.
- El trust debe estar `PAIRED` y no `REVOKED`.
- Debe existir conexion gRPC/mTLS autenticada `ONLINE`.
- El Client debe anunciar `POWER_CONTROL_V1`.

`POWER_CONTROL_V1` indica soporte tecnico, no autorizacion. Trust, mTLS, registro y autorizacion local del Master siguen siendo obligatorios.

Respuesta:

```json
{
  "operationId": "uuid-batch",
  "type": "SHUTDOWN",
  "status": "PARTIAL_SUCCESS",
  "targetCount": 3,
  "successCount": 2,
  "failedCount": 1,
  "targets": [
    {
      "deviceId": "device-1",
      "status": "SUCCESS",
      "errorCode": null,
      "message": "Agent reported operation success.",
      "attempt": 1
    },
    {
      "deviceId": "device-3",
      "status": "FAILED",
      "errorCode": "DEVICE_OFFLINE",
      "message": "Device is offline.",
      "attempt": 1
    }
  ]
}
```

Semantica:

- `SUCCESS` significa que el Agent reporto que Windows acepto la solicitud; no significa que el equipo ya este apagado o reiniciado.
- `OperationAccepted` no marca exito.
- `OPERATION_REJECTED` puede aparecer si el Agent responde `OperationAccepted` con estado `REJECTED` o `UNSPECIFIED`, o si devuelve un `OperationResult` con el error tipado equivalente. Actualmente no es retryable y no equivale a `OPERATION_RESULT_UNKNOWN`.
- Si el Device estaba offline antes de enviar, se registra `DEVICE_OFFLINE` retryable.
- Si la request fue enviada pero no llega `OperationResult` por timeout o desconexion, se registra `OPERATION_RESULT_UNKNOWN`, no retryable.
- La correlacion interna usa `(deviceId, operationId)`, aunque el mismo `operationId` de batch se envie a varios Agents.
- La UI/API debe decidir por `errorCode` tipado y no por texto de `message`.

Errores relevantes:

- `400 INVALID_REQUEST`: body faltante, `type` no permitido, targets vacios/duplicados o campos no soportados.
- `403 <authorization.status>`: Master local no autorizado.
- `404 CLASSROOM_NOT_FOUND`: aula inexistente.
- Target `DEVICE_NOT_FOUND`: Device no pertenece al aula.
- Target `DEVICE_NOT_REGISTERED`: Device sin binding vigente.
- Target `MASTER_NOT_PAIRED` o `CLIENT_REVOKED`: trust invalido.
- Target `CAPABILITY_NOT_SUPPORTED`: no anuncia `POWER_CONTROL_V1`.
- Target `DEVICE_OFFLINE`: no hay conexion autenticada online antes del envio.
- Target `OPERATION_REJECTED`: el Agent rechazo la operacion mediante respuesta tipada; no retryable actualmente.
- Target `OPERATION_RESULT_UNKNOWN`: resultado incierto despues del envio.

## Classrooms

- `GET /api/classrooms?active=true|false`
- `POST /api/classrooms`
- `PATCH /api/classrooms/{id}`
- `POST /api/classrooms/{id}/archive`

Create:

```json
{
  "displayName": "Aula Primaria",
  "authorizedApplicationIds": ["math-app"],
  "defaultBrowserProfileId": null,
  "workspaceRecoveryPlanned": true,
  "batchConfirmationsRequired": true
}
```

Patch requiere `expectedVersion`. Si `authorizedApplicationIds` se envia, reemplaza la lista del aula.

Archive requiere `expectedVersion` y falla con `CLASSROOM_HAS_ACTIVE_CONTENT` si quedan grupos, alumnos activos, devices, assignments actuales o aplicaciones asociadas.

## Groups

- `GET /api/classrooms/{id}/groups?active=true|false`
- `POST /api/classrooms/{id}/groups`
- `PATCH /api/groups/{id}`
- `POST /api/groups/{id}/archive`

Create:

```json
{
  "grade": "1",
  "section": "A",
  "displayName": "1 A"
}
```

Patch/archive requieren `expectedVersion`. Archive falla con `GROUP_HAS_ACTIVE_STUDENTS` si el grupo aun tiene alumnos activos.

## Students

- `GET /api/classrooms/{id}/students?groupId=&active=&search=`
- `GET /api/students/{id}`
- `POST /api/classrooms/{id}/students`
- `POST /api/classrooms/{id}/students/batch`
- `PATCH /api/students/{id}`
- `POST /api/students/{id}/archive`
- `POST /api/students/archive-batch`

Create:

```json
{
  "clientReference": "row-1",
  "groupId": "uuid",
  "firstName": "Juan",
  "lastName": "Ramos",
  "displayName": "Juan Ramos",
  "grade": "1"
}
```

`displayName`, `grade`, `workspaceId` y `browserProfileId` tienen defaults. Los nombres duplicados estan permitidos; `studentId` es la identidad.

Batch create:

```json
{
  "students": [
    {
      "clientReference": "row-1",
      "groupId": "uuid",
      "firstName": "Juan",
      "lastName": "Ramos"
    }
  ]
}
```

Respuesta batch:

```json
{
  "operationId": null,
  "total": 25,
  "successCount": 24,
  "failedCount": 1,
  "results": [
    {
      "clientReference": "row-1",
      "status": "SUCCESS",
      "studentId": "uuid",
      "assignmentId": null,
      "errorCode": null,
      "message": null
    },
    {
      "clientReference": "row-25",
      "status": "FAILED",
      "studentId": null,
      "assignmentId": null,
      "errorCode": "INVALID_REQUEST",
      "message": "firstName is required."
    }
  ]
}
```

Un alumno invalido no cancela los demas.

## Assignments

- `GET /api/classrooms/{id}/assignments?current=true|false`
- `POST /api/assignments`
- `POST /api/assignments/batch`
- `POST /api/assignments/{id}/close`

Create:

```json
{
  "studentId": "uuid",
  "deviceId": "uuid"
}
```

Reglas:

- `Device != Student`.
- `DeviceAssignment` es la fuente de verdad.
- `TARGET_OCCUPIED` nunca reemplaza automaticamente el assignment actual.
- Si el alumno ya esta asignado, devuelve `STUDENT_ALREADY_ASSIGNED` o `SAME_DEVICE_ASSIGNMENT`.
- Cerrar un assignment actual requiere `expectedVersion` y conserva historial.

Batch:

```json
{
  "classroomId": "uuid",
  "assignments": [
    {
      "clientReference": "row-1",
      "studentId": "uuid",
      "deviceId": "uuid"
    }
  ]
}
```

El batch hace preflight de todo el lote antes de escribir y detecta conflictos internos, por ejemplo dos filas al mismo device. Los exitos se escriben en una transaccion junto con una `batch_operation` de tipo `ASSIGN_STUDENT`.

## Applications

- `GET /api/applications`
- `GET /api/classrooms/{id}/applications`

La API solo lista catalogo existente y aplicaciones autorizadas por aula. No ejecuta aplicaciones y no acepta rutas arbitrarias.

## Operations

- `GET /api/operations`
- `GET /api/operations/{id}`
- `GET /api/operations/{id}/retryable-targets`

Las operaciones devuelven resultados por target y permiten consultar fallidos retryable sin repetir targets exitosos.

## Classroom Snapshot

`GET /api/classrooms/{id}/snapshot`

Devuelve en una respuesta:

- `classroom`
- `groups`
- `students`
- `devices`
- `currentAssignments`
- `applications`
- `summary`

Para Devices registrados via `device_network_bindings`, el snapshot superpone presencia viva desde el stream autenticado en memoria. El heartbeat no escribe SQLite cada 15 segundos.

La respuesta permite construir directamente estados como:

```text
PC01 -> Juan
PC02 -> Alicia
PC03 -> libre
```

`devices[].assignedStudentId` y `devices[].assignedStudentDisplayName` derivan de `device_assignments.current = 1`, no del nombre del device ni del alumno.
