# Master API v1

Estado: implementado inicial en Prompt 10.

Esta API es local al Master Backend y existe para la futura UI React/Tauri. No ejecuta comandos remotos, no crea Devices remotamente y no mueve `StudentWorkspace` en filesystem. Las asignaciones `Student -> Device` solo modifican metadata SQLite.

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

La respuesta permite construir directamente estados como:

```text
PC01 -> Juan
PC02 -> Alicia
PC03 -> libre
```

`devices[].assignedStudentId` y `devices[].assignedStudentDisplayName` derivan de `device_assignments.current = 1`, no del nombre del device ni del alumno.
