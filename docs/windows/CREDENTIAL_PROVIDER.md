# Credential Provider

Prompt 19G1 agrega la foundation segura para un futuro `LOGON_MANAGED_ACCOUNT` mediante Credential Provider V2 nativo. 19G1 no inicia sesion, no cambia usuario y no serializa passwords.

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

19G1 soporta solo `CPUS_LOGON`. `CPUS_CREDUI`, `CPUS_CHANGE_PASSWORD` y unlock quedan no soportados productivamente.

## Provider Aditivo

Galtek no implementa `ICredentialProviderFilter`.

Nunca debe ocultar ni reemplazar:

- Password Provider de Windows;
- PIN;
- Windows Hello;
- otros providers instalados.

Si Galtek falla, los mecanismos estandar de Windows deben seguir disponibles. Galtek falla abierto hacia login estandar de Windows, pero cerrado respecto a autenticacion Galtek.

## Estado 19G1

Sin activation pendiente:

```text
0 credenciales Galtek
```

Con activation metadata valida, el provider puede detectar la activacion por el bridge local, pero en 19G1 sigue devolviendo 0 credentials productivas para evitar una tile que no pueda completar login real. `GetSerialization` existe en la clase credential y devuelve `CPGSR_NO_CREDENTIAL_NOT_FINISHED`; no construye `KERB_INTERACTIVE_UNLOCK_LOGON`, no llama LSA y no transporta password.

## Service Como Autoridad

El Credential Provider no lee directamente:

- `managed-windows-accounts.json`;
- `managed-windows-credentials.dat`;
- `installation.json`;
- `license.dat`;
- `authorized-masters.json`;
- `credential-vault.dat`.

Toda decision futura vendra desde `GaltekClassroom.Agent.Service`. El provider es solo un adapter minimo para LogonUI.

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

Contrato local JSON UTF-8 con frame de 4 bytes big-endian y limite de 8 KiB.

Version:

```text
protocolVersion = 1
```

Operaciones 19G1:

- `PING`;
- `GET_PENDING_ACTIVATION_METADATA`.

No usa Protobuf.

## Activation Metadata

Modelo efimero 19G1:

```text
CredentialProviderActivation {
  activationId
  accountId
  createdAtUtc
  expiresAtUtc
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

## Secret Exclusion

19G1 no contiene password, password bytes, lease, protectedData, vault token, credentialId ni master password en activation, bridge request o bridge response.

El bridge no llama:

```text
ManagedWindowsCredentialStore.Acquire()
```

La operacion one-time de acquisition queda para 19G2.

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
7. Detener Agent Service y confirmar que login estandar sigue funcionando.
8. Ejecutar `unregister-credential-provider-dev.ps1`.
9. Confirmar que Galtek provider desaparece.

No probar password, autologon ni switch en 19G1.

## Performance

El provider usa timeout corto para consultar el pipe. Si el Service no esta disponible, se considera unavailable de forma silenciosa y no bloquea LogonUI.

El Service no agrega timer permanente, polling, WMI, disk scan, heartbeat field ni background crypto. El pipe queda esperando conexiones sin actividad periodica.

## Pendiente 19G2

- Operacion futura one-time para adquirir credential desde el Agent Service.
- Transporte seguro de secreto Service -> Provider, con limpieza de buffers.
- `GetSerialization` real con formato Windows soportado.
- Integracion con `LOGON_MANAGED_ACCOUNT` y `SWITCH_MANAGED_ACCOUNT`.
- Tests nativos mas amplios cuando el harness de C++ quede definido.
