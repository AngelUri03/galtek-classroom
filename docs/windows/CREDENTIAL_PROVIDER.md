# Credential Provider

Prompt 19G2 completa el mecanismo local seguro para que una activation existente pueda producir una credential Galtek mediante Credential Provider V2 nativo. Todavia no existe `LOGON_MANAGED_ACCOUNT` remoto, endpoint Master, BatchOperation, planner, notification remoto ni auto-logon remoto.

## Mecanismo

El mecanismo planificado para introducir credenciales al flujo normal Winlogon/LSA es un Credential Provider V2 de Windows.

No se permite sustituirlo por:

- `LogonUser`;
- `CreateProcessAsUser`;
- `CreateProcessWithLogonW`;
- Registry autologon;
- `DefaultPassword`;
- Winlogon registry password;
- SendKeys;
- UI Automation;
- PowerShell, `cmd` o scripts;
- RDP;
- APIs WinStation no documentadas.

## Proyecto Nativo

El provider vive en:

```text
agent/native/GaltekClassroom.CredentialProvider/
```

Es una DLL C++ nativa con Windows SDK. No carga .NET dentro de LogonUI y no usa librerias externas.

CLSID Galtek:

```text
{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}
```

Interfaces implementadas:

- `ICredentialProvider`;
- `ICredentialProviderCredential`;
- `ICredentialProviderCredential2`.

19G2 soporta solo `CPUS_LOGON`. `CPUS_CREDUI`, `CPUS_CHANGE_PASSWORD` y unlock quedan no soportados productivamente.

## Provider Aditivo

Galtek no implementa `ICredentialProviderFilter`.

Nunca debe ocultar ni reemplazar:

- Password Provider de Windows;
- PIN;
- Windows Hello;
- otros providers instalados.

Si Galtek falla, los mecanismos estandar de Windows deben seguir disponibles. Galtek falla abierto hacia login estandar de Windows, pero cerrado respecto a autenticacion Galtek.

## Estado 19G2

Sin activation pendiente:

```text
0 credenciales Galtek
```

Con activation vigente + identity valida:

```text
1 credential Galtek
```

La tile no contiene campo password visible/editable ni boton de reveal. `SetSelected` devuelve `autoLogon = FALSE`; 19G2 no intenta auto-logon por activacion remota ni llama `CredentialsChanged` desde un listener background.

`GetSerialization` adquiere la password una sola vez, construye credential serialization soportada para `CPUS_LOGON` y devuelve `CPGSR_RETURN_CREDENTIAL_FINISHED` solo cuando el buffer completo esta listo. Una segunda llamada sobre la misma credential no reacquire y devuelve `CPGSR_NO_CREDENTIAL_NOT_FINISHED`.

## Service Como Autoridad

El Credential Provider no lee directamente:

- `managed-windows-accounts.json`;
- `managed-windows-credentials.dat`;
- `installation.json`;
- `license.dat`;
- `authorized-masters.json`;
- `credential-vault.dat`.

Toda decision viene desde `GaltekClassroom.Agent.Service`. El provider es solo un adapter minimo para LogonUI.

## Bridge Local

El bridge usa un Named Pipe dedicado:

```text
GaltekClassroom.CredentialProvider.v1
```

No reutiliza:

- `GaltekClassroom.Agent.v1`;
- `GaltekClassroom.Agent.SessionCommand.v1.<sessionId>`.

Direccion:

```text
Agent Service -> pipe server
Credential Provider -> pipe client
```

No hay TCP, HTTP, gRPC local, LAN sockets, DNS ni red desde el provider.

## ACL Y Caller Validation

El pipe se crea solo para `LocalSystem`. No concede acceso explicito a Builtin Users, Authenticated Users, Interactive Users ni Builtin Administrators.

El Service no confia solo en el token `SYSTEM`. Cuando un cliente conecta:

