# Master API v1

Estado: implementado inicial en Prompt 10; ampliado en Prompt 14 con Clients de red y registro de Devices; ampliado en Prompt 15B con dispatch batch de power control; ampliado en Prompt 15C con reconciliacion segura de power control incierto; ampliado en Prompt 16C con administracion persistente de politicas de navegacion web; ampliado en Prompt 16E1 con administracion persistente de politicas de descarga de navegador; ampliado en Prompt 16F1 con dispatch batch Master de aplicacion de policies de navegacion y descarga; ampliado en Prompt 16F2 con dispatch batch Master de `OPEN_URL`.

Esta API es local al Master Backend y existe para la futura UI React/Tauri. No ejecuta comandos remotos arbitrarios y no mueve `StudentWorkspace` en filesystem. Prompt 14 permite registrar como `Device` persistente a un Client ya paired; el Master genera el `deviceId` y vincula el Device con la Network Identity en SQLite. Prompt 15B permite enviar solo `SHUTDOWN`/`RESTART` tipados por el framework gRPC seguro. Prompt 16F1 permite aplicar policies de navegacion y descarga desde la fuente de verdad persistida del Master hacia Agents con handlers ya existentes. Prompt 16F2 permite enviar `OPEN_URL` batch con `OpenUrlOperationParameters.url`, evaluando safety global y policy efectiva por target. Los apply endpoints no aceptan URLs/commands, Java no escribe registry y 16F2 no modifica Agent/Protobuf/Registry/Session. Las asignaciones `Student -> Device` solo modifican metadata SQLite.

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

## Open URL

`POST /api/classrooms/{classroomId}/open-url`

Endpoint protegido por `MasterAccessGuard`. Ejecuta una sola operacion batch remota tipada `OPEN_URL` sobre Devices explicitos del aula.

Request:

```json
{
  "url": "https://example.com/material",
  "targetDeviceIds": [
    "device-1",
    "device-2"
  ]
}
```

Reglas de request:

- Solo se aceptan `url` y `targetDeviceIds`; cualquier campo adicional produce `400 INVALID_REQUEST`.
- `url` es obligatorio, no blank, debe respetar el limite vigente de `OPEN_URL` y debe pasar safety estructural antes de leer aula, Devices, assignments o policies.
- `targetDeviceIds` es obligatorio, no puede estar vacio, no acepta strings en blanco ni duplicados.
- No se aceptan `browser`, `executablePath`, `browserProfileId`, `profile`, `accountType`, `groupId`, `policyId`, rules, comandos, argumentos, shell, force, timeout, `newTab`, `incognito`, SID, registry paths ni payload libre.

Safety global:

- La URL debe ser absoluta, `http`/`https`, con host obligatorio, sin userinfo, sin controles/CR/LF, sin rutas locales/UNC, sin URL relativa y sin esquemas `file:`, `javascript:` o `data:`.
- Si falla safety estructural, se rechaza toda la request con `INVALID_URL` y no se crea `BatchOperation`.
- La normalizacion se usa para comparar policy, pero el Agent recibe la URL original aceptada, conservando query y fragment visible.

Policy por target:

- El Master resuelve una sola policy efectiva por Device con `classroomId`, `deviceId`, `groupId` derivado del assignment actual `Device -> Student -> Student.groupId` y `accountType = null`.
- `accountType = null` significa que solo participan policies `ANY`; `PRIMARY`/`SECONDARY` no se infieren ni se aceptan desde HTTP.
- Si no hay policy efectiva, aplica `UNRESTRICTED` implicito.
- `BrowserNavigationPolicyEvaluator` decide `ALLOW` o `BLOCK` usando la semantica vigente de matching. `EXACT_URL` si se evalua para `OPEN_URL` concreto; no produce `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE`.
- Un target bloqueado por policy queda `FAILED URL_BLOCKED_BY_POLICY` y no se envia nada a su Agent.

Preflight y dispatch:

