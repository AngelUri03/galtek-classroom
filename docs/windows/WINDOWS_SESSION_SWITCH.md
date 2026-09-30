# Windows Session Switch

Contrato vigente: source y target pueden ser `PRIMARY`, `SECONDARY` o `ADMIN`; esto también aplica a la metadata no secreta de observabilidad. Las referencias históricas a solo dos roles conservan la evolución del contrato, no el estado actual.

## 20F.2-F1 - frontera post-logout y resultado parcial

La evidencia `ADMIN_ACTIVE -> PRIMARY` de 0.0.5 confirma logout del source y ausencia de login target. El root cause reproducido es una carrera independiente del bug ADMIN: WTS puede confirmar `NO_SESSION` antes de que el listener Credential Provider de LogonUI este observable; el logon reutilizaba la espera normal de 750 ms y podia devolver `CREDENTIAL_PROVIDER_UNAVAILABLE`.

El switch conserva su timeout total. Tras `SWITCH_NO_SESSION_CONFIRMED`, y solo en esta transicion post-logout, `WindowsSessionLogonService` espera hasta 8 s mediante la senal de disponibilidad existente y despues envia la activacion una sola vez. No hay sleep fijo, polling infinito, doble dispatch, retry automatico ni rollback de source.

Checkpoints sanitizados: `SWITCH_TARGET_PREFLIGHT_OK`, `SWITCH_SOURCE_LOGOFF_ACCEPTED`, `SWITCH_NO_SESSION_CONFIRMED`, `SWITCH_TARGET_LOGON_BEGIN`, `CP_ACTIVATION_SENT`, `CP_REPORT_RESULT`, `SWITCH_TARGET_LOGON_FAILED` y `SWITCH_COMPLETE`. Si source cerro y target falla, el resultado es `PARTIAL`; si el resultado no puede confirmarse es `UNKNOWN`.

## 20F.2

PRIMARY, SECONDARY y ADMIN son targets válidos. Target ADMIN exige step-up Master-side; `ADMIN_ACTIVE` es source managed válido para PRIMARY/SECONDARY y `NO_CHANGE` para ADMIN. El Agent usa el mismo preflight/revalidation/WTS logoff/wait-NO_SESSION/logon y no hace rollback automático. `OTHER_SESSION_ACTIVE` y `UNKNOWN` permanecen protegidos.

Prompt 19H1 agrega el batch Master `POST /api/classrooms/{classroomId}/managed-accounts/switch` sobre la primitive remota 19G4. 20F.2 FINAL amplía `targetAccountId` a `PRIMARY|SECONDARY|ADMIN`, manteniendo targets explícitos, `MasterAccessGuard`, una sola `BatchOperation`, snapshot fresco y dispatch únicamente a targets válidos. Prompt 19H2 agrega retry explícito; reintentar ADMIN requiere una nueva autorización exact-target.

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

Valores válidos: `PRIMARY`, `SECONDARY` y `ADMIN`. `UNSPECIFIED` se rechaza. El Master declara únicamente la cuenta objetivo; no envía source account, username, domain, SID, `accountReference`, `sessionId`, password, `credentialId`, master password, `force`, timeout, comandos, argumentos, shell ni payload arbitrario.

La capability especifica es:

```text
WINDOWS_SESSION_SWITCH_V1
```

No autoriza por si misma. La operacion entra por el `RemoteOperationDispatcher` normal con gRPC/mTLS, Master esperado, trust `PAIRED`, no `REVOKED`, Device correcto, operacion soportada y Commercial License Client `ACTIVE`. `UNLOCK_INPUT` sigue siendo la unica excepcion recovery-safe.

## Semantica

`SWITCH_MANAGED_ACCOUNT(target)` significa dejar la consola fisica en la cuenta administrada objetivo sin tocar sesiones no administradas.

El source se deriva localmente desde `WindowsSessionState` real: `PRIMARY_ACTIVE`, `SECONDARY_ACTIVE` y `ADMIN_ACTIVE` pueden ser source. El Master no puede enviarlo y el Agent no lo infiere por username, `accountReference`, PID, foreground window, RDP ni `sessionId`.

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

