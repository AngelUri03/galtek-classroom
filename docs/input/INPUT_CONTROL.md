# Input Control

Prompt 18A implements Agent-side `LOCK_INPUT` and `UNLOCK_INPUT` without Master batch endpoint or UI.

Prompt 18B1 adds a local read-only Master unlock authorization primitive for future recovery-safe `UNLOCK_INPUT` dispatch. It does not add the Master endpoint, batch operation, gRPC dispatch, UI, overlay or input-control persistence.

## Flow

```text
OperationRequest LOCK_INPUT / UNLOCK_INPUT
  -> Agent Service RemoteOperationDispatcher
  -> typed Session Command v1
  -> GaltekClassroom.Agent.Session
  -> WindowsInputBlockCoordinator
  -> User32.dll BlockInput(BOOL)
```

The Agent Service runs as LocalSystem in Session 0 and never calls `BlockInput`. The Session Agent is the physical authority because it runs inside the interactive Windows session.

## Thread Ownership

Windows requires the thread that successfully calls `BlockInput(TRUE)` to call `BlockInput(FALSE)`. Galtek therefore uses a lazy dedicated worker thread inside the Session Agent:

- normal unlocked idle: no extra input-control thread;
- first `LOCK_INPUT`: create the worker and call `BlockInput(TRUE)`;
- repeated `LOCK_INPUT`: reassert `BlockInput(TRUE)` on the same owner thread;
- `UNLOCK_INPUT`: call `BlockInput(FALSE)` on the same owner thread and terminate the worker;
- Session Agent shutdown: request unlock on the owner thread and wait only for a bounded cleanup.

No input lock state is persisted. A new Session Agent starts logically unlocked.

## Recovery

`CTRL+ALT+DEL` is a native Windows safety escape for `BlockInput`. Galtek does not try to block Secure Attention Sequence, Task Manager, Winlogon, account switching, or physical recovery paths.

If the Agent Service restarts while the Session Agent remains alive, the lock can remain active and a later `UNLOCK_INPUT` can still reach the same Session Agent owner thread. If the Session Agent process or owner thread exits abruptly, Windows releases input.

`LOCK_INPUT` requires Commercial License `ACTIVE`. `UNLOCK_INPUT` is recovery-safe and is not blocked by license state, but it still requires all existing network and local-channel security: mTLS, expected Master identity, paired trust, non-revoked status, registered target Device, known operation type, dispatcher path, and authenticated LocalSystem-to-Session Command.

## Master Unlock Authorization

For the future Master-originated `UNLOCK_INPUT`, the Master Backend must ask the local Agent Service through Local IPC v1:

```text
GET_MASTER_UNLOCK_AUTHORIZATION
```

The Agent Service remains the only local authority. It authorizes unlock recovery only when all of these are true:

- local Installation Identity is valid;
- `master-binding.json` exists and is structurally valid;
- `binding.installationId` matches the current Installation Identity;
- the real Windows SID of the Named Pipe caller is obtained through impersonation/API Windows;
- that real SID exactly matches the bound SID.

This check does not require local Master Commercial License `ACTIVE` and does not read claims/roles from an invalid or tampered license. It also does not trust a SID declared in JSON, username/display name, HTTP headers, request parameters or membership in Builtin Administrators.

The response is intentionally minimal:

```json
{
  "status": "AUTHORIZED",
  "authorized": true,
  "configured": true
}
```

It must not expose SID, JWT, `LicenseState`, raw claims, roles, full `installationId`, binding path, ACLs, username or key material. Any uncertainty about identity, binding, SID resolution, impersonation or IPC caller fails closed with `authorized=false`.

In Java, `MasterUnlockAccessGuard.requireUnlockAuthorized()` is separate from `MasterAccessGuard.requireAuthorized()`. It must only protect actions that reduce control and have been explicitly declared recovery-safe. Currently that is only the future `UNLOCK_INPUT`. There is no fallback between guards.

## Not Implemented Through 18B1

- Master endpoint or batch dispatch.
- BatchOperation `LOCK_INPUT` or `UNLOCK_INPUT`.
- Master gRPC dispatch for input control.
- UI or overlay.
- Message on screen.
- Keyboard-only or mouse-only modes.
- Duration, lease, timeout, automatic unlock timer or persistent lock.
- Global hooks, keyboard filters, drivers, Raw Input interception, `SendInput`, `SendKeys`, shell, PowerShell, `cmd`, WMI or process suspension.
- Blocking `CTRL+ALT+DEL` or Task Manager.

## Manual Validation Pending

Run only on a disposable PC:

- `LOCK_INPUT`: keyboard and mouse stop reaching normal applications.
- `UNLOCK_INPUT`: keyboard and mouse input returns.
- `LOCK_INPUT`, restart Agent Service, then `UNLOCK_INPUT`: unlock still works because Session Agent kept the owner thread.
- `LOCK_INPUT`, terminate Session Agent: Windows releases input.
- `LOCK_INPUT`, press `CTRL+ALT+DEL`: Windows releases input.
- After `CTRL+ALT+DEL`, send `LOCK_INPUT` again: same owner thread reasserts the lock.
- With expired Client license: `LOCK_INPUT` is rejected by license gate, `UNLOCK_INPUT` still reaches the handler.
