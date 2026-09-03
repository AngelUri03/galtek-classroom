# Input Control

Prompt 18A implements Agent-side `LOCK_INPUT` and `UNLOCK_INPUT` without Master batch endpoint or UI.

Prompt 18B1 adds a local read-only Master unlock authorization primitive for future recovery-safe `UNLOCK_INPUT` dispatch. It does not add the Master endpoint, batch operation, gRPC dispatch, UI, overlay or input-control persistence.

Prompt 18B2 adds the Master batch dispatch endpoints:

```text
POST /api/classrooms/{classroomId}/input-control/lock
POST /api/classrooms/{classroomId}/input-control/unlock
```

There is no generic `/input-control` endpoint with a request-controlled `type`.

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

In Java, `MasterUnlockAccessGuard.requireUnlockAuthorized()` is separate from `MasterAccessGuard.requireAuthorized()`. It must only protect actions that reduce control and have been explicitly declared recovery-safe. Currently that is only `UNLOCK_INPUT`. There is no fallback between guards.

The Master endpoints keep the split structural:

- `/input-control/lock` calls `MasterAccessGuard.requireAuthorized()` before reading Classroom, Devices, bindings, trust or SQLite school data.
- `/input-control/unlock` calls `MasterUnlockAccessGuard.requireUnlockAuthorized()` before reading Classroom, Devices, bindings, trust or SQLite school data.
- Passing `MasterUnlockAccessGuard` only permits `UNLOCK_INPUT`; it does not authorize lock, power control, URL/application launch, browser policy apply, CRUD, assignments or registration.

## Master Batch Dispatch

Both endpoints accept only:

```json
{
  "targetDeviceIds": [
    "device-1",
    "device-2"
  ]
}
```

The body is required. `targetDeviceIds` is required, non-empty, non-blank and duplicate-free. Extra fields are rejected, including `type`, `duration`, `timeout`, `message`, `keyboardOnly`, `mouseOnly`, `command`, `arguments`, `shell`, `accountType`, `studentId`, `groupId` and `payload`.

Preflight is per target and batch-friendly:

- Device exists and belongs to the Classroom.
- Current network binding exists.
- Network Identity matches the binding.
- Trust is `PAIRED` and not `REVOKED`.
- Authenticated gRPC/mTLS connection is `ONLINE`.
- Capability `INPUT_CONTROL_V1` is present.

The Master does not require `SESSION_AGENT_AVAILABLE` in preflight and does not evaluate the Client Commercial License. Agent-side 18A preserves the productive rules: `LOCK_INPUT` requires active Client license; `UNLOCK_INPUT` remains recovery-safe.

Each request creates one `BatchOperation` before fanout. Ready targets start as `PENDING`; preflight failures are persisted as `FAILED`. The same batch `operationId` is sent to all ready Agents using `OperationType.LOCK_INPUT` or `OperationType.UNLOCK_INPUT` with no functional Protobuf parameters. The persisted payload is minimal (`schemaVersion = 1`) and does not store duration, reason, message, SID, user, thread, session or current input state.

`OperationAccepted ACCEPTED` is not success. Only `OperationResult SUCCESS` marks a target `SUCCESS`. Agent errors such as `INPUT_LOCK_FAILED`, `INPUT_UNLOCK_FAILED`, `SESSION_AGENT_UNAVAILABLE`, `SESSION_COMMAND_RESULT_UNKNOWN`, `OPERATION_REJECTED` and `OPERATION_RESULT_UNKNOWN` are preserved per target.

## Not Implemented Through 18B2

- UI or overlay.
- Message on screen.
- Keyboard-only or mouse-only modes.
- Duration, lease, timeout, automatic unlock timer or persistent lock.
- Lock status endpoint, lock heartbeat, persisted current lock state or status query.
- Automatic retry, resend, reconciliation or auto-unlock scheduler.
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
