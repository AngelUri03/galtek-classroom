# Windows Session State

Prompt 19C implementa `GET_WINDOWS_SESSION_STATE` productivo del lado Client como operacion remota read-only y on-demand.

## Autoridad

La autoridad inicial es la consola fisica de la PC:

```text
WTSGetActiveConsoleSessionId()
```

No se selecciona sesion mediante procesos, `explorer.exe`, username, foreground window, Winlogon, WMI, Registry, `C:\Users`, RDP ni presencia del Session Agent.

`0xFFFFFFFF` de `WTSGetActiveConsoleSessionId()` es una condicion transitoria attach/detach y se reporta como `UNKNOWN`, no como `NO_SESSION`. Session 0 pertenece al Service y tambien se reporta como `UNKNOWN`.

## Presencia E Identidad

`WTSUserName` puede consultarse solo como senal auxiliar de presencia. Si esta vacio, Galtek clasifica `NO_SESSION`.

Ese username nunca se usa para mapear `PRIMARY` o `SECONDARY`, nunca se compara con `accountReference`, nunca se persiste y nunca sale en el resultado remoto.

Cuando hay usuario presente, el Agent Service obtiene el token de esa sesion con:

```text
WTSQueryUserToken(sessionId)
```

El Service debe correr como LocalSystem y habilitar `SeTcbPrivilege` de forma acotada para esa consulta. El token se usa solo para leer `TokenUser` con `GetTokenInformation`, convertir el SID con API Windows soportada y cerrar/liberar los handles o buffers nativos.

## Mapping

El estado remoto contiene solo:

```text
WindowsSessionStateResult.state
```

Estados:

- `NO_SESSION`: no hay usuario logueado en la consola fisica.
- `PRIMARY_ACTIVE`: el SID activo coincide con `PRIMARY.windowsSid`.
- `SECONDARY_ACTIVE`: el SID activo coincide con `SECONDARY.windowsSid`.
- `OTHER_SESSION_ACTIVE`: hay usuario real pero su SID no coincide con ningun binding, o no hay bindings configurados.
- `UNKNOWN`: no hay observacion confiable pero tampoco una falla cerrada de catalogo.

Si `managed-windows-accounts.json` existe pero es invalido, corrupto o de otra instalacion, la operacion falla como `MANAGED_ACCOUNT_BINDINGS_INVALID` y no intenta clasificacion parcial.

## Privacidad

El resultado remoto no contiene SID, username, domain, `accountReference`, `sessionId`, token handle, path de perfil ni passwords. El Master solo necesita el significado logico `PRIMARY`/`SECONDARY`.

## Relacion Con Credenciales

Desde Prompt 19D, `READY` de una cuenta administrada depende tambien de una credencial DPAPI usable en `managed-windows-credentials.dat`, ligada al mismo SID del binding. Desde 19E1 esa credencial puede cargarse por `PROVISION_MANAGED_CREDENTIAL`. `GET_WINDOWS_SESSION_STATE` no consulta ni descifra ese credential store: sigue observando solo la sesion de consola actual y clasificandola por SID contra `managed-windows-accounts.json`.

## Relacion Con Logoff

Desde Prompt 19F, `LOGOFF_WINDOWS_SESSION(accountId)` reutiliza la misma autoridad de consola fisica y SID real, pero como operacion destructiva expected-account. El Agent Service exige que el binding local del `accountId` solicitado exista y que el SID activo coincida con ese binding.

La operacion observa la consola, valida SID, vuelve a observar inmediatamente antes de cerrar, exige el mismo `sessionId` y SID, y solo entonces llama `WTSLogoffSession` con `bWait = FALSE`. Si entre ambas observaciones cambia el `sessionId` o el SID, no cierra la nueva sesion.

`NO_SESSION` se considera `SUCCESS` idempotente porque la sesion esperada ya esta ausente. `OTHER_SESSION_ACTIVE` o cualquier SID distinto devuelve `WINDOWS_SESSION_CHANGED`. Estado no confiable devuelve `WINDOWS_SESSION_UNKNOWN`.

