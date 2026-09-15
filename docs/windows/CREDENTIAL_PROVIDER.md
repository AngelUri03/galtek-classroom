# Credential Provider

Prompt 19I1 integra el Credential Provider nativo al lifecycle productivo del Agent: `PUBLISH -> INSTALL -> UPDATE -> VERIFY -> UNINSTALL`. No ejecuta registro real ni validacion de logon/switch en la maquina de desarrollo; el estado queda `PACKAGE_VERIFIED`, `INSTALLER_PREPARED` y `REAL_LOGON_VALIDATION_PENDING`.

Prompt 19G3 completo `LOGON_MANAGED_ACCOUNT` remoto para un Client individual mediante Credential Provider V2 nativo, activation efimera, notification event-driven, auto-submit once y resultado confirmado por `ReportResult`. Fases posteriores agregaron `SWITCH_MANAGED_ACCOUNT` Agent-side y batch/retry Master para switch, sin cambiar el protocolo nativo del provider.

Retest fisico PC14 del 2026-09-15 con `operationId=889798b1-2274-4a31-8f1f-c78c4706db83`: por primera vez hubo logon Windows real exitoso de PRIMARY. El provider obtuvo identity real, devolvio `count=1 autoLogon=1`, entro a `GetSerialization`, ejecuto `ACQUIRE_PENDING_CREDENTIAL`, resolvio `Negotiate`, aplico `CredProtectW`, empaqueto Kerberos corregido y devolvio serialization. Winlogon/Operational registro autenticacion detenida con `Resultado 0`, y la maquina quedo fisicamente en `ICH11\ICH-PRIMARIA-14`. El fix Kerberos del antiguo `ERROR_INVALID_PARAMETER 87` queda fisicamente validado para PC14/Windows 11 build 22621.

La frontera pendiente no esta ya en password, DPAPI, caller validation, listener, identity snapshot, acquire ni serialization. Esta en `ICredentialProviderCredential::ReportResult -> REPORT_LOGON_RESULT -> Agent completion -> OperationResult SUCCESS`: Master termino `FAILED WINDOWS_LOGON_NOT_CONFIRMED` porque no aparecio `CP_REPORT_RESULT` ni `REPORT_LOGON_RESULT`, aunque Windows/LSA acepto la serialization.

La auditoria posterior encontro una carrera de visibilidad: `ACQUIRE_PENDING_CREDENTIAL` consumia la activation e incrementaba la generation visible, despertando `WAIT_FOR_ACTIVATION_CHANGE` durante `GetSerialization`. El worker llamaba `CredentialsChanged`, LogonUI reenumeraba, `GetPending` ocultaba la activation `CONSUMED` y el provider devolvia `count=0 autoLogon=0` mientras la credential original seguia in-flight. El fix separa cambios internos de lifecycle de cambios visibles para enumeracion: consume/acquire ya no publica generation.

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

19G3 soporta solo `CPUS_LOGON`. `CPUS_CREDUI`, `CPUS_CHANGE_PASSWORD` y unlock quedan no soportados productivamente.

## Provider Aditivo

Galtek no implementa `ICredentialProviderFilter`.

Nunca debe ocultar ni reemplazar:

- Password Provider de Windows;
- PIN;
- Windows Hello;
- otros providers instalados.

Si Galtek falla, los mecanismos estandar de Windows deben seguir disponibles. Galtek falla abierto hacia login estandar de Windows, pero cerrado respecto a autenticacion Galtek.

## Estado 19G3

Sin activation pendiente:

```text
0 credenciales Galtek
```

Con activation vigente + identity valida:

```text
1 credential Galtek
```

La tile no contiene campo password visible/editable ni boton de reveal. Si la activation remota tiene `autoSubmitRequested=true`, `GetCredentialCount` devuelve `count = 1`, `defaultCredential = 0` y `pbAutoLogonWithDefault = TRUE`. `SetSelected` solicita auto-logon exactamente una vez para esa credential. Las activations locales/manuales no solicitan auto-logon.

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
5. obtiene el SID real del token del pipe por impersonation explicita;
6. exige `S-1-5-18` (`LocalSystem`);
7. falla cerrado ante cualquier duda.

