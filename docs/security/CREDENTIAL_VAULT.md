# Credential Vault

Prompt 19A implementa el nucleo seguro local para que la profesora administradora conserve y consulte passwords escolares Windows y Google.

## Alcance

- Fuente de verdad: `<CommonApplicationData>\Galtek\Classroom\Master\credential-vault.dat`.
- Usa el Master data directory y sus overrides existentes: `GALTEK_CLASSROOM_MASTER_DATA_DIR` y `galtek.classroom.master.storage.data-dir`.
- No usa `classroom.db`, migrations SQLite, BrowserProfile, environment variables, registry, logs ni browser storage para secretos.
- Tipos iniciales: `WINDOWS_ACCOUNT` y `GOOGLE_ACCOUNT`.
- No implementa UI, Tauri/React, clipboard, HTTP reveal endpoint, Protobuf, gRPC, provisioning remoto, login Windows ni Google browser automation.
- El Client credential store operativo de Prompt 19D es `managed-windows-credentials.dat` en el Agent Service y esta separado de esta boveda.

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

## Reglas De Secretos

- La UI no recibe passwords por defecto.
- La unica excepcion futura sera `REVEAL CREDENTIAL` explicito despues de `MasterAccessGuard.requireAuthorized()`, vault unlock valido y sesion no expirada.
- El secreto se entrega solo para la credencial solicitada y no se persiste en estado normal de UI.
- Passwords prohibidas en `classroom.db`, logs, BatchOperation, heartbeat, ClientHello, OperationRequest normal, BrowserProfile, Cookies, Login Data, Local State y StudentWorkspace metadata.
- Las credenciales Google no autorizan leer Chrome passwords, copiar cookies/tokens, copiar `Login Data`, copiar `Local State`, browser automation, SendKeys, auto-login ni autofill.
- Las operaciones Windows normales futuras siguen usando `accountId = PRIMARY/SECONDARY`; no envian passwords.
- El Client credential store seguro existe desde Prompt 19D, usa DPAPI bajo LocalSystem y no ofrece reveal. La boveda del Master sigue siendo la superficie humana futura para consultar passwords.

## Recovery

Archivo ausente devuelve `CREDENTIAL_VAULT_NOT_INITIALIZED`; no se crea automaticamente en startup. Si el archivo existe pero esta corrupto, no se regenera, no se sobrescribe y no se adopta parcialmente. Se preserva para recovery.

Si se pierde la master password, Galtek no provee bypass, recovery question, email recovery, default password ni hardcoded key. Un reset destructivo futuro podria borrar la boveda y empezar de nuevo, pero no existe en 19A.
