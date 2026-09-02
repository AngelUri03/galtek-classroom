# Browser Download Policy

Prompt 16E1 adds the Master Backend source of truth for browser download policies. Prompt 16E2A prepares the typed Agent/Protobuf contract and the pure C# compiler that translates Galtek restriction modes to the native Chromium `DownloadRestrictions` value. Prompt 16E2B implements productive Agent-side enforcement for Google Chrome and Microsoft Edge on Windows. Prompt 16F1 adds Master batch dispatch for applying the persisted download policy to selected Devices through the existing typed operation.

16F1 still does not add UI, download monitoring, browser automation, teacher-authorized file delivery, Agent changes, Protobuf changes or Registry changes.

## Native Enforcement

The Agent applies only the supported Chromium enterprise policy:

```text
DownloadRestrictions
```

Chrome user-scope location:

```text
HKEY_USERS\<REAL_USER_SID>\Software\Policies\Google\Chrome
Value: DownloadRestrictions
Type: REG_DWORD
```

Edge user-scope location:

```text
HKEY_USERS\<REAL_USER_SID>\Software\Policies\Microsoft\Edge
Value: DownloadRestrictions
Type: REG_DWORD
```

The Agent runs as `LocalSystem`, but it never writes `HKCU` for this feature. It resolves the real interactive Windows user through the existing 16D `IInteractiveUserIdentityResolver` and writes under `HKEY_USERS\<SID>` for that user. Session 0 is never the target.

The Agent does not write download policy under `HKLM`, does not modify other Chrome/Edge policies, and does not use `reg.exe`, PowerShell, `cmd`, WMI shell, scripts, extension, proxy, DNS, firewall, hosts or browser automation.

## Navigation Is Not Download

Navigation policy and download policy are separate Galtek domains.

Example:

```text
BrowserAccessPolicy: ALLOWLIST educational sites
BrowserDownloadPolicy: BLOCK_ALL
```

The student can browse an educational page while browser downloads covered by `DownloadRestrictions` remain blocked. `DownloadRestrictions` does not belong in `BrowserAccessPolicy`, `BrowserPolicyMode`, `BrowserUrlRule` or URL match types.

## Restriction Modes

Galtek exposes these enum values:

```text
NO_SPECIAL_RESTRICTIONS
BLOCK_DANGEROUS
BLOCK_POTENTIALLY_DANGEROUS
BLOCK_ALL
BLOCK_MALICIOUS
```

Native mapping for Chrome/Edge `DownloadRestrictions`:

```text
NO_SPECIAL_RESTRICTIONS      -> 0
BLOCK_DANGEROUS             -> 1
BLOCK_POTENTIALLY_DANGEROUS -> 2
BLOCK_ALL                   -> 3
BLOCK_MALICIOUS             -> 4
```

`ChromiumDownloadPolicyCompiler` remains the only authority for this mapping inside the Agent. The handler consumes the compiler result and does not duplicate a numeric severity table. The values are semantic enum translations; `4` (`BLOCK_MALICIOUS`) is not treated as "more restrictive" than `3` (`BLOCK_ALL`).

`NO_SPECIAL_RESTRICTIONS` means Galtek adds no special download restriction. It does not mean Galtek disables Safe Browsing or every browser security feature.

`BLOCK_ALL` uses `DownloadRestrictions = 3` and blocks downloads covered by that browser policy. It does not promise DLP and does not necessarily cover File -> Save Page As, Print -> Save as PDF, files created by other applications, general filesystem writes, clipboard, SMB, USB or generic network traffic.

`BLOCK_MALICIOUS` uses `DownloadRestrictions = 4`. Microsoft Edge requires Edge >= 100 for that native value. 16E2B does not scan browser processes, poll versions or block apply only because Edge is not installed.

## Typed Remote Operation

The network protocol has a distinct operation:

```text
APPLY_BROWSER_DOWNLOAD_POLICY
```

It is separate from:

```text
APPLY_BROWSER_NAVIGATION_POLICY
```

The operation parameters are typed Protobuf fields:

```text
policy_id
policy_version
implicit_no_special_restrictions
restriction_mode
account_scope
```

The request does not contain JSON payloads, `Struct`, `Any`, maps, native registry values, registry paths, Windows SIDs, usernames, browser executable paths, commands, arguments or scripts.

## Master Batch Dispatch

Prompt 16F1 adds:

```text
POST /api/classrooms/{classroomId}/browser-download-policies/apply
```

