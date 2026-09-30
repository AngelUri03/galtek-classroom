# Windows Session Logon

## 20F.2-F1

PRIMARY, SECONDARY y ADMIN recorren la misma ruta tipada de binding, SID, credential store, activation payload y `ReportResult`. La divergencia fisica ADMIN estaba despues del Agent, en el parser de accountId del CP nativo, y queda corregida en Client 0.0.6.

La espera normal de listener continua acotada a 750 ms para login directo. El switch puede proporcionar una ventana de 8 s unicamente despues de haber confirmado logout y `NO_SESSION`; esta sobrecarga espera la misma senal real y no cambia el timeout global ni reenvia la activacion.

## 20F.2

Los targets admitidos son PRIMARY, SECONDARY y ADMIN. Target ADMIN requiere step-up fresco Master-side y capability `ADMIN_MANAGED_SESSION_V1`; Agent-side siempre revalida live la estructura administrativa por SID/grupo built-in. Si ADMIN ya está activa el resultado es idempotente. `OTHER_SESSION_ACTIVE` y `UNKNOWN` bloquean toda mutación automática.

Prompt 19G3 implementa `LOGON_MANAGED_ACCOUNT` remoto para un Client individual usando Credential Provider V2 y el flujo soportado de Windows Winlogon/LSA.

## Contrato Remoto

El request Protobuf contiene solo:

```text
LogonManagedAccountOperationParameters {
  ManagedWindowsAccountId account_id
}
```

`account_id` debe ser `PRIMARY`, `SECONDARY` o `ADMIN`; `UNSPECIFIED` se rechaza. El Master no envía password, username, domain, SID, master password, `accountReference`, sessionId, credentialId, command, args, shell, timeout configurable ni payload arbitrario.

La capability es especifica:

```text
WINDOWS_SESSION_LOGON_V1
```

No existe capability generica de session-control.

## Autorizacion

La operacion llega por el `RemoteOperationDispatcher` normal:

- gRPC/mTLS;
- Master esperado;
- trust `PAIRED`;
- Client no `REVOKED`;
- Device correcto;
- operacion soportada;
- Commercial License Client `ACTIVE`.

`UNLOCK_INPUT` sigue siendo la unica excepcion recovery-safe a Commercial License.

## Preflight

El Agent observa la consola fisica antes de tocar credenciales:

- target account ya activo: `SUCCESS` idempotente, sin activation ni DPAPI;
- `NO_SESSION`: continua;
- otro managed account u otro usuario activo: `WINDOWS_SESSION_CHANGED`;
- estado no confiable: `WINDOWS_SESSION_UNKNOWN`.

Luego valida:

- binding local `PRIMARY`/`SECONDARY` existente;
- SID valido y resoluble como `SidTypeUser`;
- credential DPAPI usable en `managed-windows-credentials.dat`;
- SID interno de la credential igual al SID del binding vigente.

Sin binding devuelve `ACCOUNT_NOT_CONFIGURED`; cuenta ausente devuelve `ACCOUNT_NOT_FOUND`; falta credential devuelve `MANAGED_CREDENTIAL_NOT_CONFIGURED`; stores corruptos fallan cerrado.

La consola se revalida inmediatamente antes de activation y debe seguir en `NO_SESSION`. Logon no hace unlock, switch ni logoff implicito.

## Activation

La activation remota vive solo en memoria del Agent Service:

```text
activationId
operationId
accountId
createdAtUtc
expiresAtUtc
autoSubmitRequested = true
expectedWindowsSid
```

No se persiste en archivo, Registry, SQLite, DPAPI ni heartbeat. Restart del Service pierde la activation.

Una activation remota productiva no reemplaza otra remota de distinto `operationId`; en ese caso el Agent devuelve `WINDOWS_LOGON_BUSY`. Un duplicado de la misma operacion y mismo accountId conserva idempotencia por el dispatcher.

## Notification

El bridge `GaltekClassroom.CredentialProvider.v1` agrega:

```text
WAIT_FOR_ACTIVATION_CHANGE(observedGeneration)
REPORT_LOGON_RESULT(activationId, outcome)
```

El Agent Service mantiene un generation counter in-memory. Crear, completar, expirar o limpiar activation incrementa generation y despierta waiters; identity snapshot y acquire/consume no publican cambio de enumeracion.

Mientras el Credential Provider esta en `CPUS_LOGON` y `Advise` activo, un worker cancellable espera `WAIT_FOR_ACTIVATION_CHANGE`, usa COM marshaling inter-thread para `ICredentialProviderEvents` y llama `CredentialsChanged` cuando cambia la generation.

Antes de activation, el Service espera brevemente que exista disponibilidad real de LogonUI/Credential Provider. La disponibilidad se satisface por un long-poll activo (`active_wait`) o por una presencia fresca corta marcada solo despues de caller validation estricta (`fresh_presence`). Si no existe ninguna de las dos antes del timeout, devuelve `CREDENTIAL_PROVIDER_UNAVAILABLE`.

