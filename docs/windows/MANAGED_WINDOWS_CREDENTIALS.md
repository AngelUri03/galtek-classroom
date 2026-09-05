# Managed Windows Credentials

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

## CLI, IPC Y Red

19D no agrega password a CLI, Local IPC, Protobuf, gRPC, UI ni logs.

`--managed-account-list` sigue siendo diagnostico local de binding y no intenta descifrar DPAPI desde una consola administrativa normal. La autoridad futura para `credentialConfigured` sera una operacion productiva del Agent Service bajo LocalSystem.

## Limites 19D

19D no implementa provisioning remoto, Credential Vault integration, API Master, transporte de password, Local IPC de credenciales, login, logoff, switch, Credential Provider, `LogonUserW`, `CreateProcessAsUser`, `LsaLogonUser`, autologon, cambio de password ni validacion de password contra Windows.

No agrega timers, polling, reads/writes periodicos, threads, heartbeat fields ni account scans. DPAPI solo se usa bajo operaciones explicitas: provisioning futuro, status explicito y acquire futuro para login.

## Validacion Manual Pendiente

1. Confirmar que el Service corre como LocalSystem.
2. Provisionar `PRIMARY` desde la futura operacion 19E.
3. Verificar que `managed-windows-credentials.dat` no contiene password ni SID en plaintext.
4. Reiniciar el Service y confirmar que la credencial sigue usable.
5. Copiar el credential store a otra instalacion y confirmar fail closed.
6. Rebindear `PRIMARY` a otro SID y confirmar que la credencial vieja queda no configurada.
7. Re-provisionar y confirmar `READY`.
8. Confirmar que un usuario Windows estandar no puede leer el credential store.

## Pendiente

- 19E: provisioning administrativo seguro Master -> Client.
- 19F: `LOGOFF_WINDOWS_SESSION`.
- 19G: `LOGON_MANAGED_ACCOUNT` / `SWITCH_MANAGED_ACCOUNT`.
- 19H: dispatch batch Master y planner.
