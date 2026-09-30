# Credential Vault

## Integración 20F.2

El registro, actualización y eliminación de passwords administradas requiere una sesión Vault válida. La UI inicializa/desbloquea mediante octetos y conserva el token solo en memoria React: no localStorage, sessionStorage, IndexedDB, URL, SQLite ni logs. Al eliminar una password guardada se retira la entrada Vault correspondiente después de que el Client confirme la eliminación DPAPI; el binding Galtek permanece.

20F.2 FINAL reemplaza la política previa de no-reveal humano. `SensitiveActionAuthorizationService` verifica la misma master password criptográfica sin crear/extender una sesión general y emite tokens in-memory de 60 segundos. `ADMIN_SESSION` es one-time y scoped a actor/aula/lista exacta de Devices; `CREDENTIAL_REVEAL` se limita a actor/aula/un Device y usa un snapshot fresco del Vault. El reveal es por account, octet-stream, `Cache-Control: no-store`/`Pragma: no-cache`, auditado sin secreto y nunca consulta DPAPI del Client. Desde 20F.2-F2.1, preparar el secreto no registra éxito: el audit `SUCCESS` ocurre solo después de escribir y hacer flush de la respuesta HTTP; los intentos auditados que se rechazan o fallan durante la entrega quedan como `FAILED`.

Las entries históricas pueden conservar un `loginIdentifier` con hostname anterior. Para cuentas Windows administradas, lookup/status/remove/reveal acotan primero por el prefijo de `displayName` que contiene el `deviceId` estable y después comparan case-insensitive el nombre local posterior a `\\`; el hostname no es autoridad. Cero o múltiples matches fallan cerrado. Una actualización posterior normaliza `displayName` y `loginIdentifier` al nombre observado actual.

Prompt 19A implementa el nucleo seguro local para que la profesora administradora conserve y consulte passwords escolares Windows y Google. La ampliacion vigente agrega una API HTTP minima protegida para `status`, `initialize`, `unlock` y `lock`, mas el uso administrativo de vault para provisionar `PRIMARY`, `SECONDARY` y `ADMIN` en un Client explicito sin revelar passwords.

## Alcance

- Fuente de verdad: `<CommonApplicationData>\Galtek\Classroom\Master\credential-vault.dat`.
- Usa el Master data directory y sus overrides existentes: `GALTEK_CLASSROOM_MASTER_DATA_DIR` y `galtek.classroom.master.storage.data-dir`.
- No usa `classroom.db`, migrations SQLite, BrowserProfile, environment variables, registry, logs ni browser storage para secretos.
- Tipos iniciales: `WINDOWS_ACCOUNT` y `GOOGLE_ACCOUNT`.
- No implementa clipboard, export masivo, reset destructivo, recovery de master password ni Google browser automation.
- No existe `GET /passwords`; el único reveal humano es el endpoint individual de managed account protegido por autorización sensible fresca.
- El Client credential store operativo de Prompt 19D es `managed-windows-credentials.dat` en el Agent Service y esta separado de esta boveda. Prompt 19E1 agrega `PROVISION_MANAGED_CREDENTIAL` y el metodo tipado del gateway; Prompt 19E2 conecta esta boveda al gateway mediante un bridge interno Master. Desde 2026-09-14, el endpoint administrativo de managed account credential usa ese bridge despues de upsert seguro en vault.

## Modelo

Cada entry cifrada contiene:

```text
credentialId
credentialType
displayName
loginIdentifier
password
createdAtUtc
updatedAtUtc
```

`password` admite letras, numeros, simbolos, espacios y Unicode. No se normaliza ni se cambia silenciosamente; solo se aplican limites maximos explicitos.

## Crypto

- Master password separada de Windows, Google, Commercial License, JWT, MasterWindowsBinding y pairing.
- La master password no se persiste.
- PBKDF2-HMAC-SHA256 deriva una KEK con salt aleatorio y work factor versionado.
- Setup genera un DEK aleatorio de 256 bits.
- AES-256-GCM envuelve el DEK con la KEK.
- AES-256-GCM cifra el documento completo de vault con el DEK.
- Cada escritura usa nonce aleatorio nuevo.
- Fallos de autenticidad, envelope corrupto, schema desconocido, cryptoVersion desconocida, documento invalido o IDs duplicados fallan cerrado como `CREDENTIAL_VAULT_INVALID`.

## Sesiones

- El vault queda locked en backend restart.
- `unlock(masterPassword)` valida el archivo completo, descifra el DEK y crea una sesion temporal.
- Solo hay una sesion activa por Master Backend; un nuevo unlock invalida la anterior.
- El session token es aleatorio, vive solo en memoria y no se persiste ni se registra en logs.
- Timeout default: 5 minutos de inactividad.
- La expiracion es lazy: cada operacion compara `now - lastAccess <= timeout`; no hay timer, polling ni background crypto.
- `lock(sessionToken)` invalida sesion y elimina referencias in-memory al DEK/documento.