Antes de impersonar, el Service lee solo el frame acotado del pipe; no procesa ni autoriza por el JSON. Esa lectura es necesaria porque `ImpersonateNamedPipeClient` usa el contexto de seguridad del ultimo mensaje leido del pipe. La ruta vigente para el token del cliente es auditable y sincronica: `ImpersonateNamedPipeClient`, `OpenThreadToken(TOKEN_QUERY, OpenAsSelf=true)`, `GetTokenInformation(TokenUser)`, `ConvertSidToStringSidW`, `LocalFree`, `CloseHandle` y `RevertToSelf`. Los imports P/Invoke de este tramo fijan `EntryPoint` real y `ExactSpelling`, incluyendo `ImpersonateNamedPipeClient` aunque el metodo managed privado tenga prefijo `Native`. No usa `WindowsIdentity.GetCurrent()` como caja negra, no cruza `await` dentro del scope de impersonation, no abre el token del proceso del Agent y no hace fallback a `OpenProcessToken(clientPid)`. Para `TokenUser` se pide solo `TOKEN_QUERY`; no se solicita `TOKEN_DUPLICATE`, `TOKEN_ADJUST_PRIVILEGES` ni otro derecho no necesario.

Los fallos de caller validation se registran por etapa no secreta: `pid`, `process`, `image`, `session`, `impersonation`, `open_thread_token`, `token_user_length`, `token_user`, `token_sid`, `revert_to_self` o `not_local_system`. Cuando la etapa viene de una API Win32, el log agrega `win32=<codigo>` y `clientPid`; no registra token handle, SID completo, username ni material sensible.

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
- `GET_PENDING_ACTIVATION_IDENTITY`;
- `WAIT_FOR_ACTIVATION_CHANGE`;
- `REPORT_LOGON_RESULT`.

`GET_PENDING_ACTIVATION_IDENTITY` devuelve solo al caller LogonUI validado una respuesta JSON `SUCCESS` con `activationStatus=PENDING` y la identidad anidada en `pendingIdentity`:

```text
pendingIdentity.activationId
pendingIdentity.accountId
pendingIdentity.userSid
pendingIdentity.domain
pendingIdentity.username
pendingIdentity.autoSubmitRequested
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

`WAIT_FOR_ACTIVATION_CHANGE(observedGeneration)` bloquea hasta que cambia la generation visible de enumeracion o hasta cancelacion/shutdown. Crear una activation visible incrementa la generation. Completar, expirar antes de submit o limpiar una activation visible tambien incrementa la generation para retirar la tile. Cambios internos del lifecycle, como identity snapshot y `IDENTITY_RESOLVED -> CONSUMED` durante `ACQUIRE_PENDING_CREDENTIAL`, no incrementan la generation visible y no deben forzar `CredentialsChanged` durante auth in-flight. Esta es la ruta sana de notification; no debe reemplazarse por polling del provider.

Desde el fix 2026-09-14 para PC14, el Agent tambien mantiene una observacion fresca corta de callers LogonUI ya autenticados por el bridge. En el retest fisico posterior, el bridge paso caller validation real, marco fresh presence, satisfizo disponibilidad por `active_wait`, resolvio identity y completo `ACQUIRE_PENDING_CREDENTIAL`; por tanto `CREDENTIAL_PROVIDER_UNAVAILABLE` ya no es el fallo vigente de PC14. La observacion fresca se conserva como tolerancia bajo diagnostico: solo se marca despues de validar PID real, imagen `LogonUI.exe`, sesion interactiva y token `LocalSystem`, expira en memoria y nunca autoriza por payload.

La disponibilidad que consume `WindowsSessionLogonService` distingue ahora `active_wait`, `fresh_presence` y `unavailable`. Durante retest fisico deben observarse `WAIT_LISTENER_ENTERED` y luego exactamente uno de `LISTENER_AVAILABILITY_SATISFIED:active_wait`, `LISTENER_AVAILABILITY_SATISFIED:fresh_presence` o `LISTENER_AVAILABILITY_TIMEOUT`. El bridge agrega tracing no secreto para diferenciar caller validation pasada, caller validation fallida por etapa, fresh presence marcada, operation recibida y respuesta/escritura.

`REPORT_LOGON_RESULT(activationId,outcome)` acepta solo:

- `SUCCESS`;
- `FAILED`;
- `LOCAL_SERIALIZATION_FAILED`.

No transporta mensaje, NTSTATUS textual, SID, username, domain, sessionId, password ni retry instruction.

## Activation Metadata

Modelo efimero:

```text
CredentialProviderActivation {
  activationId
  accountId
  operationId?
  createdAtUtc
  expiresAtUtc
  autoSubmitRequested
  source: LOCAL | REMOTE
  state: PENDING | IDENTITY_RESOLVED | CONSUMED | COMPLETED
  expectedWindowsSid?
}
```

`accountId` solo acepta `PRIMARY` o `SECONDARY`.

La activacion:

- vive solo en memoria del Agent Service;
- mantiene como maximo una activacion pendiente por Client;
- una activation local puede reemplazar la anterior;
- una activation remota productiva no reemplaza otra remota de distinto `operationId`;
- expira lazy al consultar;
- dura por default 45 segundos y maximo 60 segundos;
- desaparece al reiniciar el Service.

No hay archivo, Registry, SQLite, DPAPI, heartbeat, timer ni polling para activation.

## SID Y Rebind Safety

La primera identity exitosa fija `expectedWindowsSid` en memoria. Antes de acquire, el Service relee el binding local y exige que el `windowsSid` actual siga coincidiendo con ese SID esperado. Si PRIMARY/SECONDARY fue rebindeado entre identity y acquire, la operacion falla cerrado y no revela credential.

## One-Time Acquisition

Una activation puede revelar credential como maximo una vez. Despues de validar activation, identity snapshot y binding vigente, el Service marca la activation `CONSUMED` atomicamente antes de llamar `ManagedWindowsCredentialStore.AcquireForWindowsSidAsync(...)`. Ese cambio es interno y no publica generation visible, porque la credential que entrego serialization debe poder recibir `ReportResult`. Si el pipe falla durante el envio, si falla `CredProtectW`, si falla el packing o si falla lookup de `Negotiate`, la activation no se restaura.

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

El blob de `KERB_INTERACTIVE_UNLOCK_LOGON` para Winlogon/LSA usa `UNICODE_STRING.Buffer` como offset relativo al inicio de `rgbSerialization`, no como puntero absoluto del proceso del provider. `Length` y `MaximumLength` se expresan en bytes; para los strings empaquetados `MaximumLength == Length` porque no se copia terminador NUL. Cada offset debe caer dentro de `cbSerialization`, respetar alignment de `wchar_t` y apuntar a bytes contiguos propios del blob.

Si el buffer se entrega exitosamente a Windows, el provider transfiere ownership y no puede zeroizarlo inmediatamente. Si no se entrega, cualquier buffer sensible propio se limpia antes de liberar. La serialization nunca se loguea, cachea, persiste ni imprime en self-test.

## Notification Y Resultado

Durante `CPUS_LOGON + Advise`, el provider inicia un worker con COM inicializado y unmarshalea `ICredentialProviderEvents` mediante marshaling inter-thread. El worker espera `WAIT_FOR_ACTIVATION_CHANGE` con cancelacion por `UnAdvise`; cuando cambia la generation llama `CredentialsChanged(adviseContext)` para que LogonUI reenumere. El cliente nativo no debe aplicar el timeout corto de RPC a la lectura del long-poll: la espera finaliza por generation change/respuesta legitima, cancelacion de `UnAdvise`, shutdown o error real del pipe. El self-test nativo cubre un wait de 2.5 segundos y cancelacion acotada.

El worker no debe avanzar `observedGeneration` antes de conocer el `HRESULT` de `CredentialsChanged`. Si el callback falla, esa generation sigue no observada, el worker espera un backoff acotado y reintenta el mismo cambio sin polling rapido. Solo despues de `CredentialsChanged` exitoso se emite `CP_WORKER_REARM generation=<n>` y el siguiente wait usa la generation nueva. Esto evita perder una activation vigente cuando el wakeup del Agent ya ocurrio pero LogonUI todavia no reenumero.

Para el siguiente retest PC14 el provider emite diagnostico nativo temporal/no secreto por `OutputDebugStringW`: `CP_WORKER_WAIT_SUCCESS generation=<n>`, `CP_CREDENTIALS_CHANGED_CALL generation=<n>`, `CP_CREDENTIALS_CHANGED_RESULT hr=<numeric>`, `CP_WORKER_REARM generation=<n>`, `CP_UNADVISE`, `CP_WORKER_CANCEL reason=<...>`, `CP_GET_CREDENTIAL_COUNT_ENTERED`, `CP_IDENTITY_QUERY_ENTER`, `CP_IDENTITY_QUERY_PIPE_CONNECTED elapsedMs=<n>`, `CP_IDENTITY_QUERY_REQUEST_SENT elapsedMs=<n>`, `CP_IDENTITY_QUERY_RESPONSE_RECEIVED elapsedMs=<n>`, `CP_IDENTITY_QUERY_RESPONSE_STATUS status=<...>`, `CP_IDENTITY_QUERY_PARSE_OK`, `CP_IDENTITY_QUERY_REJECT reason=<transport|framing|json|protocol|status|account|identity_validation>`, `CP_IDENTITY_QUERY_TIMEOUT stage=<connect|write|read_length|read_payload> win32=<n>`, `CP_GET_CREDENTIAL_COUNT_NO_IDENTITY`, `CP_GET_CREDENTIAL_COUNT_IDENTITY_READY`, `CP_IDENTITY_SNAPSHOT_COMMITTED accountId=<PRIMARY|SECONDARY>`, `CP_GET_CREDENTIAL_COUNT_RETURN count=<0|1> autoLogon=<0|1>`, `CP_CREDENTIAL_CREATED instance=<id>`, `CP_GET_SERIALIZATION_ENTERED`, `CP_IDENTITY_READY`, `CP_CREDENTIAL_ACQUIRED`, `CP_AUTH_PACKAGE_SUCCESS`, `CP_CREDPROTECT_SUCCESS`, `CP_PACK_SUCCESS`, `CP_SERIALIZATION_RETURNED instance=<id>`, `CP_CREDENTIAL_DESTROYED instance=<id>`, `CP_REPORT_RESULT_ENTERED instance=<id> status=<numeric> substatus=<numeric>`, `CP_REPORT_RESULT accountId=<PRIMARY|SECONDARY> status=<numeric> substatus=<numeric>` y `CP_REPORT_RESULT_BRIDGE_SENT instance=<id> outcome=<SUCCESS|FAILED|LOCAL_SERIALIZATION_FAILED>`. No registra password, longitud de password, `rgbSerialization`, SID completo, username, domain, activationId completo, JSON de respuesta ni protected password.

Si `OutputDebugStringW` no se captura en PC14, el mismo tracing puede activarse de forma opt-in con `HKLM\SOFTWARE\Galtek\Classroom\CredentialProvider\NativeTraceFile` (`REG_SZ`) apuntando a un archivo local, por ejemplo `C:\ProgramData\Galtek\Classroom\credential-provider-native.log`. Sin esa clave no se escribe archivo de trace.

`ReportResult` reporta al Service:

- `STATUS_SUCCESS` -> `SUCCESS`;
- rechazo de autenticacion -> `FAILED`.

Si `GetSerialization` falla localmente despues de adquirir la password, reporta `LOCAL_SERIALIZATION_FAILED`. En todos los casos, no reacquire, no reconstruye activation, no restaura password y no oculta los providers estandar.

## Memoria Sensible

Windows kernel/Named Pipe, COM, LSA y APIs del sistema pueden crear buffers transitorios que Galtek no puede zeroizar. La garantia de 19G3 es: sin persistencia, sin logs, sin JSON/Base64/string de contrato, sin caches Galtek, lease dispuesto inmediatamente y buffers propios zeroizados con `CryptographicOperations.ZeroMemory` en .NET o `SecureZeroMemory` en C++.

## Product Lifecycle

Scripts productivos:

```text
installer/windows/publish-credential-provider.ps1
installer/windows/test-credential-provider-package.ps1
installer/windows/install-credential-provider.ps1
installer/windows/test-credential-provider-installation.ps1
installer/windows/uninstall-credential-provider.ps1
```

`publish-credential-provider.ps1` localiza MSBuild, compila `GaltekClassroom.CredentialProvider` como `Release|x64`, no registra nada, valida PE x64 y crea:

```text
artifacts/windows/credential-provider/
  GaltekClassroom.CredentialProvider.dll
  credential-provider.manifest.json