- Para targets permitidos por policy, el Device debe pertenecer al aula, existir, tener binding vigente, trust `PAIRED`, no estar `REVOKED`, tener conexion gRPC/mTLS autenticada `ONLINE` y anunciar `OPEN_URL_V1`.
- No se exige Chrome, Edge, browser instalado ni `SESSION_AGENT_AVAILABLE` como sustituto de disponibilidad runtime.
- El Master congela todos los planes antes del fanout, persiste una unica `BatchOperation` `OPEN_URL` antes del primer send y usa el mismo `operationId` para todos los Agents.
- El gateway envia `OperationType.OPEN_URL` con `OpenUrlOperationParameters { url = originalValidatedUrl }`; no usa payload JSON generico ni transporte nuevo.

Semantica de resultados:

- `SUCCESS` significa solo que Windows, en la sesion interactiva del Client, acepto abrir la URL mediante el handler HTTP/HTTPS registrado. No implica Internet, DNS, HTTP 200, carga completa, render, video iniciado, alumno viendo la pagina, Chrome ni Edge.
- `OperationAccepted ACCEPTED` no marca `SUCCESS`; solo `OperationResult SUCCESS`.
- Si el Master envio `OperationRequest` pero no obtuvo `OperationResult`, el target queda `OPERATION_RESULT_UNKNOWN`.
- Si el Agent envio el Session Command pero perdio/no confirmo respuesta del Session Agent, queda `SESSION_COMMAND_RESULT_UNKNOWN`.
- No hay retry automatico ni reconciliacion para `OPEN_URL`, para evitar duplicar pestanas o ventanas.
- La defensa en profundidad se conserva: el Agent vuelve a validar safety y puede devolver `URL_BLOCKED_BY_POLICY` si su policy local aplicada bloquea la URL.

Respuesta: misma forma de batch que `power-control`, con `type = OPEN_URL`.

Errores relevantes:

- `400 INVALID_REQUEST`: body faltante, `url` blank, targets vacios/duplicados o campos no soportados.
- `400 INVALID_URL`: safety estructural global falla.
- `403 <authorization.status>`: Master local no autorizado.
- `404 CLASSROOM_NOT_FOUND`: aula inexistente.
- Target `DEVICE_NOT_FOUND`, `DEVICE_NOT_REGISTERED`, `MASTER_NOT_PAIRED`, `CLIENT_REVOKED`, `DEVICE_OFFLINE`, `CAPABILITY_NOT_SUPPORTED`, `URL_BLOCKED_BY_POLICY`, `INVALID_URL`, `SESSION_AGENT_UNAVAILABLE`, `SESSION_COMMAND_RESULT_UNKNOWN`, `OPERATION_REJECTED` u `OPERATION_RESULT_UNKNOWN`.

## Reconcile Power Operation

`POST /api/operations/{operationId}/reconcile`

Endpoint administrativo protegido por `MasterAccessGuard`. No acepta body y solo actua sobre operaciones existentes `SHUTDOWN` o `RESTART`.

Semantica:

- Consulta solo targets `FAILED` con `errorCode = OPERATION_RESULT_UNKNOWN`.
- Consulta solo Devices actualmente `ONLINE` mediante el stream gRPC/mTLS autenticado existente.
- Envia `OperationStatusQuery` read-only con el mismo `operationId` y `targetDeviceId`; no reenvia `OperationRequest`.
- Si el Agent responde `KNOWN`, actualiza el target del `BatchOperation` existente y recalcula `SUCCESS`, `PARTIAL_SUCCESS` o `FAILED`.
- Si el Agent responde `UNKNOWN`, no responde o el Device esta offline, conserva `OPERATION_RESULT_UNKNOWN`.
- Nunca marca `SUCCESS` solo porque un Device quede `OFFLINE` ni por reconexion posterior.

Respuesta: `OperationResponse` equivalente a `GET /api/operations/{operationId}`.

Errores relevantes:

- `400 INVALID_REQUEST`: body no soportado o la operacion no es `SHUTDOWN`/`RESTART`.
- `403 <authorization.status>`: Master local no autorizado.
- `404 OPERATION_NOT_FOUND`: operacion inexistente.
- `503 MASTER_DATABASE_UNAVAILABLE` u otro codigo de storage: SQLite no esta disponible.

## Browser Policies

