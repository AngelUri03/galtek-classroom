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

## Locked, Disconnected Y RDP

Una sesion bloqueada sigue siendo una sesion logueada: `PRIMARY` bloqueado sigue siendo `PRIMARY_ACTIVE`.

Fast User Switching puede dejar sesiones historicas o disconnected. Galtek no enumera todas para elegir una; solo usa la identidad asociada a la consola fisica actual. Sesiones RDP no reemplazan automaticamente esa consola.

## Limites

`GET_WINDOWS_SESSION_STATE` no implementa passwords, credential store, provisioning, login, logoff, switch, Credential Provider, UI, endpoint/batch Master, Local IPC, Session Command, Session Agent dependency, browser policy integration, heartbeat state, polling, WMI, process scans ni writes.

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
