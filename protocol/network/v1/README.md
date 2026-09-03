# Galtek Classroom Network Protocol v1

`galtek-classroom-network-v1.proto` defines the first Master <-> Client transport protocol.

The Client opens one persistent outbound gRPC stream to the Master. TLS/mTLS is mandatory and the peer certificate is pinned to the public key fingerprint persisted by the Prompt 12 pairing trust stores. The protocol never carries private keys, passwords, JWTs, or command payloads.

Current scope:

- `ClientHello` identifies the paired Client by Network Identity, installation id, public key fingerprint and public SPKI.
- `ConnectionStatus` reports `CONNECTING`, `ONLINE`, `OFFLINE`, or `REJECTED`.
- `Heartbeat` and `HeartbeatAck` keep the authenticated stream alive.
- `OperationRequest`, `OperationAccepted` and `OperationResult` provide a typed remote operation framework; they do not carry shell commands, executable paths or generic command payloads. `LOCK_INPUT` and `UNLOCK_INPUT` are explicit operation types without functional parameters. `OPEN_APPLICATION` uses `OpenApplicationOperationParameters` with a typed `applicationId` field only. `OPEN_URL` uses `OpenUrlOperationParameters` with a typed `url` field. `APPLY_BROWSER_NAVIGATION_POLICY` uses `ApplyBrowserPolicyOperationParameters` and typed rule enums. `APPLY_BROWSER_DOWNLOAD_POLICY` uses `ApplyBrowserDownloadPolicyOperationParameters` with typed download restriction and account scope enums.
- `OperationStatusQuery` and `OperationStatusReport` reconcile a previous operation by `operationId` and `targetDeviceId` on the existing `NetworkConnection.Connect` stream. The query is read-only and never re-runs an operation handler.
- `POWER_CONTROL_V1` announces Agent-side support for `SHUTDOWN` and `RESTART`.
- `OPEN_APPLICATION_V1` announces Agent-side support for opening authorized local applications through Session Command v1 and local `application-bindings.json` resolution.
- `OPEN_URL_V1` announces Agent-side support for `OPEN_URL` through Session Command v1 and the interactive user's registered HTTP/HTTPS handler.
- `BROWSER_NAVIGATION_POLICY_V1` announces Agent-side support for Chrome/Edge `URLBlocklist` and `URLAllowlist` enforcement for the real interactive Windows user.
- `BROWSER_DOWNLOAD_POLICY_V1` announces Agent-side support for Chrome/Edge `DownloadRestrictions` enforcement for the real interactive Windows user.
- `INPUT_CONTROL_V1` announces Agent-side support for `LOCK_INPUT` and `UNLOCK_INPUT` through Session Command v1 and the Session Agent's interactive-session `BlockInput` coordinator.

Authorization is never based on IP, MAC address, hostname, discovery, or MASTER license alone. A peer must be `PAIRED`, must not be `REVOKED`, and its certificate public key must match the stored trust fingerprint.

For uncertain power control, `UNKNOWN` means the Agent has no retained cache entry or durable receipt for that original operation. It does not prove physical failure or success, and the Master must not resend `SHUTDOWN` or `RESTART` automatically to find out.

For `OPEN_URL`, `SUCCESS` means Windows accepted the local launch request only. If the Agent Service sends the Session Command but loses or times out waiting for the response, the result is `SESSION_COMMAND_RESULT_UNKNOWN`; it is not retried automatically because the URL may already have opened.

For `OPEN_APPLICATION`, `SUCCESS` means Windows accepted process creation for the resolved authorized local `.exe` only. The Master and Agent Service never send executable paths, command lines, arguments, working directories, shell verbs, shortcuts or URI payloads. If the Agent Service sends the Session Command but loses or times out waiting for the response, the result is `SESSION_COMMAND_RESULT_UNKNOWN`; it is not retried automatically because the application may already have opened.

For `LOCK_INPUT`, `SUCCESS` means the Session Agent's owner thread confirmed `BlockInput(TRUE)` or confirmed the desired blocked state on a repeated lock. `LOCK_INPUT` still requires Commercial License `ACTIVE`.

For `UNLOCK_INPUT`, `SUCCESS` means no Galtek input lock was active or the owner thread confirmed `BlockInput(FALSE)`. `UNLOCK_INPUT` is recovery-safe and is not blocked by Commercial License state, but mTLS, pairing trust, non-revoked status, Device authorization and the authenticated Session Command channel remain required.

For `APPLY_BROWSER_NAVIGATION_POLICY`, `SUCCESS` means the Agent wrote, reread and durably recorded the desired user-scope Chrome/Edge policy. It does not mean Chrome/Edge was restarted, installed, open, or that every already-loaded tab immediately changed state.

For `APPLY_BROWSER_DOWNLOAD_POLICY`, `SUCCESS` means the Agent wrote or removed the desired user-scope Chrome/Edge `DownloadRestrictions` value, reread and verified both browsers, durably recorded download state and cleared the download journal. The administrative API sends Galtek restriction modes, not raw Chromium `DownloadRestrictions` values. The Agent still does not provide Master batch dispatch for this operation.
