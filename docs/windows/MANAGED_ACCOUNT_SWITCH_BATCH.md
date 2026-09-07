# Managed Account Switch Batch

Prompt 19H1 implementa en el Master Backend el batch administrativo para dejar Devices explicitamente seleccionados en `PRIMARY` o `SECONDARY`. Prompt 19H2 agrega retry administrativo explicito y selectivo sobre la misma `BatchOperation`.

## Endpoint

```text
POST /api/classrooms/{classroomId}/managed-accounts/switch
```

Request estricta:

```json
{
  "targetAccountId": "PRIMARY",
  "targetDeviceIds": ["device-1", "device-2"]
}
```

Solo se aceptan `PRIMARY` y `SECONDARY`. `targetDeviceIds` es obligatorio, no vacio, sin blanks, sin duplicados y explicito: no hay `allDevices`, `groupId`, seleccion por alumno ni fanout implicito de aula.

## Retry Endpoint

```text
POST /api/operations/{operationId}/retry
```

Request estricta:

```json
{
  "targetDeviceIds": ["device-2"]
}
```

El retry solo existe para operaciones persistidas `SWITCH_MANAGED_ACCOUNT`. La request acepta solo `targetDeviceIds`, obligatorio, no vacio, sin blanks, sin duplicados y maximo 100. No acepta `targetAccountId`, source account, password, username, SID, sessionId, credentialId, vault token, `force`, `allFailed`, timeout, comandos, payload generico, grupo, alumno ni all classroom.

## Seguridad

El endpoint de switch y el endpoint de retry usan `MasterAccessGuard.requireAuthorized()` antes de leer Classroom, Devices, assignments, bindings, trust, presencia o SQLite escolar. No usan `MasterUnlockAccessGuard`.

No recibe ni persiste password, username, SID, sessionId, credentialId, vault session token, source account, force, timeout, command, args ni payload generico.

## Persistencia

Cada request crea una sola `BatchOperation`:

```text
operationType = SWITCH_MANAGED_ACCOUNT
```

La operacion y sus targets se persisten antes del primer `GET_WINDOWS_SESSION_STATE`. El payload JSON solo contiene metadata no secreta:

```json
{
  "schemaVersion": 1,
  "targetAccountId": "PRIMARY"
}
```

`NO_CHANGE` es resultado final persistido por target, cuenta como exito y no es retryable.

El retry 19H2 conserva `operationId`, `createdAtUtc`, `requestedBy`, `targetCount` y payload original. `targetAccountId` se lee solo desde ese payload durable `schemaVersion = 1`; la request de retry no puede cambiarlo. Antes de llamar a ningun Client, los targets seleccionados se reclaman en una transaccion `FAILED -> PENDING` con `attempt + 1`; si version/estado/error/attempt ya no coinciden, el retry completo falla con `CONCURRENT_MODIFICATION` y no hay trabajo remoto.

## Preflight Local

Cada target se valida de forma independiente:

- Device existe y pertenece al aula.
- Binding de red vigente.
- Trust `PAIRED`.
- Trust no `REVOKED`.
- Conexion gRPC/mTLS autenticada `ONLINE`.
- Capability `WINDOWS_SESSION_STATE_V1`.
- Capability `WINDOWS_SESSION_SWITCH_V1`.

`SESSION_AGENT_AVAILABLE` no se exige en el Master. La disponibilidad real del Agent, Credential Provider, bindings y credenciales la decide el Client durante `SWITCH_MANAGED_ACCOUNT`.

El retry repite este preflight local fresco para cada target reclamado. No reutiliza readiness anterior ni convierte `SESSION_AGENT_AVAILABLE` en requisito.

## Snapshot Y Planner

Cada target listo obtiene `GET_WINDOWS_SESSION_STATE` con operationId remoto propio. El snapshot no es lock.

El retry tambien obtiene snapshot fresco con operationId remoto nuevo. Si el target ya esta activo al reintentar, queda `NO_CHANGE` con el attempt incrementado y no se envia mutation.

Matriz:

- Target ya activo -> `NO_CHANGE`.
- `NO_SESSION` -> plan conceptual `LOGON`.
- Opposite managed activo -> plan conceptual `SWITCH`.
- `OTHER_SESSION_ACTIVE` -> `FAILED/WINDOWS_SESSION_CHANGED`, sin mutation.
- `UNKNOWN` -> `FAILED/WINDOWS_SESSION_UNKNOWN`, sin mutation.
- Snapshot fallido o incierto -> `FAILED` con error estructurado, sin mutation.

Para cualquier plan mutating, el Master envia siempre:

```text
SWITCH_MANAGED_ACCOUNT(targetAccountId)
```

Nunca envia `LOGON_MANAGED_ACCOUNT` directamente ni encadena `LOGOFF_WINDOWS_SESSION + LOGON_MANAGED_ACCOUNT`.

En retry, el Master vuelve a enviar solo `SWITCH_MANAGED_ACCOUNT(targetAccountId original)` con operationId remoto nuevo cuando el snapshot permite mutation.

## Resultados

El batch agrega resultados asi:

- Todos `SUCCESS` o `NO_CHANGE` -> `SUCCESS`.
- Mezcla de `SUCCESS`/`NO_CHANGE` y `FAILED` -> `PARTIAL_SUCCESS`.
- Todos `FAILED` -> `FAILED`.

`GET /api/operations/{id}` lee los resultados durables. `GET /api/operations/{id}/retryable-targets` excluye siempre `SUCCESS`, `NO_CHANGE`, `PENDING` y `OPERATION_RESULT_UNKNOWN` de switch, e incluye solo `FAILED` con `ErrorCode.retryable()` seguro para esta operacion.

## Elegibilidad De Retry

Un target es elegible para retry solo si:

- pertenece al batch original;
- su resultado persistido actual es `FAILED`;
- tiene `errorCode` no nulo;
- `ErrorCode.retryable() == true`;
- el error no es `OPERATION_RESULT_UNKNOWN`.

Si cualquier target solicitado no cumple, se rechaza toda la request antes del claim y antes de hacer trabajo remoto. No hay partial retry de una request malformada.

## Agregacion Tras Retry

El retry actualiza solo targets seleccionados y recalcula `summary` y `status` sobre el batch completo. Los targets no seleccionados conservan resultado e intento. Una mezcla final de exitos y fallos sigue produciendo `PARTIAL_SUCCESS`; todos `SUCCESS`/`NO_CHANGE` producen `SUCCESS`; todos fallidos producen `FAILED`; targets `PENDING` restantes mantienen el batch en `RUNNING`.

## Fuera De Alcance

No hay retry automatico, scheduler, polling, startup auto-retry de `PENDING`, UI, Agent changes, Protobuf changes, C++ changes, credential provisioning, vault unlock, switch por assignment/startup, group fanout implicito, all classroom implicito ni reconciliation de `SWITCH_MANAGED_ACCOUNT`.
