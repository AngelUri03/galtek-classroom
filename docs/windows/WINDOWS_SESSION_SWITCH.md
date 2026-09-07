# Windows Session Switch

Prompt 19H1 agrega el batch Master `POST /api/classrooms/{classroomId}/managed-accounts/switch` sobre la primitive remota 19G4. El batch acepta solo `targetAccountId` `PRIMARY|SECONDARY` y `targetDeviceIds` explicitos, se protege con `MasterAccessGuard`, persiste una sola `BatchOperation` antes del primer snapshot, consulta `GET_WINDOWS_SESSION_STATE` por target con operationId propio y solo envia `SWITCH_MANAGED_ACCOUNT(target)` para targets cuyo snapshot permite mutation. Prompt 19H2 agrega `POST /api/operations/{operationId}/retry` para reintentar explicitamente targets `FAILED` retryable de ese mismo batch, sin crear operacion nueva ni cambiar el target account original.

`NO_CHANGE` se persiste cuando el snapshot ya coincide con el target y no envia mutation. `OTHER_SESSION_ACTIVE` se bloquea como `WINDOWS_SESSION_CHANGED`; `UNKNOWN` como `WINDOWS_SESSION_UNKNOWN`. Aunque el plan conceptual sea `LOGON` para `NO_SESSION`, el Master no envia `LOGON_MANAGED_ACCOUNT`: usa siempre `SWITCH_MANAGED_ACCOUNT(target)` porque el Agent vuelve a observar y revalidar ante races.

El retry administrativo tambien exige `MasterAccessGuard`, acepta solo `targetDeviceIds`, reclama transaccionalmente `FAILED -> PENDING` con `attempt + 1`, ejecuta preflight y snapshot frescos y usa operationIds remotos nuevos. `SUCCESS`, `NO_CHANGE`, `PENDING` y `OPERATION_RESULT_UNKNOWN` de switch nunca se reintentan.

Prompt 19G4 implementa `SWITCH_MANAGED_ACCOUNT` como operacion remota tipada para un Client individual. No agrega endpoint HTTP, BatchOperation, fanout, planner, UI ni cambios C++ del Credential Provider.

## Contrato

El request contiene solo:

```text
SwitchManagedAccountOperationParameters {
  ManagedWindowsAccountId account_id
}
```

Valores validos: `PRIMARY` y `SECONDARY`. `UNSPECIFIED` se rechaza. El Master declara unicamente la cuenta objetivo; no envia source account, username, domain, SID, `accountReference`, `sessionId`, password, `credentialId`, vault token, `force`, timeout, comandos, argumentos, shell ni payload arbitrario.

La capability especifica es:

```text
WINDOWS_SESSION_SWITCH_V1
```

No autoriza por si misma. La operacion entra por el `RemoteOperationDispatcher` normal con gRPC/mTLS, Master esperado, trust `PAIRED`, no `REVOKED`, Device correcto, operacion soportada y Commercial License Client `ACTIVE`. `UNLOCK_INPUT` sigue siendo la unica excepcion recovery-safe.

## Semantica

`SWITCH_MANAGED_ACCOUNT(target)` significa dejar la consola fisica en la cuenta administrada objetivo sin tocar sesiones no administradas.

El source se deriva localmente desde `WindowsSessionState` real: solo `PRIMARY_ACTIVE` y `SECONDARY_ACTIVE` pueden ser source. El Master no puede enviarlo y el Agent no lo infiere por username, `accountReference`, PID, foreground window, RDP ni `sessionId`.

Matriz inicial:

- target ya activo: `SUCCESS` idempotente, sin logoff, sin logon y sin DPAPI.
- `NO_SESSION`: reutiliza `LOGON_MANAGED_ACCOUNT(target)`.
- opposite managed activo: preflight target, revalidacion source, `LOGOFF source`, espera `NO_SESSION`, luego `LOGON target`.
- `OTHER_SESSION_ACTIVE`: `WINDOWS_SESSION_CHANGED`, sin tocar la sesion.
- `UNKNOWN`: `WINDOWS_SESSION_UNKNOWN`.

## Target Preflight Antes De Logoff

Antes de cerrar una source administrada, el Agent valida que el target sea estructuralmente posible:

- binding configurado;
- SID valido resoluble como `SidTypeUser`;
- credencial DPAPI usable ligada al mismo SID.

Este preflight no adquiere ni revela password. Si falla, la source no se cierra y se devuelve el error estructurado del target, por ejemplo `ACCOUNT_NOT_CONFIGURED`, `ACCOUNT_NOT_FOUND`, `MANAGED_CREDENTIAL_NOT_CONFIGURED` o `MANAGED_CREDENTIAL_STORE_INVALID`.

## Logoff Source

Despues del preflight, el Agent relee `WindowsSessionState` y exige que siga siendo exactamente el source derivado. Si cambio, devuelve `WINDOWS_SESSION_CHANGED` y no llama logoff.

El tramo destructivo reutiliza `WindowsSessionLogoffService`: binding SID esperado, consola fisica, double-check `sessionId + SID`, `WTSLogoffSession(WTS_CURRENT_SERVER_HANDLE, sessionId, FALSE)`, sin password y sin Session Agent.

`WTSLogoffSession SUCCESS` significa solo que Windows acepto la solicitud asincrona. SWITCH no inicia el logon target hasta confirmar primero `WindowsSessionState == NO_SESSION`.

## Espera De Transicion

La espera post-logoff existe solo mientras una operacion SWITCH explicita esta activa. No hay timer permanente, heartbeat, polling idle, WMI ni writes periodicos.

La implementacion usa un retry loop local acotado con `CancellationToken`, intervalo conservador y deadline corto. Durante la espera:

- `NO_SESSION`: continuar a `LOGON target`.
- source sigue activo: seguir esperando hasta el limite.
- target aparece activo: `SUCCESS` idempotente, sin activation nueva.
- `OTHER_SESSION_ACTIVE` u otra sesion inesperada: `WINDOWS_SESSION_CHANGED`.
- estado no confiable: `WINDOWS_SESSION_UNKNOWN`.
- deadline sin confirmar `NO_SESSION`: `WINDOWS_SWITCH_NOT_CONFIRMED`, sin iniciar logon.

## Logon Target

El tramo de logon reutiliza `WindowsSessionLogonService` de 19G3. Ese servicio revalida inmediatamente `NO_SESSION` antes de crear activation y conserva binding, SID `SidTypeUser`, credential DPAPI usable, listener del provider, activation productiva, auto-submit one-shot, `ReportResult` authority, `WINDOWS_LOGON_FAILED` y `WINDOWS_LOGON_NOT_CONFIRMED`.

La disponibilidad real de LogonUI/Credential Provider se espera aqui, solo despues de confirmar `NO_SESSION`. Si el listener no aparece, `WindowsSessionLogonService` devuelve `CREDENTIAL_PROVIDER_UNAVAILABLE`; esto puede ocurrir despues de que la source ya se cerro y no dispara rollback.

SWITCH no duplica logica de Credential Provider y no requiere cambios C++.

## Efectos Parciales

SWITCH es compuesto. Una vez que Windows acepta el logoff, la source pudo cerrarse aunque despues falle el logon target o la confirmacion.

Errores posteriores posibles incluyen `CREDENTIAL_PROVIDER_UNAVAILABLE`, `WINDOWS_LOGON_FAILED`, `WINDOWS_LOGON_NOT_CONFIRMED`, `WINDOWS_SWITCH_NOT_CONFIRMED`, `WINDOWS_SESSION_CHANGED` y `WINDOWS_SESSION_UNKNOWN`.

No hay rollback automatico a source, no hay retry automatico y no se crea journal/receipt nuevo en 19G4. Si el Master pierde el `OperationResult`, conserva `OPERATION_RESULT_UNKNOWN`; 19H2 no permite reintentar ese error para switch.

## Timeout

El Master usa timeout fijo especifico para `SWITCH_MANAGED_ACCOUNT`, separado del timeout global y del timeout de logon. Cubre la espera post-logoff acotada, el TTL de activation/logon y un margen pequeno.

## Pendiente

19H1 ya integra planner/batch/endpoint para Devices explicitamente seleccionados, con partial success y `NO_CHANGE`. 19H2 ya integra retry administrativo explicito solo de errores realmente retryable. Queda pendiente UI.