- `GET /api/classrooms/{classroomId}/browser-policies?active=true|false`
- `POST /api/classrooms/{classroomId}/browser-policies`
- `PATCH /api/browser-policies/{policyId}`
- `POST /api/browser-policies/{policyId}/archive`
- `GET /api/browser-policies/{policyId}/rules`
- `POST /api/browser-policies/{policyId}/rules`
- `PATCH /api/browser-url-rules/{ruleId}`
- `POST /api/browser-url-rules/{ruleId}/archive`
- `GET /api/classrooms/{classroomId}/browser-policies/effective?deviceId=&groupId=&accountType=`
- `POST /api/classrooms/{classroomId}/browser-policies/apply`

Todos estos endpoints estan protegidos por `MasterAccessGuard`. Los endpoints CRUD/effective administran o leen la fuente de verdad del Master; no escriben registry, no crean extension, no modifican Chrome/Edge y no crean proxy/DNS/firewall. El endpoint `apply` de 16F1 envia una operacion remota tipada al Agent usando el transporte gRPC/mTLS existente y los parametros Protobuf de `APPLY_BROWSER_NAVIGATION_POLICY`; no acepta payload libre ni registry paths.

Create policy:

```json
{
  "name": "Aula primaria primary",
  "mode": "ALLOWLIST",
  "scopeType": "CLASSROOM",
  "schoolGroupId": null,
  "deviceId": null,
  "accountScope": "PRIMARY"
}
```

Modes:

```text
UNRESTRICTED
BLOCKLIST
ALLOWLIST
```

Scopes:

```text
CLASSROOM -> sin schoolGroupId ni deviceId
GROUP     -> schoolGroupId obligatorio y del aula
DEVICE    -> deviceId obligatorio y del aula
```

Account scopes:

```text
ANY
PRIMARY
SECONDARY
```

Patch/archive policy requieren `expectedVersion`.

Create rule:

```json
{
  "action": "ALLOW",
  "matchType": "EXACT_URL",
  "pattern": "https://www.youtube.com/watch?v=ABC123",
  "enabled": true,
  "description": "Video de clase"
}
```

Rule actions:

```text
ALLOW
BLOCK
```

Match types:

```text
HOST_EXACT
HOST_SUFFIX
URL_PREFIX
EXACT_URL
```

Reglas:

- No se aceptan regex arbitrarias, JavaScript regex ni wildcards libres.
- `HOST_SUFFIX` respeta frontera DNS: `youtube.com` coincide con `www.youtube.com`, no con `evilyoutube.com`.
- `URL_PREFIX` y `EXACT_URL` deben ser URLs absolutas seguras `http`/`https`.
- Fragment se elimina para matching; query se conserva en `EXACT_URL`.
- `URL_PREFIX` no acepta query como mecanismo de prefix.

Effective policy:

```json
{
  "policy": {
    "policyId": "uuid",
    "classroomId": "uuid",
    "name": "PC07 primary",
    "mode": "UNRESTRICTED",
    "scopeType": "DEVICE",
    "schoolGroupId": null,
    "deviceId": "device-07",
    "accountScope": "PRIMARY",
    "active": true,
    "version": 0
  },
  "mode": "UNRESTRICTED",
  "implicit": false
}
```

Si no hay policy aplicable:

```json
{
  "policy": null,
  "mode": "UNRESTRICTED",
  "implicit": true
}
```

Precedencia:

```text
1. DEVICE + cuenta especifica
2. DEVICE + ANY
3. GROUP + cuenta especifica
4. GROUP + ANY
5. CLASSROOM + cuenta especifica
6. CLASSROOM + ANY
7. ninguna policy -> UNRESTRICTED implicito
```

Si `accountType` falta, solo aplican policies `ANY`; no se infiere `PRIMARY`.

Apply navigation policy:

```json
{
  "targetDeviceIds": [
    "device-1",
    "device-2"
  ]
}
```

Reglas de request:

- `targetDeviceIds` es obligatorio, no puede estar vacio, no acepta strings en blanco ni duplicados.
- No se aceptan campos adicionales: `policyId`, `rules`, `accountType`, `url`, `browser`, comandos, registry paths, timeouts ni payload JSON libre.
- El Master resuelve la policy efectiva desde SQLite para cada target usando `classroomId`, `deviceId`, group derivado del assignment actual `Student -> SchoolGroup` y `accountType = null`.
- En 16F1 `accountType = null` significa `ANY` solamente; `PRIMARY`/`SECONDARY` no se infieren ni se aplican por dispatch Master.
- Todos los parametros tipados se construyen y congelan antes del fanout; el mismo `operationId` se usa para todos los targets.