## Operaciones

- `list(sessionToken)` devuelve metadata descifrada sin password.
- `reveal(sessionToken, credentialId)` devuelve solo el password de esa credencial.
- No existe `exportAllPasswords`, `dumpVault` ni reveal masivo.
- `add`, `update` y `remove` requieren sesion valida.
- `credentialId` lo genera el backend.
- `update` permite modificar `displayName`, `loginIdentifier` y `password`; no cambia `credentialType`.
- `changeMasterPassword(sessionToken, newMasterPassword)` re-wrappea el DEK con nuevo salt/KEK/wrappedKey, no re-encripta entries innecesariamente e invalida la sesion.

## API HTTP Minima

- `GET /api/credential-vault/status` devuelve `initialized` y `locked`.
- `POST /api/credential-vault/initialize` crea el vault; consume `application/octet-stream` con master password UTF-8 sin BOM.
- `POST /api/credential-vault/unlock` desbloquea el vault; consume `application/octet-stream` con master password UTF-8 sin BOM y devuelve `vaultSessionToken`.
- `POST /api/credential-vault/lock` invalida la sesion; recibe `X-Galtek-Vault-Session`.

Todos llaman primero a `MasterAccessGuard`. No usan `MasterUnlockAccessGuard`. Los cuerpos JSON para master password son rechazados por media type; las respuestas JSON no contienen passwords. El token de sesion es opaco, vive solo en memoria del backend y la UI no debe persistirlo en `localStorage`, `sessionStorage`, archivos, logs, URLs ni SQLite.

## Reglas De Secretos

- La UI no recibe passwords por defecto.
- La unica excepcion futura sera `REVEAL CREDENTIAL` explicito despues de `MasterAccessGuard.requireAuthorized()`, vault unlock valido y sesion no expirada.
- El secreto se entrega solo para la credencial solicitada y no se persiste en estado normal de UI.
- Passwords prohibidas en `classroom.db`, logs, BatchOperation, heartbeat, ClientHello, OperationRequest normal, BrowserProfile, Cookies, Login Data, Local State y StudentWorkspace metadata.
- Las credenciales Google no autorizan leer Chrome passwords, copiar cookies/tokens, copiar `Login Data`, copiar `Local State`, browser automation, SendKeys, auto-login ni autofill.
- Las operaciones Windows normales usan `accountId = PRIMARY/SECONDARY/ADMIN`; no envían passwords.
- La excepcion vigente para transporte de password es `PROVISION_MANAGED_CREDENTIAL`: el secreto viaja como bytes UTF-16LE sobre gRPC/mTLS y se persiste inmediatamente por DPAPI en el Client. Desde 19E2, `ManagedCredentialProvisioningBridge` puede leer internamente una entry `WINDOWS_ACCOUNT` bajo sesion de vault vigente y enviarla al gateway sin BatchOperation.
- El endpoint `PUT /api/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}/credential` no recibe credentialId. Recibe una password UTF-8 sin BOM como `application/octet-stream`, consulta el status remoto del slot, usa `windowsAccountName` como `loginIdentifier`, agrega o actualiza una entry `WINDOWS_ACCOUNT` en vault y despues invoca el bridge. Si hay mas de una entry del vault para el mismo `loginIdentifier`, falla cerrado como `CREDENTIAL_NOT_PROVISIONABLE`.
- El bridge interno exige `MasterAccessGuard.requireAuthorized()` antes de acceder al vault, no usa `MasterUnlockAccessGuard`, no recibe password ni master password, rechaza `GOOGLE_ACCOUNT`, no devuelve el secreto al caller y no registra reveal humano.
- `credentialId` y vault session token permanecen solo dentro del Master Backend; no se envian al Client ni se persisten en SQLite, BatchOperation, Protobuf, heartbeat, ClientHello, logs, exceptions ni resultados.
- La password del vault ya existe como `String` en el modelo cifrado/desbloqueado de 19A; 19E2 no redisena eso. La garantia vigente es no crear strings adicionales innecesarios, no persistir/loguear/cachear el secreto, mantener corta su vida y limpiar el `byte[]` UTF-16LE controlado en `finally`.
- El Client credential store seguro existe desde Prompt 19D, usa DPAPI bajo LocalSystem y no ofrece reveal. La boveda del Master sigue siendo la superficie humana futura para consultar passwords.

## Recovery

Archivo ausente devuelve `CREDENTIAL_VAULT_NOT_INITIALIZED`; no se crea automaticamente en startup. Si el archivo existe pero esta corrupto, no se regenera, no se sobrescribe y no se adopta parcialmente. Se preserva para recovery.

Si se pierde la master password, Galtek no provee bypass, recovery question, email recovery, default password ni hardcoded key. Un reset destructivo futuro podria borrar la boveda y empezar de nuevo, pero no existe en 19A.