The request accepts only explicit `targetDeviceIds`. The Master does not accept `policyId`, restriction mode, account type, browser, Registry paths, native values, shell commands or arbitrary payloads in the apply request.

For each target, the Master resolves the effective persisted policy with `classroomId`, `deviceId`, group derived from the current `Student -> Device` assignment, and `accountType = null`. That means only `ANY` policies apply in 16F1. `PRIMARY` and `SECONDARY` remain persisted/readable concepts, but Master dispatch does not infer them yet.

The Master freezes typed `ApplyBrowserDownloadPolicyOperationParameters` before fanout, persists one `BatchOperation`, and uses one `operationId` for all targets. Preflight mirrors power control: target classroom membership, current binding, paired/non-revoked trust, authenticated `ONLINE` gRPC/mTLS connection and `BROWSER_DOWNLOAD_POLICY_V1`.

No effective policy is sent as `implicit_no_special_restrictions = true` with `NO_SPECIAL_RESTRICTIONS`, which asks the Agent to remove only Galtek-owned download policy. Explicit `NO_SPECIAL_RESTRICTIONS` is sent as explicit value `0`, preserving the 16E2A/16E2B distinction. Targets sent but left without confirmed `OperationResult` become `OPERATION_RESULT_UNKNOWN`.

## Implicit Versus Explicit Zero

16E2A intentionally distinguishes these cases and 16E2B preserves the distinction:

```text
implicit_no_special_restrictions = true
restriction_mode = NO_SPECIAL_RESTRICTIONS
```

This means there is no effective Master policy. The compiler returns `RemoveGaltekPolicy = true` and no native value. The Agent removes only `DownloadRestrictions` that Galtek can prove it owns. It does not write native value `0`.

```text
implicit_no_special_restrictions = false
restriction_mode = NO_SPECIAL_RESTRICTIONS
```

This is an explicit policy. The compiler returns `RemoveGaltekPolicy = false` and native value `0`, so a specific Device policy can replace a previously more restrictive Galtek policy by writing `REG_DWORD 0`.

The content hash includes this distinction and is deterministic for the effective semantics. It does not include timestamps, visible names, Windows SIDs or registry paths.

## Accounts

`accountScope = ANY` applies to the real interactive Windows user. `PRIMARY` and `SECONDARY` remain typed but return `BROWSER_ACCOUNT_SCOPE_UNRESOLVED` until there is a safe Managed Account -> Windows SID binding.

## Capability And Handler

`BROWSER_DOWNLOAD_POLICY_V1` is now announced by the Agent because 16E2B registers `ApplyBrowserDownloadPolicyOperationHandler` and can modify and verify registry policy safely.

The capability is operational information only. It does not bypass mTLS, pairing, non-`REVOKED` trust, Device registration, Commercial License `ACTIVE`, typed `OperationRequest` validation or dispatcher authorization.

## Ownership And External Conflicts

Download enforcement has its own local state and journal, separate from navigation:

```text
browser-download-policy-state.json
browser-download-policy-apply.json
```

The state records the local Windows SID, Galtek policy identity/version, content hash, implicit-removal flag, native value if any, apply time and per-browser parent-key ownership/ACL facts. It does not store usernames, passwords, tokens, arbitrary registry paths or browser history. Internal parent security descriptors may be stored only for rollback/recovery and are not exposed in operation results, IPC, logs or Master payloads.

Before an explicit apply, the Agent checks only these machine-level values:

```text
HKLM\Software\Policies\Google\Chrome\DownloadRestrictions
HKLM\Software\Policies\Microsoft\Edge\DownloadRestrictions
```

If either exists, the operation fails with `BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT`. Other unrelated HKLM browser policies are not treated as conflicts and are not modified.

On first user-scope apply, an existing `DownloadRestrictions` value without Galtek state is also an external conflict. If Galtek state exists, the real `DownloadRestrictions` values for Chrome and Edge must match the previous Galtek state exactly before any update/removal.

The Agent is deliberately conservative with the parent Chrome/Edge policy key. It can harden the parent key only when the parent is absent, empty, or contains only structures Galtek can prove it owns. `URLBlocklist` and `URLAllowlist` are considered Galtek-owned only when the read-only navigation ownership reader can prove ownership from 16D navigation state. Unknown parent values or unsafe unknown subkeys produce `BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT`. Unknown content is never deleted.

## ACL Parent-Only