Preflight y dispatch:

- El Device debe pertenecer al aula solicitada.
- El Device debe tener binding vigente con Network Identity.
- El trust debe estar `PAIRED` y no `REVOKED`.
- Debe existir conexion gRPC/mTLS autenticada `ONLINE`.
- El Client debe anunciar `BROWSER_NAVIGATION_POLICY_V1`.
- Si la policy efectiva contiene una rule `EXACT_URL` habilitada, ese target queda `FAILED` con `BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE` y no se envia nada al Agent.
- Los targets fallidos de preflight no cancelan los targets listos.
- El Master persiste una unica `BatchOperation` antes de enviar.
- Si una request ya enviada queda sin `OperationResult` por timeout o desconexion, el target queda `FAILED` con `OPERATION_RESULT_UNKNOWN`.

Respuesta: misma forma de batch que `power-control`, con `type = APPLY_BROWSER_NAVIGATION_POLICY`.

Errores relevantes:

- `400 BROWSER_POLICY_SCOPE_INVALID`: scope inconsistente o group/device de otra aula.
- `400 BROWSER_POLICY_RULE_INVALID`: pattern invalido para el match type.
- `404 BROWSER_POLICY_NOT_FOUND`: policy o rule inexistente/archivada.
- `409 BROWSER_POLICY_CONFLICT`: ya existe una policy activa para la misma combinacion target/account.
- `409 CONCURRENT_MODIFICATION`: `expectedVersion` obsoleto.

Separacion de seguridad:

- Safety estructural de URL decide si una URL es tecnicamente procesable.
- Browser policy decide si la navegacion esta administrativamente permitida.
- Una policy `ALLOW` nunca puede saltarse safety; `file:`, `javascript:`, `data:` y esquemas inseguros siguen rechazados.

## Browser Download Policies

- `GET /api/classrooms/{classroomId}/browser-download-policies?active=true|false`
- `POST /api/classrooms/{classroomId}/browser-download-policies`
- `PATCH /api/browser-download-policies/{policyId}`
- `POST /api/browser-download-policies/{policyId}/archive`
- `GET /api/classrooms/{classroomId}/browser-download-policies/effective?deviceId=&groupId=&accountType=`
- `POST /api/classrooms/{classroomId}/browser-download-policies/apply`

Todos estos endpoints estan protegidos por `MasterAccessGuard`. Los endpoints CRUD/effective administran o leen la fuente de verdad del Master para descarga de navegador; no escriben registry, no aplican `DownloadRestrictions`, no crean extension y no monitorean descargas. El endpoint `apply` de 16F1 envia una operacion remota tipada al Agent usando el transporte gRPC/mTLS existente y los parametros Protobuf de `APPLY_BROWSER_DOWNLOAD_POLICY`; no acepta payload libre ni registry paths.

Create policy:

```json
{
  "name": "Primaria sin descargas",
  "restrictionMode": "BLOCK_ALL",
  "scopeType": "CLASSROOM",
  "schoolGroupId": null,
  "deviceId": null,
  "accountScope": "PRIMARY"
}
```

Restriction modes:

```text
NO_SPECIAL_RESTRICTIONS
BLOCK_DANGEROUS
BLOCK_POTENTIALLY_DANGEROUS
BLOCK_ALL
BLOCK_MALICIOUS
```

La API expone el enum Galtek. El mapping nativo de `DownloadRestrictions` 0-4 pertenece al Agent y es aplicado por el enforcement implementado desde 16E2B; los valores nativos no forman parte de la autoridad de la API.

Scopes y account scopes son iguales a navegacion:

```text
CLASSROOM -> sin schoolGroupId ni deviceId
GROUP     -> schoolGroupId obligatorio y del aula
DEVICE    -> deviceId obligatorio y del aula

ANY
PRIMARY
SECONDARY
```

