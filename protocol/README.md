# Galtek Classroom Protocol

This directory contains protocol documents shared by the Java Master backend and the C# Agent components.

The current gRPC transport protocol is defined in `network/v1/galtek-classroom-network-v1.proto` and generates Java and C# bindings from the same source. It covers secure Master-Client connection, `ClientHello`, heartbeat, typed capabilities, the remote operation framework and read-only status queries for reconciling uncertain prior operations.

Discovery, screen streams, projection and most administrative commands remain planned features. Productive Agent-side operations currently include `SHUTDOWN`, `RESTART`, `OPEN_URL`, `OPEN_APPLICATION`, `APPLY_BROWSER_NAVIGATION_POLICY` and `APPLY_BROWSER_DOWNLOAD_POLICY`, exposed only through the typed remote operation framework. `OPEN_APPLICATION` carries only `applicationId`; executable resolution stays local to the Client. Uncertain power-control operations are reconciled by asking about the original `operationId`; the protocol does not automatically resend destructive requests.

Current local protocol:

- `local-ipc-v1.md`: Windows Named Pipe IPC v1 used for read-only local queries between Master Backend, Session Agent, and Agent Service.
- `local-session-command-v1.md`: privileged Service-to-Session command channel for typed interactive actions such as `OPEN_URL` and `OPEN_APPLICATION`.