El resultado remoto de logoff no contiene SID, username, domain, `accountReference`, `sessionId` ni token. `SUCCESS` significa solicitud WTS aceptada, no cierre confirmado; una consulta futura explicita de `GET_WINDOWS_SESSION_STATE` puede observar el resultado.

## Relacion Con Logon

Desde Prompt 19G3, `LOGON_MANAGED_ACCOUNT(accountId)` reutiliza este estado como preflight, no como mecanismo de autenticacion. Si el target ya esta activo, devuelve `SUCCESS` idempotente sin crear activation ni consultar DPAPI. Si el estado es `NO_SESSION`, el Agent puede continuar con preflight de binding/credential y luego revalida inmediatamente que siga en `NO_SESSION` antes de activation.

Si aparece cualquier otra sesion activa, incluso otro managed account, el logon remoto devuelve `WINDOWS_SESSION_CHANGED` y no intenta unlock, switch ni logoff. Si la consola no es confiable, devuelve `WINDOWS_SESSION_UNKNOWN`.

La operation result de logon tampoco contiene SID, username, domain, `accountReference`, `sessionId` ni token. `SUCCESS` significa que Winlogon/LSA acepto la autenticacion reportada por `ReportResult(STATUS_SUCCESS)`, no que el escritorio ya este listo.

## Relacion Con Credential Provider

Desde Prompt 19G3, `Credential Provider V2` ejecuta el tramo de serialization/autosubmit de `LOGON_MANAGED_ACCOUNT`, pero `GET_WINDOWS_SESSION_STATE` no depende del provider y el provider no lee el estado de sesion por su cuenta. El Agent Service conserva la autoridad local.

La activation metadata del provider es efimera y no cambia el resultado de `GET_WINDOWS_SESSION_STATE`: no es una sesion, no es proof de login y no contiene SID ni password.

## Locked, Disconnected Y RDP

Una sesion bloqueada sigue siendo una sesion logueada: `PRIMARY` bloqueado sigue siendo `PRIMARY_ACTIVE`.

Fast User Switching puede dejar sesiones historicas o disconnected. Galtek no enumera todas para elegir una; solo usa la identidad asociada a la consola fisica actual. Sesiones RDP no reemplazan automaticamente esa consola.

## Limites

`GET_WINDOWS_SESSION_STATE` no implementa provisioning, login, logoff, switch, Credential Provider, UI, endpoint/batch Master, Local IPC, Session Command, Session Agent dependency, browser policy integration, heartbeat state, polling, WMI, process scans ni writes, y no descifra passwords.

`LOGOFF_WINDOWS_SESSION` no implementa login, switch, Credential Provider usage, endpoint/batch Master, fanout, planner, UI, Session Agent, password usage, DPAPI, force flag, configurable timeout, polling, status heartbeat ni reconciliation.

`LOGON_MANAGED_ACCOUNT` no implementa switch, logoff implicito, unlock de sesion existente, endpoint/batch Master, fanout, planner, UI, configurable timeout desde request, retry automatico, status heartbeat ni reconciliation.

## Validacion Manual Pendiente

En una PC descartable:

1. Sin usuario logueado: `NO_SESSION`.
2. `PRIMARY` logueado: `PRIMARY_ACTIVE`.
3. Windows bloqueado con `PRIMARY`: sigue `PRIMARY_ACTIVE`.
4. Cambio manual a `SECONDARY`: `SECONDARY_ACTIVE`.
5. Usuario administrador u otra cuenta: `OTHER_SESSION_ACTIVE`.
6. Renombrar `PRIMARY` conservando SID: sigue `PRIMARY_ACTIVE`.
7. Sesion historica/disconnected de `PRIMARY` y consola actual `SECONDARY`: `SECONDARY_ACTIVE`.
8. Session Agent detenido: `GET_WINDOWS_SESSION_STATE` sigue funcionando.
