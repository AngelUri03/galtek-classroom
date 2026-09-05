# Managed Windows Credentials

Prompt 19E1 agrega provisioning remoto seguro para este store mediante `PROVISION_MANAGED_CREDENTIAL`. La operacion recibe solo `PRIMARY`/`SECONDARY` y `password_utf16le` como bytes UTF-16LE sobre gRPC/mTLS autenticado, valida el binding local y persiste inmediatamente por DPAPI. Prompt 19E2 agrega el bridge interno Master Credential Vault -> gateway para tomar una credencial `WINDOWS_ACCOUNT` ya almacenada y provisionarla en un Client explicito. Prompt 19G2 agrega el unico reveal productivo local permitido: one-time acquisition desde Agent Service hacia Galtek Credential Provider validado por `GaltekClassroom.CredentialProvider.v1`, sin JSON/Base64/string de password y sin Master remoto todavia.

Prompt 19D agrega el almacenamiento local seguro del Client para las passwords Windows de los slots administrados:

```text
PRIMARY
SECONDARY
```

## Fuente De Verdad

El archivo es:

```text
<CommonApplicationData>\Galtek\Classroom\managed-windows-credentials.dat
```

En desarrollo y tests usa el override vigente del Agent: `GALTEK_CLASSROOM_DATA_DIR`.

Este archivo esta separado de `managed-windows-accounts.json`, Installation Identity, licencia, Master binding, Network Identity, trust stores, application bindings, browser policy state/journals y del `credential-vault.dat` del Master.

## Separacion Binding Vs Credential

`managed-windows-accounts.json` contiene el binding logico `PRIMARY`/`SECONDARY` hacia `windowsSid` y `accountReference`. No contiene passwords.

`managed-windows-credentials.dat` contiene solo un envelope tecnico con `schemaVersion`, `installationId`, `accountId`, `protectedData`, `createdAtUtc` y `updatedAtUtc`. No contiene SID, `accountReference`, username, domain, password, hash, credentialId del Master ni vault entry id fuera del ciphertext.

`credential-vault.dat` pertenece al Master y es la boveda consultable por la profesora. El Client credential store no es una superficie de reveal ni un password manager.

## Modelo

Envelope externo:

```json
{
  "schemaVersion": 1,
  "installationId": "...",
  "entries": [
    {
      "accountId": "PRIMARY",
      "protectedData": "<base64>",
      "createdAtUtc": "...",
      "updatedAtUtc": "..."
    }
  ]
}
```

El documento queda ligado al `installationId` actual. Si se copia desde otra instalacion, Galtek falla cerrado como `MANAGED_CREDENTIAL_STORE_INVALID`; no adopta ni corrige automaticamente el archivo.

## DPAPI

La implementacion productiva usa Windows DPAPI mediante `CryptProtectData`/`CryptUnprotectData`.

El scope productivo es el usuario actual del proceso Agent Service, que debe ser `LocalSystem` (`S-1-5-18`). No se usa `CRYPTPROTECT_LOCAL_MACHINE` ni `DataProtectionScope.LocalMachine`.

Protect y Unprotect verifican LocalSystem antes de llamar DPAPI. Si el ejecutable se corre accidentalmente como consola administrativa interactiva, falla cerrado; no hay fallback a LocalMachine, plaintext ni otra cuenta.

DPAPI se invoca con semantica no interactiva `CRYPTPROTECT_UI_FORBIDDEN`. No debe aparecer prompt, dialogo ni UI de Windows.

## Optional Entropy

El store deriva optional entropy deterministica de:

```text
GaltekClassroom.ManagedWindowsCredential.v1|<installationId>|<accountId>
```

No es secreta. Impide reutilizar directamente un blob protegido entre `PRIMARY` y `SECONDARY` o entre instalaciones distintas.

## Payload Protegido

Dentro del blob DPAPI vive un payload binario versionado con:

- magic/version;
- `accountId`;
- `windowsSid`;
- password en UTF-16LE.

El SID interno debe coincidir con el binding vigente en `managed-windows-accounts.json` durante `Acquire` y `GetStatus`. Si el slot se rebindea a otro SID, la credencial vieja permanece fisicamente pero deja de ser usable y `credentialConfigured=false`.

