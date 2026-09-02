# Browser Download Policy

Prompt 16E1 adds the Master Backend source of truth for browser download policies. Prompt 16E2A prepares the typed Agent/Protobuf contract and the pure C# compiler that translates Galtek restriction modes to the native Chromium `DownloadRestrictions` value.

16E2A still does not apply `DownloadRestrictions`, write registry, dispatch from a Master endpoint, monitor downloads or implement teacher-authorized file delivery. Productive enforcement belongs to Prompt 16E2B.

## Navigation Is Not Download

Navigation policy and download policy are separate Galtek domains.

Example:

```text
BrowserAccessPolicy: ALLOWLIST educational sites
BrowserDownloadPolicy: BLOCK_ALL
```

The student can browse an educational page while browser downloads covered by the future native mechanism remain blocked. `DownloadRestrictions` does not belong in `BrowserAccessPolicy`, `BrowserPolicyMode`, `BrowserUrlRule` or URL match types.

## Restriction Modes

Galtek exposes these enum values:

```text
NO_SPECIAL_RESTRICTIONS
BLOCK_DANGEROUS
BLOCK_POTENTIALLY_DANGEROUS
BLOCK_ALL
BLOCK_MALICIOUS
```

Future native mapping for Chrome/Edge `DownloadRestrictions`:

```text
NO_SPECIAL_RESTRICTIONS      -> 0
BLOCK_DANGEROUS             -> 1
BLOCK_POTENTIALLY_DANGEROUS -> 2
BLOCK_ALL                   -> 3
BLOCK_MALICIOUS             -> 4
```

`NO_SPECIAL_RESTRICTIONS` means Galtek adds no special download restriction. It does not mean Galtek disables Safe Browsing or every browser security feature.

`BLOCK_ALL` means browser downloads covered by `DownloadRestrictions` are blocked. It does not mean Galtek prevents every possible way to create a file from Internet content. 16E2A does not implement Save Page As blocking, Print to PDF blocking, filesystem DLP, clipboard control or network filtering.

The exact registry application belongs to the Agent-side enforcement phase in Prompt 16E2B.

## Typed Remote Operation

The network protocol reserves a distinct operation:

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

## Implicit Versus Explicit Zero

16E2A intentionally distinguishes these cases:

```text
implicit_no_special_restrictions = true
restriction_mode = NO_SPECIAL_RESTRICTIONS
```

This means there is no effective Master policy. The compiler returns `RemoveGaltekPolicy = true` and no native value. 16E2B must remove only Galtek-owned policy state instead of writing native value `0`.

```text
implicit_no_special_restrictions = false
restriction_mode = NO_SPECIAL_RESTRICTIONS
```

This is an explicit policy. The compiler returns `RemoveGaltekPolicy = false` and native value `0`, so a specific Device policy can replace a previously more restrictive Galtek policy.

The content hash includes this distinction and is deterministic for the effective semantics. It does not include timestamps, visible names, Windows SIDs or registry paths.

## Agent Compiler

`ChromiumDownloadPolicyCompiler` is a pure C# component. It validates typed parameters, rejects `UNSPECIFIED` enums, rejects empty `policyId` and invalid versions for explicit policies, validates `accountScope` structurally and maps Galtek modes to native values `0` through `4`.

It does not resolve Windows SIDs. `ANY`, `PRIMARY` and `SECONDARY` remain valid account scopes in the contract, but 16E2B must keep the 16D rule: `ANY` applies to the real interactive Windows user, while `PRIMARY` and `SECONDARY` return `BROWSER_ACCOUNT_SCOPE_UNRESOLVED` until a safe Managed Account to SID binding exists.

## Capability And Handler

`BROWSER_DOWNLOAD_POLICY_V1` is reserved as a capability name, but the Agent must not announce it in `ClientHello` until 16E2B registers a productive handler that can modify and verify registry policy safely.

16E2A does not implement or register `ApplyBrowserDownloadPolicyOperationHandler`. Until 16E2B, the general dispatcher behavior remains `OPERATION_NOT_IMPLEMENTED` for this operation.

## Scopes And Accounts

Download policies reuse browser navigation scopes:

```text
CLASSROOM
GROUP
DEVICE
```

There is no Student scope.

Account scopes reuse:

```text
ANY
PRIMARY
SECONDARY
```

`PRIMARY` and `SECONDARY` are persistible now, but productive SID binding remains future work. If the effective resolver receives no account type, only `ANY` policies participate.

## Precedence

Exactly one download policy is effective:

```text
1. DEVICE + specific account
2. DEVICE + ANY
3. GROUP + specific account
4. GROUP + ANY
5. CLASSROOM + specific account
6. CLASSROOM + ANY
7. no policy -> implicit NO_SPECIAL_RESTRICTIONS
```

A more specific policy replaces the less specific one completely. Restriction modes are not combined numerically.

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