Windows Registry has ACLs on keys, not individual values. Because `DownloadRestrictions` is a value inside the Chrome/Edge parent key, 16E2B never copies the navigation subkey ACL strategy blindly.

When Galtek can safely manage the parent key, the effective parent-key ACL is:

```text
LocalSystem: FullControl
Builtin Administrators: FullControl
target user: ReadKey
```

The target user must not retain `SetValue`, `CreateSubKey`, `WriteKey`, `ChangePermissions` or `TakeOwnership`. Galtek does not grant writer rights to Everyone, Authenticated Users or Builtin Users.

The ACL is applied only to:

```text
Software\Policies\Google\Chrome
Software\Policies\Microsoft\Edge
```

It is not propagated recursively and does not rewrite child subkeys such as `URLBlocklist`, `URLAllowlist`, `Recommended` or any other subkey. If the parent ACL is already safe, the Agent avoids rewriting it.

## Apply, Rollback And Recovery

`APPLY_BROWSER_DOWNLOAD_POLICY` flow is Agent-side only:

```text
typed request validation
-> ChromiumDownloadPolicyCompiler
-> account scope validation
-> resolve real interactive user
-> lazy recovery if journal exists
-> external conflict and ownership checks
-> persist journal
-> prepare/harden parent keys safely
-> apply Chrome and verify
-> apply Edge and verify
-> persist durable state
-> clear journal
```

Chrome and Edge Registry writes are not one atomic transaction. `SUCCESS` is returned only after both browsers match the desired state by read-back and durable state is confirmed. If Edge fails after Chrome was changed, the Agent attempts rollback for both browsers and any Galtek-modified parent ACLs. Confirmed rollback returns `BROWSER_DOWNLOAD_POLICY_APPLY_FAILED`; unconfirmed rollback returns `BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED` or `BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED`.

Recovery is lazy. There is no startup scan, timer, polling, browser scan, process scan or registry polling. On the next apply, an existing download journal is handled conservatively:

- registry equals desired: finalize durable state and clear the journal;
- registry equals previous: clear the journal;
- intermediate state with only known Galtek-owned download values: restore previous and clear the journal;
- unknown divergence or ambiguous ownership: return `BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED`.

Implicit removal with no Galtek state and no existing values is `SUCCESS`/no-change. Implicit removal with an unknown existing value is `BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT`. When Galtek owns the value, the Agent removes only `DownloadRestrictions`, verifies absence in both browsers and restores the previous parent ACL only if Galtek hardened it and restoration is still structurally safe. If an administrator added new parent-key policy content after Galtek apply, the Agent does not delete it and does not blindly restore an old ACL over it.

## Success Meaning

`APPLY_BROWSER_DOWNLOAD_POLICY SUCCESS` means only:

- Chrome has the desired `DownloadRestrictions` value or verified absence;
- Edge has the desired `DownloadRestrictions` value or verified absence;
- both read-backs matched;
- durable download state was confirmed;
- the download journal is clear.

It does not mean Chrome or Edge is installed, open, restarted, or that every existing tab instantly refreshed policy. Chrome/Edge support dynamic policy refresh; Galtek does not kill, restart or poll browser processes.

## Windows Limitation

16E1 intentionally does not model:

```text
blockedExtensions
allowedExtensions
blockedMimeTypes
allowedMimeTypes
```

Galtek Classroom needs Chrome and Edge semantics that can be enforced equivalently on Windows. Edge has `DownloadBlockedForFileTypes`, but it is not currently supported on Windows, so Galtek cannot promise arbitrary extension blocking across both browsers. Do not add administrable `.exe`, `.msi`, `.zip`, `.bat` or `.cmd` deny lists until there is an enforceable cross-browser design.

## Future Authorized Download

Teacher-authorized downloads under `BLOCK_ALL` must not temporarily disable browser download restrictions, open a time window for all downloads, automate browser clicks, kill or restart the browser, or rely on an improvised extension.

The future direction is:

```text
teacher authorizes content
-> Galtek delivers that file through a typed, controlled operation
-> authorized logical destination in StudentWorkspace
```

That may reuse future `DISTRIBUTE_FILE` architecture or a dedicated typed operation if needed. Browser downloads can remain blocked while Galtek delivers approved content through a controlled channel.

## Manual Validation Pending

Manual validation remains pending on a disposable lab PC, not the developer's personal browser profile. Validate with:

```text
chrome://policy
edge://policy
```

At minimum verify native value `0`, `BLOCK_ALL` value `3`, and implicit removal.
