# Windows Session Logoff

Prompt 19F implementa `LOGOFF_WINDOWS_SESSION` como operacion remota tipada, Agent-side y destructiva para cerrar solo una sesion Windows administrada esperada.

## Contrato

El request usa:

```text
LogoffWindowsSessionOperationParameters {
  ManagedWindowsAccountId account_id
}
```

Valores validos: `PRIMARY` y `SECONDARY`. `UNSPECIFIED` se rechaza.

El contrato no transporta username, domain, SID, `accountReference`, password, sessionId, PID, force, timeout, command, args, shell ni payload arbitrario.

## Expected Account

La operacion significa:

```text
cerrar la sesion fisica actual solo si todavia pertenece al managed account indicado
```

`LOGOFF_WINDOWS_SESSION(PRIMARY)` nunca cierra `SECONDARY`, un administrador ni un usuario no administrado.

Debe existir binding local en `managed-windows-accounts.json` para el slot solicitado. Sin binding devuelve `ACCOUNT_NOT_CONFIGURED`. Binding corrupto, schema invalido o `installationId` mismatch devuelve `MANAGED_ACCOUNT_BINDINGS_INVALID`.

No se requiere password y no se consulta `managed-windows-credentials.dat`, DPAPI, Credential Vault ni Credential Provider.

## Autoridad De Consola

La autoridad es:

```text
WTSGetActiveConsoleSessionId()
```

No se elige sesion por RDP, `explorer.exe`, foreground process, username, WMI, Registry, `C:\Users` ni Session Agent.

La identidad se obtiene desde:

```text
WTSQueryUserToken(sessionId)
GetTokenInformation(TokenUser)
SID real
```

La comparacion es exclusivamente:

```text
binding.windowsSid == active TokenUser SID
```

Si una cuenta fue borrada pero la sesion viva todavia tiene el SID ligado, logoff puede cerrarla. No se exige que `LookupAccountSid` sea exitoso despues de demostrar que el token activo coincide con el binding.

## Race Protection

Antes de llamar a Windows:

1. Leer `WTSGetActiveConsoleSessionId()`.
2. Obtener SID real del token activo.
3. Comparar con el binding esperado.
4. Volver a leer consola inmediatamente antes del logoff.
5. Exigir el mismo `sessionId`.
6. Volver a obtener y comparar el SID.
7. Solo entonces llamar `WTSLogoffSession`.

Si cambia `sessionId` o SID, devuelve `WINDOWS_SESSION_CHANGED` y no cierra la nueva sesion. Si la consola queda no confiable, devuelve `WINDOWS_SESSION_UNKNOWN`.

## WTS Logoff

La implementacion productiva usa:

```text
WTSLogoffSession(WTS_CURRENT_SERVER_HANDLE, sessionId, FALSE)
```

`bWait = FALSE` evita bloquear workers del Agent esperando cierre de aplicaciones o Windows. `SUCCESS` significa que Windows acepto la solicitud asincrona, no que la sesion ya desaparecio.

No hay polling posterior, timer, loop, sleep ni heartbeat field. Una consulta futura explicita `GET_WINDOWS_SESSION_STATE` puede observar el resultado.

## Estados

- `NO_SESSION`: `SUCCESS` idempotente, la sesion esperada ya esta ausente.
- SID activo igual al binding esperado: intentar logoff.
- Otro SID activo: `WINDOWS_SESSION_CHANGED`, sin logoff.
- Estado de consola no confiable: `WINDOWS_SESSION_UNKNOWN`, sin logoff.
- `WTSLogoffSession` devuelve false: `WINDOWS_LOGOFF_FAILED`.

El resultado remoto no contiene SID, username, domain, `accountReference`, sessionId, PID ni token.

## Dedupe E Incertidumbre

Mismo `operationId + accountId` devuelve el resultado cacheado y no vuelve a llamar `WTSLogoffSession`.

Mismo `operationId` con otro `accountId` conserva el conflicto de dedupe vigente.

Si el Master envio la operacion y no recibe `OperationResult`, el resultado es `OPERATION_RESULT_UNKNOWN`. No hay retry automatico: un retry tardio podria encontrar una nueva sesion del mismo accountId y cerrarla.

19F no extiende `OperationStatusQuery`, receipts ni reconciliation framework.

## Trabajo No Guardado

`LOGOFF_WINDOWS_SESSION` es destructiva respecto a la sesion interactiva. Puede cerrar aplicaciones y provocar perdida de trabajo no guardado segun el comportamiento de Windows y de cada aplicacion.

Por eso no se ejecuta automaticamente por assignment, startup ni recovery. Requiere accion administrativa explicita futura y la UI debera advertir cuando corresponda. 19F no agrega `force=true` ni bypass de aplicaciones.

## Limites 19F

No implementa endpoint HTTP, BatchOperation, planner Master, fanout, `LOGON_MANAGED_ACCOUNT`, `SWITCH_MANAGED_ACCOUNT`, uso de Credential Provider, uso de password, DPAPI, vault, Session Agent, RDP control, shell, PowerShell, `cmd`, `logoff.exe`, UI ni polling.

Prompt 19G1 agrega una foundation separada de Credential Provider V2 para login futuro. Esa foundation no cambia `LOGOFF_WINDOWS_SESSION`: logoff sigue sin requerir password, sin provider y sin consultar `managed-windows-credentials.dat`.

## Pendiente

19G2 ya completo credential acquisition one-time y serialization local para una activation existente.

19G3 ya implemento `LOGON_MANAGED_ACCOUNT` remoto individual.

19G4 queda pendiente para `SWITCH_MANAGED_ACCOUNT`.

19H queda pendiente para dispatch/planner batch Master.
