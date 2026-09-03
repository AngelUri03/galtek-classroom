# Input Control

Prompt 18A implements Agent-side `LOCK_INPUT` and `UNLOCK_INPUT` without Master batch endpoint or UI.

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

## Not Implemented In 18A

- Master endpoint or batch dispatch.
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