```

El artifact no incluye PDB, obj, lib, exp, tests, source ni `.reg` dev. El manifest contiene metadata no secreta:

```json
{
  "schemaVersion": 1,
  "product": "GALTEK_CLASSROOM",
  "component": "CREDENTIAL_PROVIDER",
  "clsid": "{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}",
  "architecture": "x64",
  "fileName": "GaltekClassroom.CredentialProvider.dll",
  "sha256": "...",
  "packageId": "sha256-..."
}
```

El SHA-256 se calcula despues de producir la DLL final. Sirve para detectar corrupcion o mezcla de artifacts, no sustituye Authenticode.

## Product Install

El install productivo exige:

- Windows x64.
- PowerShell x64.
- elevacion.
- Agent Service `GaltekClassroomAgent` instalado.
- manifest valido.
- DLL PE x64.
- SHA-256 coincidente.
- firma Authenticode no invalida.
- ACL sin write obvio para `Everyone`, `Authenticated Users` ni `Builtin Users`.

La DLL se instala side-by-side:

```text
%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\
  versions\
    <packageId>\
      GaltekClassroom.CredentialProvider.dll
      credential-provider.manifest.json
```

`InprocServer32` apunta directamente al DLL de la version activa. No se usa symlink, junction ni `current.dll`.

Registro machine-wide en vista x64 de HKLM:

```text
HKLM\SOFTWARE\Classes\CLSID\{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}
  (default) = Galtek Classroom Credential Provider

