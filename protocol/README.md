# Galtek Classroom Protocol

This directory contains protocol documents shared by the Java Master backend and the C# Agent components.

The current gRPC transport protocol is defined in `network/v1/galtek-classroom-network-v1.proto` and generates Java and C# bindings from the same source. It covers secure Master-Client connection, `ClientHello`, heartbeat, typed capabilities and the remote operation framework.

Discovery, screen streams, projection and most administrative commands remain planned features. `SHUTDOWN` and `RESTART` are the first productive Agent-side operations, exposed only through the typed remote operation framework.

Current local protocol:

- `local-ipc-v1.md`: Windows Named Pipe IPC v1 used for read-only local queries between Master Backend, Session Agent, and Agent Service.
