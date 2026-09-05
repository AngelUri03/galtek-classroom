# Galtek Classroom Network Protocol v1

`galtek-classroom-network-v1.proto` defines the first Master <-> Client transport protocol.

The Client opens one persistent outbound gRPC stream to the Master. TLS/mTLS is mandatory and the peer certificate is pinned to the public key fingerprint persisted by the Prompt 12 pairing trust stores. The protocol never carries private keys, JWTs, shell commands or generic command payloads. `PROVISION_MANAGED_CREDENTIAL` is the only current secret-bearing operation and carries a Windows password as UTF-16LE bytes under the authenticated mTLS operation framework. `LOGOFF_WINDOWS_SESSION` carries only an expected managed account id.

Current scope:

- `ClientHello` identifies the paired Client by Network Identity, installation id, public key fingerprint and public SPKI.
- `ConnectionStatus` reports `CONNECTING`, `ONLINE`, `OFFLINE`, or `REJECTED`.
- `Heartbeat` and `HeartbeatAck` keep the authenticated stream alive.
- `OperationRequest`, `OperationAccepted` and `OperationResult` provide a typed remote operation framework; they do not carry shell commands, executable paths or generic command payloads. `LOCK_INPUT`, `UNLOCK_INPUT` and `GET_WINDOWS_SESSION_STATE` are explicit operation types without functional parameters. `GET_WINDOWS_SESSION_STATE` returns typed `WindowsSessionStateResult.state` details in `OperationResult`. `LOGOFF_WINDOWS_SESSION` uses `LogoffWindowsSessionOperationParameters` with only `ManagedWindowsAccountId account_id`. `OPEN_APPLICATION` uses `OpenApplicationOperationParameters` with a typed `applicationId` field only. `OPEN_URL` uses `OpenUrlOperationParameters` with a typed `url` field. `APPLY_BROWSER_NAVIGATION_POLICY` uses `ApplyBrowserPolicyOperationParameters` and typed rule enums. `APPLY_BROWSER_DOWNLOAD_POLICY` uses `ApplyBrowserDownloadPolicyOperationParameters` with typed download restriction and account scope enums. `PROVISION_MANAGED_CREDENTIAL` uses `ProvisionManagedCredentialOperationParameters` with only `ManagedWindowsAccountId account_id` and `bytes password_utf16le`.
- `OperationStatusQuery` and `OperationStatusReport` reconcile a previous operation by `operationId` and `targetDeviceId` on the existing `NetworkConnection.Connect` stream. The query is read-only and never re-runs an operation handler.
- `POWER_CONTROL_V1` announces Agent-side support for `SHUTDOWN` and `RESTART`.
- `OPEN_APPLICATION_V1` announces Agent-side support for opening authorized local applications through Session Command v1 and local `application-bindings.json` resolution.
- `OPEN_URL_V1` announces Agent-side support for `OPEN_URL` through Session Command v1 and the interactive user's registered HTTP/HTTPS handler.
- `BROWSER_NAVIGATION_POLICY_V1` announces Agent-side support for Chrome/Edge `URLBlocklist` and `URLAllowlist` enforcement for the real interactive Windows user.
- `BROWSER_DOWNLOAD_POLICY_V1` announces Agent-side support for Chrome/Edge `DownloadRestrictions` enforcement for the real interactive Windows user.
- `INPUT_CONTROL_V1` announces Agent-side support for `LOCK_INPUT` and `UNLOCK_INPUT` through Session Command v1 and the Session Agent's interactive-session `BlockInput` coordinator.
- `WINDOWS_SESSION_STATE_V1` announces Agent-side support for read-only, on-demand `GET_WINDOWS_SESSION_STATE` using the physical console session SID mapped against local managed Windows account bindings.
- `WINDOWS_SESSION_LOGOFF_V1` announces Agent-side support for `LOGOFF_WINDOWS_SESSION` using the physical console session SID and local managed Windows account binding for the expected `PRIMARY` or `SECONDARY` account.
- `MANAGED_CREDENTIAL_PROVISIONING_V1` announces Agent-side support for secret-bearing `PROVISION_MANAGED_CREDENTIAL`, local binding validation and immediate DPAPI persistence.

Authorization is never based on IP, MAC address, hostname, discovery, or MASTER license alone. A peer must be `PAIRED`, must not be `REVOKED`, and its certificate public key must match the stored trust fingerprint.

For uncertain power control, `UNKNOWN` means the Agent has no retained cache entry or durable receipt for that original operation. It does not prove physical failure or success, and the Master must not resend `SHUTDOWN` or `RESTART` automatically to find out.