La implementacion usa un retry loop local acotado con `CancellationToken`, intervalo conservador de 300ms y deadline monotono de 24s. El presupuesto se deriva de evidencia fisica PC14: Windows tardo aproximadamente 15-18s en completar un logout real de `PRIMARY` y dejar LogonUI utilizable en un retest previo; 24s conserva margen para PCs legacy lentas sin acercarse a minutos y sigue cabiendo bajo el timeout Master SWITCH de 75s junto con el logon target interno.

Durante la espera:

- `NO_SESSION`: continuar a `LOGON target`.
- source sigue activo: seguir esperando hasta el limite.
- target aparece activo: `SUCCESS` idempotente, sin activation nueva.
- `OTHER_SESSION_ACTIVE` u otra sesion inesperada: `WINDOWS_SESSION_CHANGED`.
- `UNKNOWN` transitorio: seguir esperando hasta que aparezca `NO_SESSION`, target activo, otra sesion real o deadline.
- deadline sin confirmar `NO_SESSION`: `WINDOWS_SWITCH_NOT_CONFIRMED`, sin iniciar logon.

La observabilidad no secreta de este tramo usa logs solo por cambio de estado o resultado: `WINDOWS_SWITCH_LOGOFF_ACCEPTED source=<PRIMARY|SECONDARY> target=<PRIMARY|SECONDARY>`, `WINDOWS_SWITCH_WAIT_STATE state=<...> elapsedMs=<n>`, `WINDOWS_SWITCH_NO_SESSION_CONFIRMED elapsedMs=<n>` y `WINDOWS_SWITCH_WAIT_TIMEOUT elapsedMs=<n>`. No registra SID, username, domain ni credenciales.

Antecedente cerrado: el fallo fisico PC14 previo del 2026-09-15 registro `WTSLogoffSession` aceptado a las 21:55:30 para `PRIMARY -> SECONDARY`, cierre real de `PRIMARY` unos 15-18s despues, LogonUI/Credential Provider posterior y `WINDOWS_SWITCH_NOT_CONFIRMED` por espera post-logoff insuficiente. Esa evidencia justifico `PostLogoffWait=24s`.

El retest fisico final de PC14 valido E2E `SWITCH_MANAGED_ACCOUNT(PRIMARY -> SECONDARY)` con `operationId=d90e01e9-8cda-401a-b7ad-9df7f8c3d11c`, `targetAccountId=SECONDARY`, target `deviceId=2966678f-0f07-43ad-936f-8fcd8fc308dd`, `summary total=1 noChange=0 success=1 failed=0` y Master `SUCCESS`. La timeline fue `WINDOWS_SWITCH_LOGOFF_ACCEPTED source=PRIMARY target=SECONDARY`, `WINDOWS_SWITCH_WAIT_STATE state=PrimaryActive elapsedMs=0`, `WINDOWS_SWITCH_WAIT_STATE state=Unknown elapsedMs=6505`, `WINDOWS_SWITCH_WAIT_STATE state=NoSession elapsedMs=6886`, `WINDOWS_SWITCH_NO_SESSION_CONFIRMED elapsedMs=6886`, `WAIT_LISTENER_ENTERED accountId=SECONDARY timeoutMs=750`, `LISTENER_AVAILABILITY_SATISFIED:active_wait accountId=SECONDARY`, `GET_PENDING_ACTIVATION_IDENTITY SUCCESS`, `ACQUIRE_PENDING_CREDENTIAL payload=binary` y `REPORT_LOGON_RESULT SUCCESS`.

Windows dejo activa la consola como `IHTEC-SECUNDARIA-14`; `query user` confirmo `ihtec-secundaria-14`, `console`, `Activo`. Una PowerShell administrativa elevada bajo `ADMIN-14` puede mostrar `whoami=ich11\admin-14`, pero esa identidad pertenece al proceso elevado y no reemplaza la autoridad WTS/session state de consola.

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

## Pendiente Y Limites

19H1 ya integra planner/batch/endpoint para Devices explicitamente seleccionados, con partial success y `NO_CHANGE`. 19H2 ya integra retry administrativo explicito solo de errores realmente retryable. La validacion E2E real de switch cubre PC14 en Windows 11 Education x64 build 22621.

Quedan fuera de esta validacion: Windows 10, todas las builds de Windows 11, escenarios multi-PC, boot storm, overwrite de installer sobre `appsettings.json` / `MasterConnection`, cold boot SCM 7000/7009, credential out-of-sync por cambio externo de password, UI y Commercial licensing offline Hub final.
