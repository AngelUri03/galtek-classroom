# Local Session Command v1

## Purpose

Local Session Command v1 is the dedicated local channel used by `GaltekClassroom.Agent.Service` to send explicit interactive-session commands to `GaltekClassroom.Agent.Session`.

Prompt 16B implements `OPEN_URL` as the first visible interactive-session action. Prompt 17B implements `OPEN_APPLICATION(applicationId)` for authorized local applications. The Agent Service never opens visible UI from Session 0; it sends typed commands to the Session Agent, and the Session Agent performs the interactive-session action.

## Separation from Local IPC v1

`GaltekClassroom.Agent.v1` remains the read-only Local IPC API. Its allowed operations stay:

- `PING`
- `GET_DEVICE_STATUS`
- `GET_MACHINE_CODE`
- `GET_MASTER_AUTHORIZATION`
- `GET_RUNTIME_DIAGNOSTICS`

Session Command v1 is a separate privileged channel. It does not replace Local IPC v1.

Responsibilities:

- Local IPC v1: local read-only queries to the Agent Service.
- Session Command v1: authorized Service-to-Session requests for future typed interactive actions.

## Pipe per Session

The Session Agent is the pipe server. The Agent Service is the client.

Each interactive Session Agent derives its pipe name from its real `Process.SessionId`:

```text
GaltekClassroom.Agent.SessionCommand.v1.<sessionId>
```

Example:

```text
GaltekClassroom.Agent.SessionCommand.v1.3
```

`sessionId` is never accepted from a user, file, Master payload or command payload. `SessionId = 0` is invalid for background Session Agent startup.

## ACL

The command pipe is privileged.

The pipe ACL allows only LocalSystem (`S-1-5-18`) as client with read/write rights needed for the exchange. It must not grant access to `Users`, `Authenticated Users` or `Everyone`.

The pipe name is not a security boundary.

## Bilateral Authentication

The Session Agent authenticates the client after accepting a connection by reading the real Named Pipe client identity with supported Windows APIs (`RunAsClient` plus `WindowsIdentity.GetCurrent`, or equivalent). The only authorized caller SID is LocalSystem (`S-1-5-18`).

The Agent Service authenticates the server before sending a command. After connecting it obtains the real Named Pipe server PID with `GetNamedPipeServerProcessId` or equivalent and validates:

- the process exists;
- `Process.SessionId` matches the resolved interactive session id;
- the executable path is the normalized productive Session Agent path:

```text
<ProgramFiles>\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe
```

If any validation fails, the Service does not send the command.

## Framing

Messages use simple framing:

```text
4-byte BIG ENDIAN length
+
JSON UTF-8
```

Maximum JSON payload length: 16 KiB.

Messages above the limit are rejected. Newline delimiters, `BinaryFormatter` and opaque binary serialization are not used.

## Request

```json
{
  "protocolVersion": 1,
  "requestId": "uuid",
  "commandType": "CHANNEL_PING"
}
```

`requestId` is mandatory and must be a UUID. The response must return exactly the same value.

There is no generic payload, command string or arguments field.

For `OPEN_URL`, the request uses an explicit typed field:

```json
{
  "protocolVersion": 1,
  "requestId": "uuid",
  "commandType": "OPEN_URL",
  "openUrl": {
    "operationId": "uuid",
    "url": "https://example.test/activity"
  }
}
```

`operationId` is copied from the remote `OperationRequest`. The URL is validated by the Agent Service before sending and validated again by the Session Agent before launching.

For `OPEN_APPLICATION`, the request uses an explicit typed field:

```json
{
  "protocolVersion": 1,
  "requestId": "uuid",
  "commandType": "OPEN_APPLICATION",
  "openApplication": {
    "applicationId": "conejito-lector"
  }
}
```

The Service-to-Session command carries only `applicationId`. It never carries an executable path, command line, arguments, working directory, shell verb, shortcut, URI, environment, `runas` flag or generic payload. The Session Agent reads and validates `application-bindings.json` again before resolving a local executable.

## Response

```json
{
  "protocolVersion": 1,
  "requestId": "uuid",
  "status": "SUCCESS",
  "errorCode": null,
  "message": null
}
```

If `protocolVersion != 1`, the request is rejected with a structured error. There is no automatic fallback.

If the response `requestId` differs from the request, the Service rejects the response as invalid.

## Commands

Implemented:

- `CHANNEL_PING`
- `OPEN_URL`
- `OPEN_APPLICATION`