Patch/archive requieren `expectedVersion`. Patch puede modificar `name`, `restrictionMode`, `scopeType`, `schoolGroupId`, `deviceId` y `accountScope`, con las mismas invariantes de scope que create.

Effective policy:

```json
{
  "policy": {
    "policyId": "uuid",
    "classroomId": "uuid",
    "name": "PC07 primary",
    "restrictionMode": "BLOCK_ALL",
    "scopeType": "DEVICE",
    "schoolGroupId": null,
    "deviceId": "device-07",
    "accountScope": "PRIMARY",
    "active": true,
    "version": 0
  },
  "restrictionMode": "BLOCK_ALL",
  "implicit": false
}
```

Si no hay policy aplicable:

```json
{
  "policy": null,
  "restrictionMode": "NO_SPECIAL_RESTRICTIONS",
  "implicit": true
}
```

Precedencia:

```text
1. DEVICE + cuenta especifica
2. DEVICE + ANY
3. GROUP + cuenta especifica
4. GROUP + ANY
5. CLASSROOM + cuenta especifica
6. CLASSROOM + ANY
7. ninguna policy -> NO_SPECIAL_RESTRICTIONS implicito
```

Si `accountType` falta, solo aplican policies `ANY`; no se infiere `PRIMARY`.

No se aceptan campos como `registryValue`, `nativeValue`, `downloadPath`, `extension`, `mimeType`, `command`, `script`, `browserExecutable` o `windowsSid`.

Apply download policy:

```json
{
  "targetDeviceIds": [
    "device-1",
    "device-2"
  ]
}
```

Reglas de request:

- `targetDeviceIds` es obligatorio, no puede estar vacio, no acepta strings en blanco ni duplicados.
- No se aceptan campos adicionales: `policyId`, `restrictionMode`, `accountType`, `browser`, comandos, registry paths, timeouts ni payload JSON libre.
- El Master resuelve la policy efectiva desde SQLite para cada target usando `classroomId`, `deviceId`, group derivado del assignment actual `Student -> SchoolGroup` y `accountType = null`.
- En 16F1 `accountType = null` significa `ANY` solamente; `PRIMARY`/`SECONDARY` no se infieren ni se aplican por dispatch Master.
- Todos los parametros tipados se construyen y congelan antes del fanout.
- Ausencia de policy efectiva se envia como `implicit_no_special_restrictions = true` y `restriction_mode = NO_SPECIAL_RESTRICTIONS`.
- Una policy explicita `NO_SPECIAL_RESTRICTIONS` se envia como explicita, no como removal implicito.

Preflight y dispatch:

- El Device debe pertenecer al aula solicitada.
- El Device debe tener binding vigente con Network Identity.
- El trust debe estar `PAIRED` y no `REVOKED`.
- Debe existir conexion gRPC/mTLS autenticada `ONLINE`.
- El Client debe anunciar `BROWSER_DOWNLOAD_POLICY_V1`.
- Los targets fallidos de preflight no cancelan los targets listos.
- El Master persiste una unica `BatchOperation` antes de enviar.
- El mismo `operationId` se usa para todos los targets.
- Si una request ya enviada queda sin `OperationResult` por timeout o desconexion, el target queda `FAILED` con `OPERATION_RESULT_UNKNOWN`.

Respuesta: misma forma de batch que `power-control`, con `type = APPLY_BROWSER_DOWNLOAD_POLICY`.

Errores relevantes:

- `400 BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID`: scope inconsistente o group/device de otra aula.
- `404 BROWSER_DOWNLOAD_POLICY_NOT_FOUND`: policy inexistente/archivada.
- `409 BROWSER_DOWNLOAD_POLICY_CONFLICT`: ya existe una policy activa para la misma combinacion target/account.
- `409 CONCURRENT_MODIFICATION`: `expectedVersion` obsoleto.

Limitacion Windows: no se modelan `blockedExtensions`, `allowedExtensions`, `blockedMimeTypes` ni `allowedMimeTypes`, porque Galtek no puede garantizar bloqueo arbitrario equivalente en Chrome y Edge sobre Windows en esta fase.

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
- `POST /api/operations/{id}/reconcile`

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
