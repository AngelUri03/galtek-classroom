# Galtek Classroom Protocol

This directory contains protocol documents shared by the Java Master backend and the C# Agent components.

The definitive gRPC protocol is intentionally not implemented in Prompt 01. Future work must define explicit, structured messages and commands, then generate Java and C# bindings from the same `.proto` sources.

Discovery, trust, pairing, mTLS, device certificates, screen streams, projection, and administrative commands remain planned features.

Current local protocol:

- `local-ipc-v1.md`: Windows Named Pipe IPC v1 used for read-only local queries between Master Backend, Session Agent, and Agent Service.