HKLM\SOFTWARE\Classes\CLSID\{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}\InprocServer32
  (default) = <Program Files Galtek>\CredentialProvider\versions\<packageId>\GaltekClassroom.CredentialProvider.dll
  ThreadingModel = Apartment

HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}
  (default) = Galtek Classroom Credential Provider
```

El installer nunca crea HKCU/per-user registration y nunca registra:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters
```

Si el CLSID Galtek existe y apunta fuera de `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\`, el installer falla cerrado con `CREDENTIAL_PROVIDER_REGISTRATION_CONFLICT`.

## Product Update

Upgrade:

1. valida el package nuevo;
2. stagea una version nueva e inmutable por `packageId`;
3. valida lo stageado;
4. cambia `InprocServer32` a la nueva ruta;
5. conserva provider registration;
6. verifica read-back.

No borra la version anterior antes de activar la nueva, no mata LogonUI/Winlogon, no intenta descargar la DLL vieja y no reinicia Windows automaticamente. Una instancia vieja de LogonUI puede terminar naturalmente; futuras instancias cargan la nueva ruta.

## Product Uninstall

`uninstall-credential-provider.ps1` remueve solo:

```text
Authentication\Credential Providers\{GALTEK_CLSID}
SOFTWARE\Classes\CLSID\{GALTEK_CLSID}
```

Luego intenta limpiar package directories Galtek. Si Windows bloquea un archivo cargado, reporta `UNREGISTERED_REBOOT_CLEANUP_REQUIRED`. La parte de seguridad principal es que provider registration y COM registration ya no existen.

El uninstall no toca ProgramData, `installation.json`, `license.dat`, bindings, credenciales, pairing, browser policies ni otros Credential Providers.

## Verification

`test-credential-provider-package.ps1` es read-only y no requiere elevacion. Valida artifact/manifest/hash/PE x64/firma sin tocar Program Files, HKLM ni Service Control Manager.

`test-credential-provider-installation.ps1` es read-only y valida:

- OS/proceso x64.
- provider key y COM key exactos.
- `InprocServer32` bajo Program Files Galtek.
- DLL y manifest existentes.
- manifest CLSID/x64/hash correctos.
- `ThreadingModel = Apartment`.
- ausencia de Galtek CLSID bajo Credential Provider Filters.
- ACL sin write obvio para usuarios estandar.
- estado Authenticode informativo/seguro.

## Registro Manual Lab / Dev

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

## Validacion 19I2 En PC Descartable

No ejecutar automaticamente en el equipo de desarrollo. 19I2 debe usar PC descartable/laboratorio.

La PC debe tener:

- Windows 10/11 x64 soportado.
- password conocida de administrador local.
- acceso fisico.
- recovery disponible.
- Windows Password Provider estandar funcional.

Nunca comenzar validacion en la unica PC Master de la maestra, en un equipo sin password administrativa conocida ni en un equipo remoto sin acceso fisico.

Checklist minimo futuro:

- Fresh install: publicar package, ejecutar `install-agent.ps1`, ejecutar verifier, confirmar registry exacto, path bajo Program Files, ACL y providers estandar visibles.
- Fail-open: detener Agent Service, iniciar/cerrar sesion manual y confirmar que Password/PIN/Hello siguen disponibles.
- No activation: sin orden Galtek, confirmar que no aparece login Galtek espontaneo ni auto-submit.
- Real logon: provisionar `PRIMARY`, dejar `NO_SESSION`, ejecutar `LOGON_MANAGED_ACCOUNT(PRIMARY)`, confirmar login y `GET_WINDOWS_SESSION_STATE -> PRIMARY_ACTIVE`.
- Wrong password: password Galtek incorrecta, maximo un intento, sin loop y provider estandar disponible.
- Real switch: `PRIMARY -> SECONDARY` y `SECONDARY -> PRIMARY`, comprobando resultado y efectos.
- Batch: switch batch, `NO_CHANGE`, `PARTIAL_SUCCESS` y retry explicito seguro.
- Update: instalar package nuevo, confirmar que registration apunta a nueva version inmutable, no se fuerza old loaded DLL y nueva LogonUI usa version nueva.
- Uninstall: unregister, COM key removida, provider key removida, login estandar disponible y ProgramData preservado.

No agregar CLI de password ni ejecutar login real automaticamente en la maquina de desarrollo.

## Performance

El provider usa timeout corto para consultar el pipe. Si el Service no esta disponible, se considera unavailable de forma silenciosa y no bloquea LogonUI.

El Service no agrega timer permanente, polling, WMI, disk scan, heartbeat field ni background crypto. El pipe queda esperando conexiones sin actividad periodica. Identity/acquire ocurren solo cuando LogonUI consulta una activation existente. El listener event-driven bloquea sin actividad sana y se cancela en `UnAdvise`.

## Pendiente Posterior

- UI para batch switch/logon si se decide exponerlo.
- Validacion manual completa 19I2 en PC descartable.
