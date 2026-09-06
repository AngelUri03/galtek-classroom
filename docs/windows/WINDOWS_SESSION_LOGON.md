# Windows Session Logon

Prompt 19G3 implementa `LOGON_MANAGED_ACCOUNT` remoto para un Client individual usando Credential Provider V2 y el flujo soportado de Windows Winlogon/LSA.

## Contrato Remoto

El request Protobuf contiene solo:

```text
LogonManagedAccountOperationParameters {
  ManagedWindowsAccountId account_id
}
```

`account_id` debe ser `PRIMARY` o `SECONDARY`; `UNSPECIFIED` se rechaza. El Master no envia password, username, domain, SID, `accountReference`, sessionId, credentialId, vault token, command, args, shell, timeout configurable ni payload arbitrario.

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

El Agent Service mantiene un generation counter in-memory. Crear, consumir, completar, expirar o limpiar activation incrementa generation y despierta waiters.

Mientras el Credential Provider esta en `CPUS_LOGON` y `Advise` activo, un worker cancellable espera `WAIT_FOR_ACTIVATION_CHANGE`, usa COM marshaling inter-thread para `ICredentialProviderEvents` y llama `CredentialsChanged` cuando cambia la generation.

Antes de activation, el Service espera brevemente que exista al menos un listener LogonUI validado. Si no existe, devuelve `CREDENTIAL_PROVIDER_UNAVAILABLE`.

## Auto-Submit Y Serialization

Con activation remota:

- `GetCredentialCount` devuelve una credential;
- default credential es `0`;
- `pbAutoLogonWithDefault = TRUE`;
- `SetSelected` solicita auto-logon exactly once.

`GetSerialization` adquiere la password una sola vez por `ACQUIRE_PENDING_CREDENTIAL(activationId)`, usa `CredProtectW`, construye `KERB_INTERACTIVE_UNLOCK_LOGON`, resuelve `Negotiate` y entrega la serialization a Windows. Si falla despues de acquire, reporta `LOCAL_SERIALIZATION_FAILED`, no restaura activation y no reintenta.

## Resultado

`REPORT_LOGON_RESULT` acepta solo:

- `SUCCESS`;
- `FAILED`;
- `LOCAL_SERIALIZATION_FAILED`.

No transporta mensajes, NTSTATUS textual, SID, username, domain, sessionId ni password.

`OperationResult SUCCESS` solo ocurre si `ReportResult` recibe `STATUS_SUCCESS` y reporta `SUCCESS`. Rechazo de autenticacion o fallo local produce `WINDOWS_LOGON_FAILED`. Si no llega confirmacion antes del timeout, el Agent limpia la activation, despierta al provider y reporta `WINDOWS_LOGON_NOT_CONFIRMED`.

El Master usa un timeout fijo especifico para logon, mayor al timeout global normal. Si el transporte se pierde y no llega `OperationResult`, el Master conserva `OPERATION_RESULT_UNKNOWN`.

## Limites

Desde Prompt 19G4, `SWITCH_MANAGED_ACCOUNT` reutiliza de este servicio el preflight estructural de target antes de logoff y el logon target completo despues de confirmar `NO_SESSION`. La disponibilidad real de LogonUI/Credential Provider se verifica dentro de `LogonAsync`, despues de `NO_SESSION`; SWITCH no duplica Credential Provider logic ni salta esta proteccion.

19G3 no agrego endpoint HTTP, BatchOperation, fanout, planner, UI, retry automatico, reconciliation, status heartbeat, unlock implicito ni logoff implicito. 19G4 agrega switch como primitive separada y Agent-side, no como cambio al contrato de logon.

No usa Registry autologon, `DefaultPassword`, `LogonUser`, `CreateProcessAsUser`, `CreateProcessWithLogonW`, SendKeys, UI Automation, PowerShell, `cmd`, scripts, RDP ni APIs WinStation no documentadas.