For `OPEN_URL`, `SUCCESS` means Windows accepted the local launch request only. If the Agent Service sends the Session Command but loses or times out waiting for the response, the result is `SESSION_COMMAND_RESULT_UNKNOWN`; it is not retried automatically because the URL may already have opened.

For `OPEN_APPLICATION`, `SUCCESS` means Windows accepted process creation for the resolved authorized local `.exe` only. The Master and Agent Service never send executable paths, command lines, arguments, working directories, shell verbs, shortcuts or URI payloads. If the Agent Service sends the Session Command but loses or times out waiting for the response, the result is `SESSION_COMMAND_RESULT_UNKNOWN`; it is not retried automatically because the application may already have opened.

For `LOCK_INPUT`, `SUCCESS` means the Session Agent's owner thread confirmed `BlockInput(TRUE)` or confirmed the desired blocked state on a repeated lock. `LOCK_INPUT` still requires Commercial License `ACTIVE`.

For `UNLOCK_INPUT`, `SUCCESS` means no Galtek input lock was active or the owner thread confirmed `BlockInput(FALSE)`. `UNLOCK_INPUT` is recovery-safe and is not blocked by Commercial License state, but mTLS, pairing trust, non-revoked status, Device authorization and the authenticated Session Command channel remain required.

For `GET_WINDOWS_SESSION_STATE`, `SUCCESS` contains `WindowsSessionStateResult.state` with one of `NO_SESSION`, `PRIMARY_ACTIVE`, `SECONDARY_ACTIVE`, `OTHER_SESSION_ACTIVE` or `UNKNOWN`. The request has no functional payload. The result never carries SID, username, domain, account reference, session id, token handle or profile path. `WINDOWS_SESSION_STATE_UNSPECIFIED` is not a valid observed state. Invalid managed account bindings are reported as `MANAGED_ACCOUNT_BINDINGS_INVALID`; technical inability to observe a reliable session identity is reported as `WINDOWS_SESSION_UNKNOWN`.

For `LOGOFF_WINDOWS_SESSION`, the request contains only the expected managed `account_id` (`PRIMARY` or `SECONDARY`). The Agent loads the local binding, observes the physical console with `WTSGetActiveConsoleSessionId()`, reads the real `TokenUser` SID with `WTSQueryUserToken`/`GetTokenInformation`, compares it to the expected binding SID, repeats session id and SID validation immediately before the destructive call, and only then calls `WTSLogoffSession(WTS_CURRENT_SERVER_HANDLE, sessionId, FALSE)`. `NO_SESSION` is idempotent `SUCCESS`; any other active SID is `WINDOWS_SESSION_CHANGED`; unreliable console state is `WINDOWS_SESSION_UNKNOWN`; WTS rejection is `WINDOWS_LOGOFF_FAILED`. `SUCCESS` means Windows accepted the asynchronous logoff request, not that the session has already disappeared. No automatic retry or reconciliation is added for this operation.

For `PROVISION_MANAGED_CREDENTIAL`, `SUCCESS` means the Agent Service copied the received UTF-16LE bytes into a controlled mutable buffer, validated framing/length, validated the local `PRIMARY`/`SECONDARY` binding, protected the password immediately with DPAPI through the Client credential store, durably persisted `managed-windows-credentials.dat` and verified the store. It does not mean the password is correct, accepted by Windows, used for login, or changed in Windows. The operation result carries no secret payload. Invalid account ids or malformed password bytes are protocol violations; binding and store failures use structured account/credential error codes. If the Master does not receive `OperationResult`, the outcome is `OPERATION_RESULT_UNKNOWN` and must not be retried automatically.

Secret-bearing requests are not logged or cached as full protobufs. The Agent dedupe cache retains only metadata for this operation: operation id, operation type, target device id, protocol version, account id and the cached `OperationResult`. It never stores raw password bytes, a password hash, a password fingerprint or a serialized secret request.

For `APPLY_BROWSER_NAVIGATION_POLICY`, `SUCCESS` means the Agent wrote, reread and durably recorded the desired user-scope Chrome/Edge policy. It does not mean Chrome/Edge was restarted, installed, open, or that every already-loaded tab immediately changed state.

For `APPLY_BROWSER_DOWNLOAD_POLICY`, `SUCCESS` means the Agent wrote or removed the desired user-scope Chrome/Edge `DownloadRestrictions` value, reread and verified both browsers, durably recorded download state and cleared the download journal. The administrative API sends Galtek restriction modes, not raw Chromium `DownloadRestrictions` values. The Agent still does not provide Master batch dispatch for this operation.