Renombrar una cuenta conserva la credencial si el SID es el mismo. Borrar la cuenta produce `ACCOUNT_NOT_FOUND` mientras el binding SID no resuelva a `SidTypeUser`.

## Operaciones Internas

El store interno expone:

```text
GetStatus
Add
Replace
AddUtf16LittleEndian
ReplaceUtf16LittleEndian
Remove
Acquire
```

No existe `Reveal`, `GetPasswordString`, `Dump`, `Export` ni CLI de password.

`Add` y `Replace` exigen:

- `accountId` exactamente `PRIMARY` o `SECONDARY`;
- binding existente y valido;
- SID del binding valido;
- SID resoluble como `SidTypeUser`.

Sin binding devuelve `ACCOUNT_NOT_CONFIGURED`. Si el SID ya no existe devuelve `ACCOUNT_NOT_FOUND`.

## Escritura Durable Y ACL

Las escrituras serializan solo el envelope cifrado y usan `DurableFileWriter`: temp file en el mismo directorio, flush/fsync, move/replace atomico y verificacion posterior.

Nunca se escribe plaintext a temp, journal, backup, log ni archivo final.

ACL objetivo del archivo: `LocalSystem` y `Builtin Administrators` con `FullControl`; usuarios estandar y `Authenticated Users` no reciben read/write explicito. El ACL es defensa adicional: la confidencialidad principal depende de DPAPI user scope bajo LocalSystem.

Un administrador local malicioso con control total de Windows queda fuera de la garantia fuerte, porque puede elevarse a SYSTEM. El objetivo es proteger contra alumnos, usuarios estandar, lectura accidental, copia del archivo y uso desde una cuenta normal.

## Memoria Sensible

La password se acepta como secreto opaco. No se trimea, normaliza, cambia de mayusculas/minusculas ni valida contra Windows. Solo se aplica un maximo anti-abuso.

El store evita cachear passwords en singletons, fields o diccionarios. Convierte la password a buffers temporales para construir el payload y limpia buffers controlados con `CryptographicOperations.ZeroMemory`.

`Acquire` devuelve un `ManagedWindowsCredentialLease` disposable con el secreto en buffer mutable UTF-16LE. Al disponerlo, el buffer se sobrescribe. Se reconoce que strings .NET recibidos desde capas futuras no pueden limpiarse de forma fiable, por lo que 19E debera preferir buffers controlados al transportar/provisionar.

## READY

Un slot puede considerarse `READY` solo cuando:

```text
binding configurado
+ SID sigue resolviendo como User
+ credential store contiene una credencial DPAPI usable ligada al mismo SID
```

`READY` no significa que la password haya sido probada con un logon real. Solo significa que Galtek tiene binding y material de credencial protegido estructuralmente usable.

Si falta credencial: `CREDENTIAL_NOT_CONFIGURED`. Si la cuenta desaparecio: `ACCOUNT_NOT_FOUND`.

## Provisioning Remoto 19E1

`PROVISION_MANAGED_CREDENTIAL` significa:

```text
establece la credencial almacenada por Galtek Client para este slot
```

Sirve para provisioning inicial y rotacion de la copia almacenada: crea si falta y reemplaza si existe. No modifica la cuenta Windows, no cambia la password real, no valida con logon, no cambia SID/binding/username/perfil/sesion y no involucra al Session Agent.

El password entra al Agent como bytes UTF-16LE sin BOM ni NUL, se copia a un buffer mutable controlado, se valida contra el maximo vigente y se pasa a `ReplaceUtf16LittleEndianAsync`. Ese buffer se limpia siempre en `finally`. Protobuf/gRPC pueden mantener buffers internos no zeroizables por Galtek; por eso no se guardan referencias al `ByteString`, no se loguea el request y el dedupe no conserva el protobuf completo.

El resultado `SUCCESS` solo confirma DPAPI protect, escritura durable y verificacion del store. Si no llega `OperationResult`, el Master reporta `OPERATION_RESULT_UNKNOWN` y no reintenta automaticamente.

