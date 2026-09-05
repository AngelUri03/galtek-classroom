# Managed Credential Provisioning

Prompt 19E1 agrega `PROVISION_MANAGED_CREDENTIAL`, una operacion remota tipada y secret-bearing para cargar o reemplazar en el Client la password Windows almacenada por Galtek para `PRIMARY` o `SECONDARY`. Prompt 19E2 agrega el bridge interno Master Credential Vault -> gateway para usar una credencial `WINDOWS_ACCOUNT` ya almacenada, sin endpoint HTTP ni BatchOperation.

## Contrato

La operacion usa `OperationRequest` sobre el stream gRPC/mTLS existente:

```text
PROVISION_MANAGED_CREDENTIAL(
  accountId: PRIMARY | SECONDARY,
  passwordUtf16Le: bytes
)
```

`UNSPECIFIED` no es valido. El password viaja como bytes UTF-16LE sin BOM y sin terminador NUL; no se transporta como string Protobuf.

El request no transporta username, domain, SID, `accountReference`, `credentialId`, vault token, master password, profile path, command, arguments, shell, JSON, `Struct`, `Any` ni `map`.

## Transporte

La unica ruta soportada es:

```text
MasterRemoteOperationGateway
  -> gRPC + TLS/mTLS
  -> Master identity esperada
  -> trust PAIRED, no REVOKED
  -> Device registrado/correcto
  -> Agent Service
```

No hay fallback HTTP, socket plaintext, Local IPC, file share, clipboard, temp file ni Session Command. No se agrega compresion especifica para este mensaje.

## Handler Agent-Side

`ProvisionManagedCredentialOperationHandler` corre en `GaltekClassroom.Agent.Service` bajo LocalSystem. Valida el request, copia `password_utf16le` a un buffer mutable, valida no vacio, cantidad par de bytes y maximo vigente del store, lee la Installation Identity, y llama `ManagedWindowsCredentialStore.ReplaceUtf16LittleEndianAsync`.

Semantica:

- si no existia entry, la crea;
- si existia entry, la reemplaza;
- no cambia la password real de Windows;
- no cambia SID, binding, username, perfil ni sesion;
- no valida la password con Windows ni provoca login.

`SUCCESS` significa solo que el secreto recibido fue protegido con DPAPI, persistido durablemente en `managed-windows-credentials.dat` y verificado por el store. No significa que Windows aceptara la password.

## Binding Y DPAPI

El Client sigue siendo autoridad del binding. Antes de guardar, el store exige binding local valido, `accountId` configurado, SID valido y SID resoluble como `SidTypeUser`. Los errores estructurados preservan `ACCOUNT_NOT_CONFIGURED`, `ACCOUNT_NOT_FOUND`, `MANAGED_ACCOUNT_BINDINGS_INVALID`, `MANAGED_CREDENTIAL_STORE_INVALID` y `MANAGED_CREDENTIAL_PROTECTION_FAILED`.

La persistencia reutiliza `ManagedWindowsCredentialStore` y `WindowsDpapiManagedWindowsCredentialProtector`: DPAPI user scope del proceso LocalSystem, `CRYPTPROTECT_UI_FORBIDDEN`, optional entropy por instalacion/slot y SID dentro del payload protegido.

## Secret-Safe Dedupe

Para esta operacion, el dispatcher nunca conserva password raw, hash, fingerprint del password, protobuf secreto completo ni serializacion del request secreto en el cache de dedupe.

La firma retenida usa solo metadata:

```text
operationId
operationType
targetDeviceId
protocolVersion
accountId
```

Mismo `operationId + accountId` devuelve el `OperationResult` cacheado y no reaplica el secreto, aunque el duplicado traiga bytes distintos. Mismo `operationId` con otro `accountId` conserva el conflicto vigente y no ejecuta la segunda operacion.

## Memoria Y Logs

Galtek limpia los buffers mutables que controla con `CryptographicOperations.ZeroMemory` en success y failure. Protobuf/gRPC y los runtimes administrados pueden crear buffers internos que Galtek no puede zeroizar directamente; la garantia es no persistir plaintext, no loguear secretos, no cachear request secreto completo, no guardar hashes de password y minimizar la vida de las copias controladas.

Los logs permitidos no incluyen password, bytes, SID, username, `accountReference`, protectedData ni protobuf `ToString()`.

## Bridge Interno 19E2

`ManagedCredentialProvisioningBridge` vive solo en el Master Backend como primitive interna. Su entrada acepta unicamente:

```text
vaultSessionToken
credentialId
deviceId
operationId
accountId: PRIMARY | SECONDARY
```

El caller no entrega password, master password, username, SID, domain ni `accountReference`. El bridge ejecuta `MasterAccessGuard.requireAuthorized()` antes de tocar Credential Vault; `MasterUnlockAccessGuard` no participa. Primero valida sesion de vault y metadata, exige `WINDOWS_ACCOUNT`, rechaza `GOOGLE_ACCOUNT` como `CREDENTIAL_NOT_PROVISIONABLE`, comprueba una conexion registrada online con capability `MANAGED_CREDENTIAL_PROVISIONING_V1`, lee internamente la password, la codifica con `StandardCharsets.UTF_16LE` sin BOM/NUL, llama exclusivamente a `MasterRemoteOperationGateway.provisionManagedCredential(...)` y limpia el `byte[]` en `finally`.

`credentialId` y vault session token nunca salen del Master Backend. El bridge no crea BatchOperation, no escribe SQLite, no agrega Protobuf, no expone HTTP, no hace fanout, no reintenta automaticamente y propaga el resultado tipado vigente del gateway, incluido `OPERATION_RESULT_UNKNOWN`.

La password del Credential Vault sigue existiendo como `String` por el modelo 19A; Java `String` no puede zeroizarse de forma fiable. La garantia de 19E2 es no crear strings adicionales innecesarios, no persistir/loguear/cachear el secreto, mantener corta su vida de uso y limpiar la copia `byte[]` controlada.

## Limites 19E1

19E1 no agrega Credential Vault bridge. 19E2 agrega solo el bridge interno Master. Sigue sin haber endpoint HTTP Master, BatchOperation, SQLite migration, retry automatico, reconciliation, status query nuevo, Local IPC credential op, Session Agent password handling, login, logoff, switch, Credential Provider, password verification, Windows password change, UI, clipboard ni reveal.

Si el Master envia la operacion y no recibe `OperationResult`, el resultado correcto sigue siendo `OPERATION_RESULT_UNKNOWN`; no se infiere exito ni se reintenta automaticamente.

## Pendiente

- 19F: `LOGOFF_WINDOWS_SESSION`.
- 19G: `LOGON_MANAGED_ACCOUNT` / `SWITCH_MANAGED_ACCOUNT`.
- 19H: dispatch batch Master, planner y superficie administrativa.
