# Managed Windows Accounts

Prompt 19B agrega la fuente de verdad local del Client para vincular dos slots logicos Galtek con cuentas Windows reales:

```text
PRIMARY
SECONDARY
```

## Fuente De Verdad

El archivo es:

```text
<CommonApplicationData>\Galtek\Classroom\managed-windows-accounts.json
```

En desarrollo y tests usa el mismo override vigente del Agent: `GALTEK_CLASSROOM_DATA_DIR`.

Este archivo pertenece al Client y esta separado de `installation.json`, `license.dat`, `master-binding.json`, `network-identity.json`, `authorized-masters.json`, `application-bindings.json`, browser policy state/journals, `managed-windows-credentials.dat` y `credential-vault.dat`.

## Modelo

El documento versionado incluye:

```json
{
  "schemaVersion": 1,
  "installationId": "...",
  "bindings": [
    {
      "accountId": "PRIMARY",
      "windowsSid": "S-1-5-21-...",
      "accountReference": "PC23\\Primaria",
      "createdAtUtc": "...",
      "updatedAtUtc": "..."
    }
  ]
}
```

`installationId` debe coincidir con la Installation Identity actual. Si el archivo fue copiado desde otra PC, Galtek falla cerrado con `MANAGED_ACCOUNT_BINDINGS_INVALID` y no adopta los bindings.

## SID Como Identidad

La identidad fuerte es `windowsSid`. `accountReference` es solo la referencia canonica/informativa que Windows devuelve, normalmente `MACHINE\User` o `DOMAIN\User`.

Si una cuenta se renombra y conserva SID, el binding sigue siendo valido. `list/status` puede mostrar el nombre canonico nuevo sin reescribir automaticamente el archivo.

Si una cuenta se borra, el binding permanece fisicamente, pero su estado operacional pasa a `ACCOUNT_NOT_FOUND`. Si luego se crea otra cuenta con el mismo username y otro SID, Galtek no la adopta. Cambiar el SID requiere replace explicito.

## Resolucion Windows

Bind/replace usa APIs nativas de Windows:

- `LookupAccountNameW` para nombre -> SID.
- `ConvertSidToStringSidW` para SID nativo -> string.
- `ConvertStringSidToSidW` y `LookupAccountSidW` para SID -> referencia canonica.

No usa PowerShell, cmd, WMI, `net user`, parsing de texto, Registry SAM, scans de perfiles ni `C:\Users`.

Un nombre corto como `Primaria` se interpreta como cuenta local de la maquina actual: `<MACHINE>\Primaria`. Referencias explicitas `DOMAIN\User` o `user@domain` se resuelven normalmente por Windows.

Solo se aceptan cuentas `SidTypeUser`. Grupos, aliases, well-known groups, dominios, computer accounts, invalid/unknown se rechazan.

## Estado

`--managed-account-list` representa siempre ambos slots:

```text
NOT_CONFIGURED
CREDENTIAL_NOT_CONFIGURED
ACCOUNT_NOT_FOUND
```

Desde 19D, el status interno productivo puede marcar `credentialConfigured=true` solo si `managed-windows-credentials.dat` contiene una credencial DPAPI usable ligada al mismo `windowsSid` del binding vigente.

`READY` requiere:

```text
binding configurado
+ SID resoluble como SidTypeUser
+ credencial usable ligada al mismo SID
```

No significa que la password haya sido validada contra Windows mediante logon real. Si falta credencial, el estado es `CREDENTIAL_NOT_CONFIGURED`; si el SID del binding no resuelve a cuenta User, `ACCOUNT_NOT_FOUND`.

La CLI `--managed-account-list` sigue siendo diagnostico local de binding y no descifra DPAPI desde una consola administrativa normal.

## CLI

Comandos locales administrativos:

```text
--managed-account-list
--managed-account-bind PRIMARY <WINDOWS_ACCOUNT>
--managed-account-bind SECONDARY <WINDOWS_ACCOUNT>
--managed-account-remove PRIMARY
--managed-account-remove SECONDARY
--replace-managed-account-binding
```

`bind`, `replace` y `remove` requieren consola elevada y no autoelevan. `list` es read-only, no crea el archivo y respeta el ACL vigente.

La salida normal no muestra SID. No se aceptan parametros de password, credential, secret, token ni PIN.

## Seguridad

`managed-windows-accounts.json` no contiene:

- password;
- password hash;
- credentialId;
- token;
- PIN;
- Windows token;
- sessionId;
- profile path;
- home directory;
- admin flag;
- group membership.

Las passwords Windows administradas viven en `managed-windows-credentials.dat`, cifradas con DPAPI bajo LocalSystem y separadas de este archivo. El binding sigue siendo la autoridad del SID; el credential store no almacena `accountReference` visible.

El archivo se escribe con `DurableFileWriter`: temp file en el mismo directorio, flush/fsync, replace/move atomico y verificacion posterior.

ACL objetivo: `LocalSystem` y `Builtin Administrators` con `FullControl`; usuarios normales sin read/write explicito.