1. obtiene el PID real del cliente con `GetNamedPipeClientProcessId`;
2. inspecciona el proceso por PID real;
3. exige image path canonico equivalente a `%SystemRoot%\System32\LogonUI.exe`;
4. exige que el proceso este en una sesion Windows interactiva;
5. obtiene el SID real del token del pipe por impersonation;
6. exige `S-1-5-18` (`LocalSystem`);
7. falla cerrado ante cualquier duda.

El request JSON no puede suministrar PID, SID, username ni process name para autorizarse. Esos campos se rechazan por forma de contrato y, de todos modos, la autorizacion ocurre antes de manejar la request.

## Protocolo

El framing externo conserva longitud big-endian de 4 bytes y limite de 8 KiB.

Version:

```text
protocolVersion = 1
```

Operaciones JSON no secretas:

- `PING`;
- `GET_PENDING_ACTIVATION_METADATA`;
- `GET_PENDING_ACTIVATION_IDENTITY`.

`GET_PENDING_ACTIVATION_IDENTITY` devuelve solo al caller LogonUI validado:

```text
activationId
accountId
userSid
domain
username
```

El Service deriva esos campos desde la activation, el binding local `PRIMARY`/`SECONDARY`, el `windowsSid` y la resolucion Windows `SidTypeUser`. No usa `accountReference` como autoridad y no recibe identidad desde Master.

Operacion secreta:

```text
ACQUIRE_PENDING_CREDENTIAL(activationId)
```

El request solo acepta `activationId`. No acepta `accountId`, SID, username, domain, password, credentialId, vault token, PID, proceso, sessionId, command ni payload arbitrario.

La respuesta de acquire es binaria, versionada y acotada:

```text
magic/version/status/activationId/passwordByteLength/passwordUtf16Le
```

No usa JSON, Base64, hexadecimal, XML ni Protobuf para password. El frame secreto no repite SID/domain/username/accountReference/credentialId/vault token. Password maximo vigente: `ManagedWindowsCredentialConstants.MaximumPasswordCharacters` (1024 chars UTF-16, 2048 bytes) mas overhead pequeno; el limite total sigue por debajo de 8 KiB.

## Activation Metadata

Modelo efimero:

```text
CredentialProviderActivation {
  activationId
  accountId
  createdAtUtc
  expiresAtUtc
  state: PENDING | IDENTITY_RESOLVED | CONSUMED
  expectedWindowsSid?
}
```

`accountId` solo acepta `PRIMARY` o `SECONDARY`.

La activacion:

- vive solo en memoria del Agent Service;
- mantiene como maximo una activacion pendiente por Client;
- reemplaza la activacion anterior al crear una nueva;
- expira lazy al consultar;
- dura por default 45 segundos y maximo 60 segundos;
- desaparece al reiniciar el Service.

No hay archivo, Registry, SQLite, DPAPI, heartbeat, timer ni polling para activation.

## SID Y Rebind Safety

La primera identity exitosa fija `expectedWindowsSid` en memoria. Antes de acquire, el Service relee el binding local y exige que el `windowsSid` actual siga coincidiendo con ese SID esperado. Si PRIMARY/SECONDARY fue rebindeado entre identity y acquire, la operacion falla cerrado y no revela credential.

## One-Time Acquisition

Una activation puede revelar credential como maximo una vez. Despues de validar activation, identity snapshot y binding vigente, el Service marca la activation `CONSUMED` atomicamente antes de llamar `ManagedWindowsCredentialStore.AcquireForWindowsSidAsync(...)`. Si el pipe falla durante el envio, si falla `CredProtectW`, si falla el packing o si falla lookup de `Negotiate`, la activation no se restaura.

El segundo acquire de la misma activation falla sin volver a llamar DPAPI.

El Service usa `ManagedWindowsCredentialLease` durante el tiempo minimo y no convierte la password a `string` .NET.

## Serialization Windows

`GetUserSid` devuelve el SID que el Service resolvio para la activation, con memoria transferida segun ownership COM.

`GetSerialization` para `CPUS_LOGON`:

