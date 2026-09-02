# Browser Download Policy

Prompt 16E1 adds the Master Backend source of truth for browser download policies. It models and persists administrative intent only. It does not apply `DownloadRestrictions`, write registry, dispatch to the Agent, modify Protobuf, monitor downloads or implement teacher-authorized file delivery.

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

`BLOCK_ALL` means browser downloads covered by `DownloadRestrictions` are blocked. The exact registry application belongs to the Agent-side enforcement phase in Prompt 16E2.

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