Desde Prompt 19E2, el Master Backend puede invocar `ManagedCredentialProvisioningBridge` con `vaultSessionToken`, `credentialId`, `deviceId`, `operationId` y `accountId` `PRIMARY`/`SECONDARY`. El bridge usa `MasterAccessGuard`, requiere sesion de vault vigente, permite solo `WINDOWS_ACCOUNT`, rechaza `GOOGLE_ACCOUNT`, no devuelve password al caller, no envia `credentialId` ni vault token al Client, codifica el password como UTF-16LE temporal sin BOM/NUL y limpia el `byte[]` controlado despues del gateway.

## CLI, IPC Y Red

No se agrega password a CLI, Local IPC, UI ni logs.

`--managed-account-list` sigue siendo diagnostico local de binding y no intenta descifrar DPAPI desde una consola administrativa normal. La autoridad futura para `credentialConfigured` sera una operacion productiva del Agent Service bajo LocalSystem.

## Relacion Con Credential Provider

Desde Prompt 19G2, el Credential Provider nativo sigue sin leer `managed-windows-credentials.dat` ni llamar DPAPI directamente. El Agent Service es quien adquiere un `ManagedWindowsCredentialLease` despues de validar caller LogonUI, activation one-time, Installation Identity, binding local, SID esperado y ausencia de rebind.

`ACQUIRE_PENDING_CREDENTIAL` recibe solo `activationId`. El Service deriva `accountId` y SID desde su activation snapshot, marca `CONSUMED` antes de llamar `AcquireForWindowsSidAsync`, usa el lease durante el minimo tiempo y dispone el lease despues de construir la respuesta binaria.

La password viaja Service -> Provider como payload binario versionado y acotado dentro del pipe dedicado:

```text
magic/version/status/activationId/passwordByteLength/passwordUtf16Le
```

No se serializa como JSON, Base64, hexadecimal, XML, Protobuf ni string de contrato. El frame secreto no contiene SID, `accountReference`, domain, username, credentialId ni vault token. El segundo acquire de la misma activation falla sin volver a llamar DPAPI.

En el provider, `GetSerialization` usa buffers mutables RAII, limpia receive buffer y plaintext con `SecureZeroMemory`, protege la password con `CredProtectW`, empaqueta `KERB_INTERACTIVE_UNLOCK_LOGON` y transfiere ownership del buffer final a LogonUI. Si algo falla despues del acquire, no se restaura la activation ni se reintenta.

## Limites 19E1

19E1 no implementa Credential Vault integration. 19E2 implementa solo el bridge interno Master. 19G2 implementa solo el tramo local Credential Provider para una activation ya existente. Sigue sin existir API HTTP Master, BatchOperation, Local IPC de credenciales, login remoto, switch, `LogonUserW`, `CreateProcessAsUser`, `LsaLogonUser`, autologon, cambio de password ni validacion de password contra Windows.

No agrega timers, polling, reads/writes periodicos, threads, heartbeat fields ni account scans. DPAPI solo se usa bajo operaciones explicitas: provisioning remoto, status explicito y acquire one-time para Credential Provider.

## Validacion Manual Pendiente

1. Confirmar que el Service corre como LocalSystem.
2. Provisionar `PRIMARY` mediante el bridge interno 19E2 o una superficie administrativa segura posterior.
3. Verificar que `managed-windows-credentials.dat` no contiene password ni SID en plaintext.
4. Reiniciar el Service y confirmar que la credencial sigue usable.
5. Copiar el credential store a otra instalacion y confirmar fail closed.
6. Rebindear `PRIMARY` a otro SID y confirmar que la credencial vieja queda no configurada.
7. Re-provisionar y confirmar `READY`.
8. Cuando exista mecanismo lab seguro en 19G3 o posterior, crear activation y confirmar que el provider puede usar la credential una sola vez.
9. Confirmar que segundo intento exige nueva activation.
10. Confirmar que un usuario Windows estandar no puede leer el credential store.

## Pendiente

- 19G3: operacion remota `LOGON_MANAGED_ACCOUNT`, creation productiva de activation, notification event-driven y resultado operacional.
- 19H: dispatch batch Master y planner.