1. verifica que la credential local no haya intentado acquire;
2. llama `ACQUIRE_PENDING_CREDENTIAL` una sola vez;
3. valida activationId de la respuesta;
4. recibe password UTF-16LE en buffer mutable;
5. protege la password con `CredProtectW`;
6. inicializa `KERB_INTERACTIVE_UNLOCK_LOGON`;
7. usa `MessageType = KerbInteractiveLogon`;
8. resuelve `Negotiate` con `LsaLookupAuthenticationPackage`;
9. empaqueta y entrega `rgbSerialization` con CLSID `{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}`;
10. limpia buffers propios.

Si el buffer se entrega exitosamente a Windows, el provider transfiere ownership y no puede zeroizarlo inmediatamente. Si no se entrega, cualquier buffer sensible propio se limpia antes de liberar. La serialization nunca se loguea, cachea, persiste ni imprime en self-test.

## Memoria Sensible

Windows kernel/Named Pipe, COM, LSA y APIs del sistema pueden crear buffers transitorios que Galtek no puede zeroizar. La garantia de 19G2 es: sin persistencia, sin logs, sin JSON/Base64/string de contrato, sin caches Galtek, lease dispuesto inmediatamente y buffers propios zeroizados con `CryptographicOperations.ZeroMemory` en .NET o `SecureZeroMemory` en C++.

## Registro Manual Lab

Scripts lab-only:

```text
installer/windows/register-credential-provider-dev.ps1
installer/windows/unregister-credential-provider-dev.ps1
```

El registro apunta por default a:

```text
<ProgramFiles>\Galtek\Classroom\Agent\CredentialProvider\GaltekClassroom.CredentialProvider.dll
```

No debe apuntar al artifact del repo, Desktop, AppData alumno ni Temp. El directorio y la DLL deben quedar bajo Program Files con escritura restringida a administradores/SYSTEM/TrustedInstaller segun politica del equipo; usuarios estandar solo read/execute.

## Validacion Manual

No ejecutar automaticamente en el equipo de desarrollo. Usar PC descartable/laboratorio.

Antes:

- conocer password de administrador local;
- tener recovery disponible;
- no probar primero en el Master real de la profesora.

Pasos:

1. Publicar/copiar la DLL al directorio lab controlado bajo Program Files.
2. Confirmar ACL del directorio y DLL: usuarios estandar sin write.
3. Registrar con `register-credential-provider-dev.ps1`.
4. Cerrar o bloquear sesion en PC descartable.
5. Confirmar que Password/PIN/Windows Hello siguen visibles.
6. Confirmar que sin activation Galtek no intenta autenticar.
7. Si no existe mecanismo seguro para crear activation manual, dejar login end-to-end para 19G3.
8. Cuando exista mecanismo lab seguro, crear activation controlada y confirmar que aparece tile Galtek solo con activation.
9. Confirmar que Password/PIN/Windows Hello siguen visibles.
10. Intentar login controlado con password correcta/incorrecta en PC descartable.
11. Confirmar que un segundo intento exige nueva activation.
12. Detener Agent Service y confirmar que login estandar sigue funcionando.
13. Ejecutar `unregister-credential-provider-dev.ps1`.
14. Confirmar que Galtek provider desaparece.

No agregar CLI de password ni ejecutar login real automaticamente en la maquina de desarrollo.

## Performance

El provider usa timeout corto para consultar el pipe. Si el Service no esta disponible, se considera unavailable de forma silenciosa y no bloquea LogonUI.

El Service no agrega timer permanente, polling, WMI, disk scan, heartbeat field ni background crypto. El pipe queda esperando conexiones sin actividad periodica. Identity/acquire ocurren solo cuando LogonUI consulta una activation existente.

## Pendiente 19G3

- `LOGON_MANAGED_ACCOUNT` remoto.
- Capability remota.
- `OperationRequest` tipado.
- Creation productiva de activation.
- Notification event-driven al provider.
- `ICredentialProviderEvents::CredentialsChanged`.
- Auto-selection/auto-logon cuando Windows lo permita.
- Resultado operacional seguro.