## Limites 19B

19B/19D no implementan:

- Credential Vault integration;
- Master HTTP API;
- Java productivo;
- Protobuf;
- gRPC;
- Local IPC;
- Session Agent;
- browser policy integration por `PRIMARY`/`SECONDARY`;
- login, logoff, switch o Credential Provider;
- creacion, borrado, renombre o cambio de password de cuentas Windows.

19D implementa passwords solo como almacenamiento local cifrado interno del Agent Service. 19E1 agrega provisioning remoto seguro con `PROVISION_MANAGED_CREDENTIAL`; 19E2 agrega el bridge interno Credential Vault -> MasterRemoteOperationGateway. 19G1 agrega foundation de Credential Provider V2 y activation metadata efimera, pero sigue sin existir HTTP, BatchOperation, login/switch, reveal Client-side ni uso de password en LogonUI.

No agrega timers, polling, WMI, enumeracion de usuarios, profile scanning ni trabajo idle. La resolucion ocurre solo on-demand durante bind/replace/list/status.

## Uso Desde Windows Session State

Desde Prompt 19C, `GET_WINDOWS_SESSION_STATE` usa estos bindings solo para comparar SID contra el SID real del token de la consola fisica actual.

La comparacion no usa `accountReference` ni username. Si una cuenta se renombra y conserva SID, un token activo con ese SID sigue clasificando como `PRIMARY_ACTIVE` o `SECONDARY_ACTIVE`. Si no hay bindings configurados y hay un usuario real, el estado es `OTHER_SESSION_ACTIVE`; si el catalogo existe pero esta corrupto o pertenece a otra instalacion, la operacion falla cerrado como `MANAGED_ACCOUNT_BINDINGS_INVALID`.

`managed-windows-accounts.json` sigue sin guardar passwords, tokens, sessionId, profile path ni estado de sesion. `GET_WINDOWS_SESSION_STATE` tampoco escribe este archivo.

## Uso Desde Windows Session Logoff

Desde Prompt 19F, `LOGOFF_WINDOWS_SESSION` usa estos bindings como expected account para cerrar la sesion de consola fisica actual solo si el SID real del `TokenUser` coincide con el `windowsSid` del slot solicitado.

El request remoto transporta solo `accountId` `PRIMARY` o `SECONDARY`. No acepta username, domain, SID, `accountReference`, password, sessionId, force, timeout ni comandos. Sin binding para el slot solicitado devuelve `ACCOUNT_NOT_CONFIGURED`; binding corrupto/schema/mismatch devuelve `MANAGED_ACCOUNT_BINDINGS_INVALID`.

Para logoff, el SID del token activo es suficiente aunque `LookupAccountSid` ya no resuelva la cuenta porque fue borrada mientras la sesion sigue viva. Galtek no adopta otro SID por username y no exige credencial almacenada para cerrar sesion.

`NO_SESSION` es `SUCCESS` idempotente. Si la consola pertenece a otro SID, incluso otro managed account o administrador, `LOGOFF_WINDOWS_SESSION` no la cierra y devuelve `WINDOWS_SESSION_CHANGED`.

## Uso Desde Credential Provider

Desde Prompt 19G1, el Credential Provider V2 no lee este archivo directamente. El provider consulta solo al Agent Service por el pipe dedicado `GaltekClassroom.CredentialProvider.v1`.

La activation metadata futura usa solo `accountId` logico `PRIMARY` o `SECONDARY`, expira rapido, vive en memoria del Service y no contiene SID, `accountReference`, username ni password. En 19G1, incluso con activation valida, el provider no enumera una credential productiva ni intenta login.

## Validacion Manual Pendiente

En una PC descartable:

1. Crear o usar cuentas locales reales `Primaria` y `Secundaria`.
2. Ejecutar bind de `PRIMARY -> Primaria` y `SECONDARY -> Secundaria`.
3. Verificar que list muestre ambas con `CREDENTIAL_NOT_CONFIGURED`.
4. Renombrar `Primaria`; el SID permanece y el binding sigue valido.
5. Provisionar credencial con `PROVISION_MANAGED_CREDENTIAL` y confirmar status interno `READY`.
6. Con `PRIMARY` logueado, ejecutar `LOGOFF_WINDOWS_SESSION(PRIMARY)` y confirmar que cierra solo si el SID activo coincide.
7. Borrar `Primaria` dejando su sesion viva; confirmar que `LOGOFF_WINDOWS_SESSION(PRIMARY)` todavia puede cerrarla por SID.
8. Borrar `Primaria`; el binding queda `ACCOUNT_NOT_FOUND` para status/list.
9. Crear otra `Primaria`; el SID nuevo no se adopta automaticamente y la credencial vieja no queda usable.
10. Ejecutar replace explicito para `PRIMARY -> nueva Primaria`.

Galtek no debe borrar, crear, renombrar ni modificar cuentas Windows automaticamente en 19B.

## Pendiente

- 19G2: one-time credential acquisition y `GetSerialization` real soportado por Windows.
- 19H: dispatch batch Master.