`CHANNEL_PING` proves the authenticated channel works. It does not open windows, launch processes, open a browser, read files, write registry, change browser policy, switch sessions or show UI.

`OPEN_URL` accepts only absolute `http://` or `https://` URLs with non-empty host, no control characters, no CR/LF, no embedded username/password and a maximum length of 4096 characters. It rejects `file:`, `javascript:`, `data:`, `ftp:`, `shell:`, `ms-*` handlers, UNC/local paths, drive paths such as `C:\...` and relative URLs. It does not block `localhost`, private IP addresses or LAN hostnames.

`OPEN_URL` launches through the Windows Shell API with verb `open`, file set to the validated URL and no parameters. It does not execute `cmd.exe`, PowerShell, `explorer.exe` with arbitrary arguments, a browser path from Master or any command line constructed from remote input.

`OPEN_URL SUCCESS` means Windows accepted the launch request for the registered HTTP/HTTPS handler. It does not mean DNS resolved, Internet works, a page loaded, HTTP returned 200 or a browser rendered content correctly.

`OPEN_APPLICATION` accepts only a valid `applicationId`. The Session Agent reads `<CommonApplicationData>\Galtek\Classroom\application-bindings.json` on demand, validates the full catalog, requires an exact enabled binding, resolves `ABSOLUTE_EXE` or HKLM-only `APP_PATHS`, and launches only the final absolute local `.exe` path.

For `APP_PATHS`, only the default value under HKLM App Paths is used, checking Registry64 and Registry32 when applicable. HKCU App Paths, PATH, Program Files scans, Start Menu scans, WindowsApps, uninstall keys and process scans are not used.

`OPEN_APPLICATION` launches via `CreateProcessW` with `lpApplicationName` set to the resolved absolute `.exe`, `lpCommandLine = null`, no inherited handles and working directory set to the executable parent directory. It does not use ShellExecute, `cmd.exe`, PowerShell, `runas`, UAC elevation, arguments, URI handlers, shortcuts, MSI, process monitoring or `WaitForExit`.

`OPEN_APPLICATION SUCCESS` means Windows accepted process creation for the authorized local executable. It does not mean the app finished starting, displayed a window, became foreground, remained running or responded.

Unknown commands are rejected. They are never interpreted as executable strings.

Forbidden generic command shapes:

- `EXECUTE_COMMAND`
- `RUN_COMMAND`
- `RUN_PROCESS`
- `RUN_POWERSHELL`
- `RUN_CMD`
- `EXECUTE_PATH`
- `command`
- `arguments`
- generic JSON payload

Future commands must extend this protocol explicitly with typed fields.

## Errors

Initial internal error codes:

- `SESSION_AGENT_UNAVAILABLE`
- `SESSION_CHANNEL_TIMEOUT`
- `SESSION_CHANNEL_UNAUTHORIZED`
- `SESSION_CHANNEL_PROTOCOL_MISMATCH`
- `SESSION_CHANNEL_INVALID_RESPONSE`
- `SESSION_COMMAND_NOT_SUPPORTED`
- `SESSION_CHANNEL_MALFORMED_REQUEST`
- `SESSION_COMMAND_RESULT_UNKNOWN`
- `INVALID_URL`
- `URL_LAUNCH_FAILED`
- `APPLICATION_BINDINGS_INVALID`
- `APPLICATION_BINDING_NOT_FOUND`
- `APPLICATION_BINDING_INVALID`
- `APPLICATION_DISABLED`
- `APPLICATION_EXECUTABLE_NOT_FOUND`
- `APPLICATION_LAUNCH_FAILED`

User-facing or remotely surfaced messages must not expose stack traces, SIDs, PIDs, full binary paths, ACLs or token details.

If the Service cannot reach a Session Agent before sending `OPEN_URL` or `OPEN_APPLICATION`, the remote operation maps to `SESSION_AGENT_UNAVAILABLE`. If the Service already sent the visible command and then loses or times out waiting for the response, the remote operation maps to `SESSION_COMMAND_RESULT_UNKNOWN` because the URL or application may already have opened. The client must not retry automatically.

## Performance

Idle cost is one asynchronous Named Pipe wait in the Session Agent.

The channel adds:

- no polling;
- no timer;
- no heartbeat;
- no disk writes;
- no WMI;
- no process scanning;
- no healthy `INFO` log spam.

Service-side session resolution and server verification run only when the Service sends an interactive command.
