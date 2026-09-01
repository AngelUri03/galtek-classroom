# Galtek Classroom Network Protocol v1

`galtek-classroom-network-v1.proto` defines the first Master <-> Client transport protocol.

The Client opens one persistent outbound gRPC stream to the Master. TLS/mTLS is mandatory and the peer certificate is pinned to the public key fingerprint persisted by the Prompt 12 pairing trust stores. The protocol never carries private keys, passwords, JWTs, or command payloads.

Current scope:

- `ClientHello` identifies the paired Client by Network Identity, installation id, public key fingerprint and public SPKI.
- `ConnectionStatus` reports `CONNECTING`, `ONLINE`, `OFFLINE`, or `REJECTED`.
- `Heartbeat` and `HeartbeatAck` keep the authenticated stream alive.
- `OperationRequest`, `OperationAccepted` and `OperationResult` provide a typed remote operation framework; they do not carry shell commands, executable paths or generic command payloads.
- `OperationStatusQuery` and `OperationStatusReport` reconcile a previous operation by `operationId` and `targetDeviceId` on the existing `NetworkConnection.Connect` stream. The query is read-only and never re-runs an operation handler.
- `POWER_CONTROL_V1` announces Agent-side support for `SHUTDOWN` and `RESTART`.

Authorization is never based on IP, MAC address, hostname, discovery, or MASTER license alone. A peer must be `PAIRED`, must not be `REVOKED`, and its certificate public key must match the stored trust fingerprint.

For uncertain power control, `UNKNOWN` means the Agent has no retained cache entry or durable receipt for that original operation. It does not prove physical failure or success, and the Master must not resend `SHUTDOWN` or `RESTART` automatically to find out.