Para diagnostico fisico, `LOGON_MANAGED_ACCOUNT` registra `WAIT_LISTENER_ENTERED`, `LISTENER_AVAILABILITY_SATISFIED:<active_wait|fresh_presence>` o `LISTENER_AVAILABILITY_TIMEOUT`. Si el bridge rechaza el caller, registra `CALLER_VALIDATION_FAILED:<failure-stage>` y no marca fresh presence. En PC14, los retests posteriores observaron `LISTENER_AVAILABILITY_SATISFIED:active_wait`, `CALLER_VALIDATION_PASSED`, `FRESH_PRESENCE_MARKED`, identity exitosa, `ACQUIRE_PENDING_CREDENTIAL` binario, `CP_REPORT_RESULT status=0` y `REPORT_LOGON_RESULT SUCCESS` desde LogonUI real. Las senales no contienen password, SID completo ni material secreto.

En el retest fisico PC14 del 2026-09-14 a las 17:28, `operationId=7a4abf1b-6972-42df-8ca6-6f764e06a69b`, el Agent creo activation para `PRIMARY`, el long-poll `WAIT_FOR_ACTIVATION_CHANGE` estaba activo y respondio `SUCCESS`, y caller validation posterior paso desde LogonUI real `clientPid=5464`. Sin embargo, durante 17:28:15-17:29:15 no aparecieron `GET_PENDING_ACTIVATION_IDENTITY`, `ACQUIRE_PENDING_CREDENTIAL` ni `REPORT_LOGON_RESULT`. Esa evidencia historica quedo superada por los retests posteriores del 2026-09-15.

El provider nativo conserva la generation anterior hasta que `CredentialsChanged(adviseContext)` devuelve `HRESULT` exitoso. Un `HRESULT` fallido no se considera reenumeration exitosa; el worker reintenta la misma generation con backoff acotado y no avanza a un long-poll que podria dormir indefinidamente mientras la activation sigue vigente.

## Auto-Submit Y Serialization

Con activation remota:

- `GetCredentialCount` devuelve una credential;
- default credential es `0`;
- `pbAutoLogonWithDefault = TRUE`;
- `SetSelected` solicita auto-logon exactly once.

`GetSerialization` adquiere la password una sola vez por `ACQUIRE_PENDING_CREDENTIAL(activationId)`, usa `CredProtectW`, construye `KERB_INTERACTIVE_UNLOCK_LOGON`, resuelve `Negotiate` y entrega la serialization a Windows. El pack Kerberos debe usar offsets relativos dentro de `rgbSerialization` en cada `UNICODE_STRING.Buffer`; no se permiten punteros absolutos del proceso. Si falla despues de acquire, reporta `LOCAL_SERIALIZATION_FAILED`, no restaura activation y no reintenta.

## Resultado

`REPORT_LOGON_RESULT` acepta solo:

- `SUCCESS`;
- `FAILED`;
- `LOCAL_SERIALIZATION_FAILED`.

No transporta mensajes, NTSTATUS textual, SID, username, domain, sessionId ni password.

`OperationResult SUCCESS` solo ocurre si `ReportResult` recibe `STATUS_SUCCESS` y reporta `SUCCESS`. Rechazo de autenticacion o fallo local produce `WINDOWS_LOGON_FAILED`. Si no llega confirmacion antes del timeout, el Agent limpia la activation, despierta al provider y reporta `WINDOWS_LOGON_NOT_CONFIRMED`.

En PC14, el antiguo intento que termino con `Resultado 87` / `ERROR_INVALID_PARAMETER` quedo cerrado por el fix de packing Kerberos. El retest fisico posterior del 2026-09-15 con `operationId=889798b1-2274-4a31-8f1f-c78c4706db83` ejercito `GetSerialization`, `ACQUIRE_PENDING_CREDENTIAL`, `CredProtectW`, `Negotiate` y el pack corregido; Winlogon/Operational termino con `Resultado 0` y la sesion real quedo activa como `ICH11\ICH-PRIMARIA-14`.

El cierre fisico posterior de PC14 valido tambien la frontera final de `ReportResult`: `LOGON_MANAGED_ACCOUNT(PRIMARY)` completo `CP_REPORT_RESULT status=0`, `REPORT_LOGON_RESULT SUCCESS`, sesion real `ICH11\ICH-PRIMARIA-14` y Master `SUCCESS`. Por tanto `WINDOWS_LOGON_NOT_CONFIRMED` y `ReportResult` pendiente quedan solo como antecedentes historicos, no como estado vigente de Paso 19 / 19I2 en PC14.

El Master usa un timeout fijo especifico para logon, mayor al timeout global normal. Si el transporte se pierde y no llega `OperationResult`, el Master conserva `OPERATION_RESULT_UNKNOWN`.

## Limites

Desde Prompt 19G4, `SWITCH_MANAGED_ACCOUNT` reutiliza de este servicio el preflight estructural de target antes de logoff y el logon target completo despues de confirmar `NO_SESSION`. La disponibilidad real de LogonUI/Credential Provider se verifica dentro de `LogonAsync`, despues de `NO_SESSION`; SWITCH no duplica Credential Provider logic ni salta esta proteccion.

19G3 no agrego endpoint HTTP, BatchOperation, fanout, planner, UI, retry automatico, reconciliation, status heartbeat, unlock implicito ni logoff implicito. 19G4 agrega switch como primitive separada y Agent-side, no como cambio al contrato de logon. La validacion E2E real documentada aqui cubre PC14 en Windows 11 Education x64 build 22621; no declara Windows 10, todas las builds de Windows 11, multi-PC, boot storm, UI ni licensing comercial offline final.

No usa Registry autologon, `DefaultPassword`, `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogonW`, SendKeys, UI Automation, PowerShell, `cmd`, scripts, RDP ni APIs WinStation no documentadas.
