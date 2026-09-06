# Managed Account Switch Batch

Prompt 19H1 implementa en el Master Backend el batch administrativo para dejar Devices explicitamente seleccionados en `PRIMARY` o `SECONDARY`.

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

## Seguridad

El endpoint usa `MasterAccessGuard.requireAuthorized()` antes de leer Classroom, Devices, assignments, bindings, trust, presencia o SQLite escolar. No usa `MasterUnlockAccessGuard`.

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

## Snapshot Y Planner

Cada target listo obtiene `GET_WINDOWS_SESSION_STATE` con operationId remoto propio. El snapshot no es lock.

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

## Resultados

El batch agrega resultados asi:

- Todos `SUCCESS` o `NO_CHANGE` -> `SUCCESS`.
- Mezcla de `SUCCESS`/`NO_CHANGE` y `FAILED` -> `PARTIAL_SUCCESS`.
- Todos `FAILED` -> `FAILED`.

`GET /api/operations/{id}` lee los resultados durables. `GET /api/operations/{id}/retryable-targets` excluye siempre `SUCCESS` y `NO_CHANGE`, e incluye solo `FAILED` con `ErrorCode.retryable()`.

## Fuera De Alcance 19H1

No hay retry automatico, retry endpoint, UI, Agent changes, Protobuf changes, C++ changes, credential provisioning, vault unlock, switch por assignment/startup, group fanout implicito, all classroom implicito ni reconciliation de `SWITCH_MANAGED_ACCOUNT`.
