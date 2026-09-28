# Galtek Classroom Client - Windows Installer

## 0.0.4 physical upgrade closure (PC14)

Installer `0.0.4` is **CLOSED - REAL UPGRADE VALIDATED ON PC14**. The physically tested artifact has SHA-256 `1ED72A03E659C42CA37F08D4326A32730B96A28534F02F8C3834A4CE59BC7EEB`, BundleId `{C44D76A6-0183-4179-B45E-9123A4584B29}`, and MSI ProductCode `{A544ED43-3AAA-4B47-8EE7-0A3807C94D59}`.

Final evidence: legacy ARP `0`; old MSI `0.0.3` `0`; current Bundle `0.0.4` `1`; current MSI `0.0.4` `1`; legacy providers/caches `0`; current Bundle/MSI providers present; historical BA executions `0`. Cleanup reported `CLEANUP_COMPLETE`, `outcome=COMPLETE`, `reason=CLEAN`, `removed=21`. Apply reported `APPLY_COMPLETE`, `result=0x00000000`, and `restart=None`.

The Service was Running, Automatic (Delayed Start), LocalSystem, recovery 5/15/60, reset 86400, failure flag enabled. Session was Running. The Credential Provider and COM registration were present with `ThreadingModel=Apartment`. SHA-256 preservation passed for `appsettings.json` plus six ProgramData stores (7/7); `setup-rollback.json` was absent. Final quiescence had no `msiexec`, Galtek Burn, or Galtek Bootstrapper process; Windows Installer was Stopped/Manual.

This closure covers only the observed physical upgrade. It does not claim Repair Apply, Uninstall Apply, clean fresh install, artificial rollback, multi-PC rollout, or a future `0.0.4 -> 0.0.5` physical upgrade. `20F.1B` is **CODE/LOCAL VALIDATED - READY FOR PHYSICAL PROFILE MANAGEMENT RETEST**.

Release binaries are reproducible outputs and are not versioned in the source repository. The source, pinned build projects/scripts, release identities, hashes and validation conclusions are retained here; generated EXE/MSI/build directories belong outside Git. Reproducing a later release must advance the version according to the freeze policy below rather than rebuilding distributable `0.0.4` bytes.

The versioned sections below are a chronological record. Their `READY`, `PAUSED`, or `BLOCKED` labels describe the handoff at that time and are superseded by the current closure above wherever they conflict.

## 0.0.4 definitive bridge release candidate (20I.0.4, historical pre-retest state)

Before the physical retest, `0.0.4` was **CODE/LOCAL VALIDATED - READY FOR PC14 DEFINITIVE BRIDGE UPGRADE RETEST**. That handoff state is superseded by the physical closure above. The earlier physical 0.0.3 run proved mixed-state Detect, suppression of 0.0.1/broken 0.0.2, successful 0.0.2-to-0.0.3 MSI commit, restart handling, healthy runtime after reboot, and 7/7 preservation. It then failed the normal parent path: C183 was launched embedded, 0.0.3 passed `IntPtr.Zero` to `Engine.Apply`, WiX returned `E_INVALIDARG` (`0x80070057`), and cleanup blocked on the observed self-dependent graph. Targeted operator intervention let the parent finish but is not counted as normal installer success.

WiX 5.0.2 declares the native `IBootstrapperEngine::Apply` parent HWND as required while Detect/Elevate permit an optional parent. The managed wrapper forwards that value to the out-of-process engine. The source contract is recorded in the [WiX 5.0.2 IEngine interface](https://github.com/wixtoolset/wix/blob/v5.0.2/src/api/burn/WixToolset.BootstrapperApplicationApi/IEngine.cs), [managed Engine implementation](https://github.com/wixtoolset/wix/blob/v5.0.2/src/api/burn/WixToolset.BootstrapperApplicationApi/Engine.cs), and [native bridge](https://github.com/wixtoolset/wix/blob/v5.0.2/src/api/burn/balutil/BalBootstrapperEngine.cpp). Interactive Apply receives the real visible WPF HWND. Embedded/passive/none Apply receives a real process-owned Win32 `STATIC` HWND created on the BA thread with `WS_POPUP`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, zero dimensions and no ShowWindow call. It remains alive through ApplyComplete and Quit, then is destroyed on its creator thread. Tests prove nonzero/valid/current-process ownership, invisibility, styles and destruction; see the Microsoft contracts for [CreateWindowEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexw), [extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles), and [thread window ownership](https://learn.microsoft.com/en-us/windows/win32/procthread/creating-windows-in-threads).

All four historical Upgrade relations are suppressed before Burn can launch their old BA:

- `{2DCDF1F3-FBDD-42D6-8879-EA33126CA34C}`: `LEGACY_BROKEN_INTERACTIVE_BA`.
- `{43F6BCC5-2BEE-4CCD-9B51-1A4BE0548D85}`: `LEGACY_BROKEN_INTERACTIVE_BA`.
- `{C18369A6-9FC2-4360-9451-D77A44A7477C}`: `LEGACY_EMBEDDED_APPLY_HWND_BUG`.
- `{A2742DDC-6F6B-46F3-A42C-FF1FDFB8BD96}`: `LEGACY_EMBEDDED_APPLY_HWND_BUG`.

Suppression is exact by BundleId plus relation Upgrade and deliberately does not trust historical version metadata. Unknown/future BundleIds keep Burn's normal behavior. Fixtures prove zero old-BA launches and normal future 0.0.4-to-0.0.5 embedded transitions.

Cleanup is now an exact graph migration. Before any write it captures the four historical ARP nodes, two direct bundle providers, two stable bundle ProviderKeys, the 0.0.2/0.0.3 MSI providers, and every observed dependent edge. Each edge is classified `SELF_DEPENDENT`, `KNOWN_LEGACY_DEPENDENT`, `CURRENT_DEPENDENT`, or `UNKNOWN_DEPENDENT`; current/unknown edges block with zero mutation. Owner, version, display name, ARP, cache, MSI/runtime/current-bundle authority, snapshot and process guards must all pass. Mutation order is exact edges, live empty-provider recheck, old providers, old ARP entries and exact caches. Missing individual residues, arbitrary allowed mixes and a second run are successful/idempotent. No wildcard, shell, task kill, dependency-ignore or current-identity deletion is used.

Frozen identity:

- ProductVersion `0.0.4`.
- MSI ProductCode `{A544ED43-3AAA-4B47-8EE7-0A3807C94D59}`.
- MSI UpgradeCode `{2D9C681B-F7A8-4C5F-97C8-C5EACB2F31D6}`.
- Bundle UpgradeCode `{A67E1418-0AD4-4DA1-915A-1098091D23E7}`.
- ProviderKey `GaltekSolution.GaltekClassroom.Client.Bundle.0.0.4`.
- Final generated BundleId `{C44D76A6-0183-4179-B45E-9123A4584B29}`.
- Final PackageCode `{0C450F64-F8BA-442A-BC82-5AA87A31FDC8}`.

The official build, ICE validation, F1-F3D, 20I.0.2-F1/F2, 20I.0.3, PC14/post-reboot graph, preservation and partial-state fixtures pass. BA, cleanup helper, MSI and Burn each build with zero warnings/errors. Agent is 766/766, backend is 363/363, and UI test/typecheck/build pass with only the three known webpack budget warnings. Non-elevated Detect/Cancel smoke passed on the laptop's honest `REPAIRABLE_PARTIAL` state: window 1658 ms, DetectComplete 1767 ms, exit 0, no Plan, Apply or elevated engine.

Historical 0.0.4 output identity (the generated files are not versioned):

- `artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.4.exe`
- `artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.4.exe.sha256`
- `artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.4.manifest.json`

EXE: 31,802,082 bytes; SHA-256 `1ED72A03E659C42CA37F08D4326A32730B96A28534F02F8C3834A4CE59BC7EEB`; Authenticode `NotSigned`. The manifest records ProductCode, PackageCode, BundleId, hash and release freeze. Do not rebuild 0.0.4 after physical testing begins; any artifact-affecting change advances to 0.0.5. PC14 was not touched and there was no commit. `20F.1B` remains **CODE/LOCAL VALIDATED - PHYSICAL PROFILE RETEST PAUSED UNTIL 0.0.4 PASSES**.

## 0.0.3 clean release candidate (20I.0.3, historical pre-0.0.4 state)

`0.0.3` is the clean upgrade candidate for the exact PC14 mixed state. MSI ProductCode is pinned to `{2BA4D6A0-9492-484E-A27B-9EAE40C886A9}`; MSI UpgradeCode remains `{2D9C681B-F7A8-4C5F-97C8-C5EACB2F31D6}`; Bundle UpgradeCode remains `{A67E1418-0AD4-4DA1-915A-1098091D23E7}`; ProviderKey is `GaltekSolution.GaltekClassroom.Client.Bundle.0.0.3`. WiX generated final BundleId `{A2742DDC-6F6B-46F3-A42C-FF1FDFB8BD96}`, recorded from the final bind rather than authored.

Broken 0.0.1 `{2DCDF1F3-FBDD-42D6-8879-EA33126CA34C}` and broken 0.0.2 `{43F6BCC5-2BEE-4CCD-9B51-1A4BE0548D85}` are suppressed and are the helper's only cleanup targets. Healthy C183 `{C18369A6-9FC2-4360-9451-D77A44A7477C}` remains a normal Burn upgrade predecessor and is explicitly excluded from cleanup. The helper receives the generated current BundleId from BA data, accepts the exact Active/finalized registration lifecycle, validates the new MSI and runtime health, then validates every target before the first mutation.

Official build, ICE, F1-F3D, F1/F2 recovery contracts, PC14/C183/legacy/current/future fixtures and no-Apply smoke pass. BA, helper, MSI and Burn build with 0 warnings/0 errors. Agent 766, backend 363 and UI test/typecheck/build pass; webpack emits the three existing performance-budget warnings.

Historical 0.0.3 output identity (the generated files are not versioned):

- `artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.3.exe`
- `artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.3.exe.sha256`
- `artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.3.manifest.json`

EXE: 31,796,478 bytes; SHA-256 `8845B451714E66D9CB44602684CC19C8ACBA3252DA042E0F6525573DBCD6FB84`; Authenticode `NotSigned`. At that handoff its status was `CODE/LOCAL VALIDATED - READY FOR PC14 0.0.3 CLEAN UPGRADE RETEST`, not `REAL VALIDATED`; the later 0.0.4 closure above supersedes it. The 0.0.2 artifacts are also historical and must not be treated as current.

Release policy: ProductVersion is unique per release; ProductCode is stable per ProductVersion; both UpgradeCodes remain stable by product family; ProviderKey is versioned/stable; BundleId is generated and recorded after final bind. PackageCode is normal per final build, but a frozen release candidate is never rebuilt for distribution with different bytes under the same ProductVersion.

## 0.0.2 cleanup F2: observable helper, release blocked

F1's physical PC14 run validated mixed-state detection, `CompleteUpdate`, exact related-bundle suppression and the non-vital preservation path. The remaining helper returned decimal 10 (`0x8007000A` after Burn conversion). In the F1 helper, 10 was the application-defined mapping for any `CleanupOutcome.Blocked`, not Win32 `ERROR_BAD_ENVIRONMENT`. Because F1 did not persist the typed reason, the exact physical guard and value cannot be reconstructed from the supplied log. Active-session timing remains a hypothesis, not a retrospective fact.

F2 now accepts the exact healthy C183 identity during either Burn Active (`Resume=1`, `Installed=0|1`) or finalized ARP (`Resume=3`, `Installed=1`) lifecycle. It validates the stable ProviderKey separately from BundleId, allows only the current bundle process, and validates all allowlisted predecessors before mutation. Exit codes are `0`, `2010`, `2011`, `2012`, and `2013`. Diagnostics are written first to `%ProgramData%\Galtek\Classroom\Installer\logs\legacy-bundle-cleanup-*.log` (maximum ten retained) and selected failure/completion lines are mirrored into the main Burn log. Failure remains non-vital and returns the maintenance-pending UI.

Local helper/BA tests and recovery contracts pass; helper and BA build with zero warnings/errors. No final EXE is published, however. WiX 5.0.2 rejects authored `Bundle/@Id` with WIX0004 and generates a new BundleId at bind. A diagnostic build produced `{2DBE4FA8-745B-4A99-B22B-35DDD027DFDE}` rather than the installed healthy `{C18369A6-9FC2-4360-9451-D77A44A7477C}`. Burn treats that same-version C183 registration as an Upgrade and an Install plan writes the bundle ProviderKey. Shipping it could uninstall C183 or transfer its stable provider ownership, violating the recovery contract.

The existing `GaltekClassroom-Client-Setup-0.0.2.exe` (SHA-256 `370AD6018CA87417F3175BB96A9746F40FEBA50E8574AECCFAF5E7FB84EB4424`) is still the F1 binary and does not contain F2. Do not run it again as an F2 retest. Current status: `CODE/LOCAL COMPONENTS VALIDATED - RELEASE BLOCKED BY BUNDLE IDENTITY`; not ready for PC14. PC14 was not touched and there was no commit.

## 0.0.2 related-bundle recovery candidate (20I.0.2-F1)

The real PC14 0.0.1-to-0.0.2 retest is a **REAL UPGRADE FAILURE ON PC14**, although the child MSI 0.0.2 itself completed successfully and committed. The deadlock happened afterward: Burn launched the 0.0.1 bundle as `Action=Uninstall`, `Display=Embedded`, `Relation=Upgrade`; its old Custom BA entered the interactive product-state path, resolved the installed 0.0.2 as `INSTALLED_NEWER`, and waited forever without Plan, Apply, or Shutdown. The installed 0.0.1 binary cannot be repaired retroactively.

The current BA reads `IBootstrapperCommand.Action`, `.Display`, and `.Relation` directly. Full/no-relation launches remain interactive. Embedded, None, Passive, or related launches are headless: no WPF window, no interactive product resolver, Detect followed by an exact Plan of the requested action (including Install during rollback), Apply, and Shutdown. Burn cancellation is propagated without confirmation UI. Mode is logged as `BA_MODE=... action=... display=... relation=...`.

Backward compatibility is deliberately narrow. `PlanRelatedBundleType` assigns `RelatedBundlePlanType.None` only to these known broken Upgrade relations:

- `{2DCDF1F3-FBDD-42D6-8879-EA33126CA34C}`, version 0.0.1.
- `{43F6BCC5-2BEE-4CCD-9B51-1A4BE0548D85}`, version 0.0.2.

Therefore Burn never starts either defective BA, while MSI MajorUpgrade behavior and unknown/future related bundles remain normal. No `/unsafeuninstall`, dependency-ignore switch, timeout, process kill, simulated click, or UI automation is used.

WiX/Burn 5.0.2 has no public API that lets the new bundle externally unregister an old bundle without invoking its BA. The chain therefore runs the elevated net48 x64 `LegacyBundleRegistrationCleanup` after the MSI, gated by detected cleanup state. It is permanent/non-vital: cleanup failure never rolls back a healthy 0.0.2 and is surfaced as `UPDATE_INSTALLED_CLEANUP_PENDING` for a safe retry.

The helper is an exact allowlist migration, not a registry cleaner. Before mutation it verifies exact product/publisher/Bundle UpgradeCode/BundleId/version, provider and cache ownership, absence of the old 0.0.1 MSI, presence of current MSI `{DCF26CE0-A601-489B-B32A-E14095CC50C0}`, exactly one healthy current bundle registration, Automatic/Running Service, enabled Ready/Running Session task with the expected executable, no pending `%ProgramData%\Galtek\Classroom\Installer\setup-rollback.json`, and no old bundle process. It may touch only:

- `HKLM\SOFTWARE\Classes\Installer\Dependencies\{package-provider}\Dependents\{BundleId}`.
- `HKLM\SOFTWARE\Classes\Installer\Dependencies\{BundleId}`.
- `%ProgramData%\Package Cache\{BundleId}\GaltekClassroom-Client-Setup-{version}.exe` and that exact BundleId directory.
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{BundleId}`.

All registry access is Registry64 and all operations use typed .NET/Win32 APIs, without `reg.exe`, PowerShell, `cmd.exe`, or wildcard deletion. Current MSI/bundle/cache, Agent files, appsettings, ProgramData, Credential Provider and Session task are not cleanup targets. The operation is idempotent.

PC14 recovery is modeled as `INSTALLED_SAME` plus `NeedsPredecessorCleanup`. It presents `Completar actualizacion`, says that 0.0.2 is already installed and only an older registration remains, offers Cancel, and does not offer Install/Update/Uninstall. The action plans Install so Burn can maintain the fixed current registration while the already-current MSI is serviced as a no-op; the helper then removes both known stale registrations where present.

Same-version servicing keeps ProductVersion 0.0.2 and pins MSI ProductCode `{DCF26CE0-A601-489B-B32A-E14095CC50C0}` so rebuilding 0.0.2 cannot create a parallel MSI product. MSI UpgradeCode remains `{2D9C681B-F7A8-4C5F-97C8-C5EACB2F31D6}` and Bundle UpgradeCode remains `{A67E1418-0AD4-4DA1-915A-1098091D23E7}`. Fixed bundles use stable ProviderKey `GaltekSolution.GaltekClassroom.Client.Bundle.0.0.2`; the broken BundleId is removed by the allowlisted cleanup.

FilesInUse was secondary, since the physical MSI succeeded. The sequence is nevertheless enforced as snapshot capture -> stop/disable Session task -> standard Service stop -> replace files -> configure/start. No generic task kill or large sleep was introduced.

Validation: all F1-F3D contracts, recovery contracts, BA/helper tests, clean 0.0.1-to-0.0.2, PC14 mixed-state, cleanup fail-closed/idempotence, and synthetic future 0.0.2-to-0.0.3 embedded fixtures pass. The official pipeline and MSI ICE pass; BA, helper, MSI, and Burn build with 0 warnings/errors. A non-elevated local smoke reached `REPAIRABLE_PARTIAL` Detect in 1.836 s and closed normally with no Plan, Apply, or elevation. 20F.1B remained unchanged by this installer fix; Agent (766), backend (363), and UI suites pass.

Final output:

```text
artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.2.exe
artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.2.exe.sha256
artifacts/windows/installer/GaltekClassroom-Client-Setup-0.0.2.manifest.json
```

The EXE is 31,787,016 bytes, SHA-256 `370AD6018CA87417F3175BB96A9746F40FEBA50E8574AECCFAF5E7FB84EB4424`, Authenticode `NotSigned`. PC14 was not touched by this build. Status: `CODE/LOCAL VALIDATED - READY FOR PC14 MIXED-STATE RECOVERY RETEST`; this is not a physically validated 0.0.2 upgrade.

## 0.0.2 Managed Profiles V2 / 20I.0 closed

The physically observed 0.0.1 artifact (`GaltekClassroom-Client-Setup-0.0.1.exe`, SHA-256 `00CA643EB80999B901316B55FE8D7A257C37E0A2471180169DF275373436BC25`) completed the real legacy update on PC14. Service, Session Agent, Credential Provider, appsettings/ProgramData preservation, successful snapshot cleanup, Burn/MSI registration and same-EXE `INSTALLED_SAME` repair/uninstall UI were observed. Repair Apply, Uninstall Apply, an artificially induced F3D rollback, a clean-machine fresh install and multi-PC rollout were not observed and are not claimed. Status: `20I.0 CLOSED — REAL INSTALL VALIDATED ON PC14`.

That earlier 0.0.2 is now a **FAILED LAB BUILD**, superseded by the 20I.0.2-F1 recovery candidate above. It packaged Managed Profiles V2 and passed local build/ICE/no-Apply smoke, but its later physical PC14 retest exposed the related-bundle deadlock. Historical EXE: 31,767,141 bytes, SHA-256 `D4FE605EA673C3B26689D83C0150D4ED5C9858C31D9B645E5846F868034B29DF`, Authenticode `NotSigned`. It must not be treated as the current artifact or as a validated upgrade.

## F3D Service live validation and real rollback restore (20I.0-F3D)

F3D fixes the physical StrictMode failure in `Assert-ServiceContract`: the function read recovery bytes into `$failure` but later dereferenced the undeclared `$registry.FailureActions`. Live validation now opens an explicit Registry64 read-only key in `Get-GaltekServiceLiveState`, materializes the post-state, and disposes key/base handles in `finally`. PRE-STATE remains limited to adoption and rollback. The semantic ImagePath parser still requires the exact Galtek Service executable with no arguments and rejects external, UNC, ADS and lookalike inputs.

Final rollback is now an embedded native x64 DLL custom action. It captures the closed PowerShell child's stdout/stderr and submits each line to the active MSI log with `MsiProcessMessage`. `Return="ignore"` remains so Windows Installer can continue rollback, while BEGIN, SERVICE_CONFIG, SERVICE_RUNTIME, SESSION, CP, REGISTRY, VERIFY, COMPLETE and typed failures are visible. The earlier EXE custom action exposed only its exit code; inheriting standard handles did not make its output part of the MSI log.

Restore converges the exact raw PRE ImagePath through writable Registry64 after SCM configuration, restores Running/Stopped through SCM plus bounded polling, imports complete task XML and restores enabled/runtime state with terminating errors, restores only the captured Galtek Credential Provider values, restores Service recovery Registry values, then verifies all of that live. Every phase runs independently. Snapshot cleanup occurs only after complete success or successful commit; partial failure preserves it for diagnosis/retry.

The F3D fixtures model PC14's Running, Automatic (Delayed), LocalSystem, unquoted legacy ImagePath, recovery values, Enabled task and registered CP. They also force SERVICE_RUNTIME failure and prove SESSION/CP/REGISTRY/VERIFY still run, diagnostics are typed, the snapshot remains, retry converges and a later no-snapshot invocation is idempotent. The physical F3C post-state identifies SERVICE_RUNTIME as the first failed phase and an incomplete SESSION restore, but its exact exception/HRESULT cannot be recovered because the former EXE action discarded them; F3D deliberately does not invent those fields.

`CaptureClientState` around 51 seconds remains a profiling debt: no fixed wait, repeated hash, filesystem scan, CIM or WMI is present in the current path, so F3D does not broaden scope. Native legacy detection and `DisableSystemRestore="yes"` remain unchanged.

Official 0.0.1 pipeline: F1/F2/F3A/F3B/F3C/F3D tests, StrictMode, lifecycle/Registry/same-FileVersion and BA domain tests PASS; helper DLL, BA, MSI and Burn build with 0 warnings/0 errors; ICE PASS. Non-elevated smoke resolved `REPAIRABLE_PARTIAL` in 1.613 seconds with no Plan, Apply or elevation. Final EXE: 31,763,609 bytes, SHA-256 `00CA643EB80999B901316B55FE8D7A257C37E0A2471180169DF275373436BC25`, Authenticode `NotSigned`. Status: `READY FOR PC14 INSTALL RETEST #4`, not `REAL INSTALL VALIDATED`.

## F3C final stabilization (20I.0-F3C)

Service configuration now treats the rollback snapshot as PRE-STATE only. After `sc.exe` configuration, setup writes canonical Registry64 `ImagePath` as `"C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"`, closes the writable handle, reopens live state and semantically validates executable plus empty arguments. This fixes the physical raw-string mismatch where `sc.exe` had stored the same executable unquoted. External roots, arguments, UNC, ADS and lookalikes fail closed, with safe typed diagnostics.

Final rollback logs BEGIN, SERVICE_CONFIG, SERVICE_RUNTIME, SESSION, CP, REGISTRY, VERIFY and COMPLETE. Every component is attempted independently; errors are accumulated, the snapshot is preserved on any failure, and runtime returns to the captured Running/Stopped state only after configuration. The F3B physical post-state proves the historical exit 1 occurred at the Service runtime boundary: configuration had returned to the legacy ImagePath/Automatic values but the pre-running Service remained Stopped; the next old operation was `Start-Service`, which aborted remaining restore work. The old helper did not forward its diagnostics; F3C does.

The bundle uses WiX 5.0.2 `Chain DisableSystemRestore="yes"`, avoiding the measured ~141-second Burn restore-point operation without changing Windows globally. Existing MSI/lifecycle rollback remains authoritative. BA legacy detect is now one cached background .NET pass using Registry64 and Task Scheduler COM, with no PowerShell process, CIM/WMI, hashing, scans or polling. Progress remains indeterminate for an initial non-meaningful zero and becomes determinate when Burn supplies positive overall progress. Failure uses a restrained red accent.

Historical physical timings (the only action data available) are:

| Action | Start | End | Duration | Classification |
|---|---:|---:|---:|---|
| CaptureClientState | log event | log event | 49.288 s | historical F3A bottleneck |
| ConfigureClientInitial | log event | failure | 45.110 s | historical F3A bottleneck |
| InstallFiles/payload copy | log event | log event | ~16.307 s | historical F3A IO |
| RollbackClientStateFinal | log event | log event | 9.211 s | historical F3A rollback |
| CostFinalize | log event | log event | ~9 s | historical F3A costing |
| MSI file rollback | log event | log event | ~6.592 s | historical F3A rollback IO |
| RollbackClientConfiguration | log event | log event | ~1.796 s | historical F3A prepare |
| ConfigureClientInitial | ~21:43:37 | ~21:44:06 | ~29-30 s | F3B physical F3C evidence, aggregate to failure |
| Burn system restore point | 21:39:33 | 21:41:54 | ~141 s | F3B physical, disabled by F3C |
| Legacy detection | ~21:38:09 | ~21:38:26 | ~17 s | F3B physical, replaced by native pass |

The 213748 logs and `F3C-DIAGNOSTICO.txt` were not present in this workspace, so no exact F3C CaptureClientState timestamp is claimed. ProductVersion remains `0.0.1`.

Final official pipeline and ICE validation pass with 0 warnings/0 errors in helper, BA, MSI and Burn. The final non-elevated detect-only smoke resolved the laptop's honest `REPAIRABLE_PARTIAL` state in 1.732 s with no Plan, Apply or elevation. Artifact: `GaltekClassroom-Client-Setup-0.0.1.exe`, 31,752,033 bytes, SHA-256 `47E33381E008D921854E80C064D8068522E60A950B77FE2678ADC90EAC32D918`, Authenticode `NotSigned`. Status: `READY FOR PC14 FINAL INSTALL RETEST`.

The supported versioned Client installer is a WiX MSI wrapped by a Burn EXE. The historical PowerShell lifecycle scripts remain available for diagnostics and legacy/lab operations; the versioned installer adopts valid machines created by those scripts.

## Versioned Client Installer 0.0.1

From the repository root, one command publishes every current production Client payload, validates it, builds the MSI and Burn bundle, and writes hashes plus a non-secret manifest:

```powershell
.\installer\windows\build-client-installer.ps1
```

Final output:

```text
artifacts\windows\installer\GaltekClassroom-Client-Setup-0.0.1.exe
artifacts\windows\installer\GaltekClassroom-Client-Setup-0.0.1.exe.sha256
artifacts\windows\installer\GaltekClassroom-Client-Setup-0.0.1.manifest.json
```

The foundation under `installer/windows/setup/` uses WiX Toolset 5.0.2, pinned in the `.wixproj` SDK and extension package references. No global WiX installation is used. `InstallerVersion.props` is the single source of truth for ProductVersion and contains stable, distinct MSI and bundle UpgradeCodes.

### Custom Bootstrapper Application (20I.0-F3B)

Burn remains the engine and the MSI remains the transactional/lifecycle authority. The bundle now launches `GaltekClassroom.Bootstrapper.exe`, a WiX 5 out-of-process Custom BA built for x64 .NET Framework 4.8 with WPF and `WixToolset.BootstrapperApplicationApi` 5.0.2. WixStdBA/ThmUtil is historical and is no longer packaged or consulted at runtime. No .NET 8 self-contained runtime, browser, WebView, Electron, React, Tauri, Avalonia or MAUI payload is added.

The BA explicitly resolves `FRESH`, `LEGACY_SUPPORTED`, `INSTALLED_SAME`, `INSTALLED_OLDER`, `INSTALLED_NEWER`, `REPAIRABLE_PARTIAL`, `BLOCKED_CONFLICT` and `DETECTION_FAILED`. Burn registration, package and related-bundle callbacks provide registered-product evidence and real versions. The fixed `ClientStateHelper detect` verb supplies read-only legacy evidence. Its embedded command is generated from the same `legacy-adoption.ps1` authority used by MSI lifecycle, accepts no script/command arguments and emits only non-secret JSON fields. It performs no writes, service control, task mutation, CIM/WMI or polling.

State-to-action mapping is deliberate: fresh uses Install; supported legacy and older versions display Update but plan Install/upgrade; same displays Repair and optionally Uninstall; safe partial displays Repair but plans Install/convergence; newer/conflict/detection failure cannot plan. A complete valid PC14 legacy footprint therefore shows `Actualizar`, never `Instalar`, without inventing a legacy version.

The 720x520 es-MX WPF window uses the official symbol, a compact header, contextual card and stable footer. Planning/cache/execute/finalizing labels are driven only by real Burn boundaries. The progress bar is indeterminate until Burn exposes a reliable overall percentage, and elapsed time updates once per second. Failure shows a visible HRESULT such as `0x80070643`, prudent rollback copy and `Abrir registro`, which opens `WixBundleLog` through ShellExecute. Success distinguishes install/update/repair/uninstall; restart required offers explicit restart or later and never forces reboot automatically. The native titlebar, WPF layout, PerMonitorV2 manifest, Automation names and keyboard focus/default action cover 100/125/150% DPI and keyboard use.

Focused F3B validation includes the pure C# state/CTA/Burn-action/version/presentation suite plus:

```powershell
.\installer\windows\test-client-legacy-detection-bridge.ps1
.\installer\windows\test-client-custom-ba-asset-contract.ps1
.\installer\windows\test-client-bundle-ui-smoke.ps1 -ExpectedState FRESH
```

The smoke is non-elevated and closes the real WPF window with `WM_CLOSE`; it fails if the log contains Plan, Apply or elevation. This laptop currently resolves `REPAIRABLE_PARTIAL` because read-only detection finds valid Galtek Service and Credential Provider evidence but no Session task, so its verified invocation uses `-ExpectedState REPAIRABLE_PARTIAL`. Do not override the expected state to disguise real footprint evidence.

### Legacy adoption failure fix (20I.0-F1)

MSI Directory properties are formatted with a trailing separator. The old native command serialized `-InstallDirectory "[INSTALLFOLDER]"`, producing `...\Agent\"`; Windows argument parsing turned the closing quote into data. Rollback received `C:\Program Files\Galtek\Classroom\Agent"`, while initial install received `C:\Program Files\Galtek\Classroom\Agent" -InitialInstall`. The latter also prevented `InitialInstall` from binding. The current contract serializes `-InstallDirectory "[INSTALLFOLDER]."`; the valid dot segment makes the final character unambiguous and `Path.GetFullPath` returns the canonical root. The identical contract is used by initial, maintenance, rollback, commit and uninstall.

Before MSI stops the Service or ends/disables the Session task, an embedded static-runtime x64 helper captures the pre-transaction Service, task and Credential Provider state. A final rollback is scheduled immediately and remains available even if `InstallFiles` fails or removes installed support scripts. It restores the original task definition/enabled state, Service configuration/recovery and running state, and relevant CP registration. Old-product removal under `UPGRADINGPRODUCTCODE` skips this capture so it cannot overwrite the new transaction's snapshot. Production ProgramData and appsettings are not captured, read or rewritten.

Windows Installer file policy is `REINSTALLMODE=emus`, set before `CostInitialize`. Its `e` mode replaces equal-or-older versioned files during normal install and repair without forcing replacement of higher versions. This fixes legacy adoption where the existing Agent Service EXE/DLL and payload both report FileVersion 0.5.0.0 but have different hashes. ProductVersion remains 0.0.1 and does not lower assembly versions. Future payload FileVersion values must be monotonic; equal versions remain replaceable and higher installed versions remain protected. After file copy, lifecycle validates a packaged SHA-256 manifest for the Service EXE/DLL, Session EXE and Credential Provider DLL.

Focused non-mutating tests are available:

```powershell
.\installer\windows\test-client-installer-argument-transport.ps1
.\installer\windows\test-client-lifecycle-fixture.ps1
.\installer\windows\test-client-same-version-payload.ps1 -PayloadManifestPath <payload-manifest.json> [-MsiPath <client.msi>]
```

The build runs these automatically. The lifecycle fixture uses only temporary files/in-memory state and does not access laptop SCM, Task Scheduler or HKLM.

### Legacy adoption and rollback Registry fix (20I.0-F2)

The physical PC14 retest reached lifecycle capture and rejected legacy adoption. The historical message did not include the rejected component or path, so it cannot prove retrospectively whether SERVICE, SESSION, or CP supplied the failing string. F2 removes that ambiguity: SERVICE parses the executable token from the original SCM ImagePath with Windows quote/backslash semantics; Scheduled Task Execute, Arguments, and WorkingDirectory stay separate; and the Credential Provider InprocServer32 value is normalized as a path. A quoted `"C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"` resolves exactly to that executable even with later arguments or trailing whitespace.

Adoption remains fail-closed. It accepts only local drive paths under the exact Agent/component roots and requires the expected Galtek Service EXE, Session EXE, or Credential Provider DLL filename. Rejections report typed `component` and `reason`, plus a normalized path only when it is a safe local path. Tests cover the supported PC14 topology and independently reject `C:\Temp\evil.exe`, `C:\Windows\System32\cmd.exe`, and `C:\OtherVendor\provider.dll`.

Registry capture and restore use explicit .NET Registry64 handles: capture opens read-only, while SetValue/DeleteValue/CreateSubKey operations open with write access. The earlier `Get-Item(...).SetValue(...)` pattern used a read-only handle and caused the real `UnauthorizedAccessException`; the embedded final rollback repeated the same defect and returned exit code 1. The regression performs writes only below a temporary HKCU key and proves restore, deletion of only a transaction-created value, idempotency, and fail-closed handling of an unexpected foreign value.

Rollback configuration is now quiesce-only before Windows Installer rolls files back. The independent embedded final phase then restores Service configuration/recovery/running state, complete Scheduled Task XML/enabled/running state, and Credential Provider registration. It deletes `%ProgramData%\Galtek\Classroom\Installer\setup-rollback.json` only after successful final restoration; a failure preserves the snapshot. Successful commit also cleans it. Neither phase reads or modifies product appsettings or ProgramData stores.

Focused non-mutating tests:

```powershell
.\installer\windows\test-client-legacy-adoption.ps1
.\installer\windows\test-client-lifecycle-fixture.ps1
.\installer\windows\test-client-registry-rollback.ps1
```

Historical F2 status was `READY FOR PC14 INSTALL RETEST #2`; F3B supersedes only the bootstrapper UI/state model, not the need for physical PC14 validation.

The historical WixStdBA Failure-page visibility fix remains recorded in source/history. F3B replaces that runtime page with its own WPF failure presentation.

### Physical Service ImagePath fix and performance profile (20I.0-F3A)

The third PC14 child MSI log proves that F1 path transport and F2 typed diagnostics worked. `ConfigureClientInitial` reached `GALTEK_SETUP_STAGE=CAPTURE_STATE`, then rejected `component=SERVICE reason=PATH_OUTSIDE_GALTEK_ROOT path="C:\Program"`. The legacy SCM value was the exact Galtek Service executable path without surrounding quotes. Ordinary Windows argv parsing therefore treated `C:\Program` as argv[0]. Burn's later `0x80070643` is only the wrapper result.

F3A accepts this historical form narrowly: only the complete argument-free value may be retried as a path, it must normalize under the exact Agent root, and its filename must be exactly `GaltekClassroom.Agent.Service.exe`. Quoted command lines still use the F2 parser and may carry separated arguments. An unquoted value with arguments, an external root, or a lookalike filename fails closed. Successful configuration rewrites SCM ImagePath to the current quoted contract.

Run the executable regression directly:

```powershell
.\installer\windows\test-client-f3a-service-imagepath.ps1
```

The 8,181-line MSI measured `CaptureClientState` at 49.288 s and the failing `ConfigureClientInitial` at 45.110 s. Payload copy was about 16.307 s and `CostFinalize` about 9 s. The old configure path queried the primary Service twice through CIM, queried Scheduled Tasks again and performed a live CP lookup even though capture already held authoritative pre-mutation state. F3A now captures Service fields through read-only Registry64 plus `Get-Service`, reuses the transaction snapshot for legacy adoption, and removes CIM from capture/configuration/assertion. It retains Service/task/CP validation, critical hashes, rollback and conditional timeouts. No post-fix physical duration is claimed before another PC14 run.

The physical log records rollback prepare and MSI file restoration. Its embedded final rollback helper returned 1 without exposing the internal exception in the main log, so that physical final phase is not declared validated. Local lifecycle and Registry rollback fixtures pass, preserve the snapshot-on-error contract, and now include the exact unquoted F3A ImagePath. ProductVersion remains 0.0.1; the historical F3A handoff was `READY FOR CUSTOM BA REWORK`.

### Official branding

The repository-root `logo.png` is the sole visual source for the installer. `setup/generate-brand-assets.ps1` verifies its pinned SHA-256 and deterministically creates the WPF symbol PNG plus the multi-resolution 16/24/32/48/64/128/256 `.ico` without changing the original.

The current WPF BA embeds only the derived official symbol and uses the derived multi-resolution `.ico` for the bundle/window/MSI. Its colors remain Classroom blue `#456FE8`, corporate blue `#104C75` and a small `#FFA60C` signature. No external URL, CDN or web font is used.

Each build regenerates only those two official derivatives. The WPF resource is embedded in the BA assembly and the bundle consumes the `.ico`; obsolete ThmUtil theme XML, backgrounds, button sprites, status PNGs, progress bitmap and localization source were retired after F3B and are neither generated nor kept. No runtime asset depends on a developer-machine absolute path.

The UI reads the bundle version and detected related-bundle versions through Burn. It reports installed/available values only when known and never fabricates a legacy `0.0.0` or progress percentage.

### Historical ThmUtil geometry (replaced by F3B; source retired)

ThmUtil `SourceX`/`SourceY` selected a rectangle from the global image declared on the former theme; they were not general positioning attributes. WiX 5.0.2 required strict source-rectangle bounds. This is retained as historical design knowledge only. The Custom BA no longer packages WixStdBA/ThmUtil or any of those raster controls, and the contract tests assert their absence from the bundle.

After a successful build, the smoke reaches the real Burn/Custom BA runtime without planning or applying an action:

```powershell
.\installer\windows\test-client-bundle-ui-smoke.ps1
```

Run it only from a non-elevated PowerShell session. It waits for `GaltekClassroom.Bootstrapper.exe`, `DETECT_BEGIN`, `DETECT_COMPLETE` and the expected resolved state, then posts a normal close to the real window. It verifies no Plan, Apply or elevated engine process and no lingering helper. Logs are written below `artifacts\windows\installer\runtime-smoke\`. This is an initialization smoke, not install validation or a substitute for operator visual review.

The bundle supports fresh install, supported legacy adoption, in-place major upgrade, same-version repair and uninstall. The Custom BA is es-MX, blocks downgrade before Plan, appears as `Galtek Classroom Client` from `GaltekSolution`, and contains only:

- GaltekClassroom Agent Service;
- GaltekClassroom Session Agent;
- Galtek Credential Provider.

It never installs Master Backend, Master UI or Hub. Normal uninstall preserves `%ProgramData%\Galtek\Classroom\`; no purge option is exposed by the bundle. `appsettings.json` is a permanent `NeverOverwrite` component, so an installed file and its complete `MasterConnection` section survive adoption, upgrade, repair and uninstall/reinstall.

Legacy adoption is explicit in `setup/legacy-adoption.ps1` and `setup/client-lifecycle.ps1`. A pre-existing Galtek Service or Session task must resolve inside `%ProgramFiles%\Galtek\Classroom\Agent\`; otherwise adoption fails closed. The lifecycle converges the service to LocalSystem + Automatic (Delayed Start) + recovery 5/15/60 seconds/reset 86400/failureflag 1, recreates the locale-independent AtLogon Session task, verifies the side-by-side Credential Provider package and then starts/verifies the Service. It does not read or rewrite ProgramData stores.

Run static/pure contract validation without installing anything:

```powershell
.\installer\windows\test-client-installer-contract.ps1
```

The build requires Windows x64, PowerShell 5.1+, .NET SDK 8.0 and Visual Studio Build Tools with the C++ desktop workload. The first build may need NuGet access to restore the pinned WiX packages.

Do not treat a successful local build as a real installation validation. F3B status is `READY FOR CUSTOM BA VISUAL REVIEW`; the 0.0.1 bundle still requires a later controlled physical installation retest on PC14.

## Historical Script Lifecycle

The scripts below are the pre-MSI lifecycle retained for compatibility, focused diagnostics and legacy recovery.

### Requirements

- Windows x64.
- PowerShell 5.1 or newer.
- .NET SDK 8.0 to publish.
- Elevated PowerShell for install, update and uninstall.

Run installation scripts from an Administrator PowerShell session. The scripts do not bypass UAC.

## Full Agent Publish

From the repository root:

```powershell
.\installer\windows\publish-agent.ps1
```

This thin orchestrator calls:

```text
publish-agent-service.ps1
publish-agent-session.ps1
publish-credential-provider.ps1
```

Default outputs:

```text
artifacts\windows\agent-service\
artifacts\windows\agent-session\
artifacts\windows\credential-provider\
```

Service and Session publishes are `Release`, `win-x64`, self-contained and folder-based. Credential Provider publish is native `Release|x64` and produces only the DLL plus manifest. Publishing does not modify Program Files, ProgramData or HKLM.

## Full Agent Install Or Update

From an elevated PowerShell session:

```powershell
.\installer\windows\install-agent.ps1
```

This thin orchestrator calls:

```text
install-agent-service.ps1
install-session-agent.ps1
install-credential-provider.ps1
test-credential-provider-installation.ps1
```

Order: Agent Service, Session Agent, Credential Provider, then read-only Credential Provider verification. The Service installer manages `%ProgramFiles%\Galtek\Classroom\Agent\`, preserves the `Session\` and `CredentialProvider\` subdirectories, and preserves an existing `appsettings.json` (including `MasterConnection`) during upgrades. The Session installer manages `%ProgramFiles%\Galtek\Classroom\Agent\Session\` and preserves ProgramData. If Credential Provider install fails after Service/Session succeeded, the orchestrator fails and reports a partial Agent install instead of declaring success.

## Full Agent Uninstall

From an elevated PowerShell session:

```powershell
.\installer\windows\uninstall-agent.ps1
```

This thin orchestrator removes the Credential Provider registration first, then the Session Agent, then the Service.

By default it preserves:

```text
%ProgramData%\Galtek\Classroom\
```

To intentionally remove machine identity, license data and Master Windows Binding:

```powershell
.\installer\windows\uninstall-agent.ps1 -PurgeData
```

`-PurgeData` is passed only to `uninstall-agent-service.ps1`.

Warning: `PurgeData` elimina Installation Identity, Commercial License, Master Windows Binding y Network Identity. La instalacion resultante requerira una nueva activacion, reconfiguracion Master y nueva identidad de red.

## Agent Service

Publish only the Service:

```powershell
.\installer\windows\publish-agent-service.ps1
```

Install or update only the Service:

```powershell
.\installer\windows\install-agent-service.ps1
```

The script:

- validates that the published artifact exists;
- stops the existing service when present;
- copies Service binaries to `%ProgramFiles%\Galtek\Classroom\Agent\`;
- preserves `%ProgramFiles%\Galtek\Classroom\Agent\Session\` if it exists;
- creates or reconfigures service `GaltekClassroomAgent`;
- sets startup type to `Automatic (Delayed Start)` for both new installs and upgrades;
- runs as `LocalSystem`;
- configures service recovery restart delays of 5, 15 and 60 seconds;
- verifies Automatic + `DelayedAutostart=1`, LocalSystem, exact ImagePath, recovery reset/actions and `failureflag=1`;
- starts the service and verifies `Running`.

ProgramData is created or verified at `%ProgramData%\Galtek\Classroom\`, but existing `installation.json`, `license.dat` and `master-binding.json` are not deleted.

Verify:

```powershell
Get-Service GaltekClassroomAgent
Get-CimInstance Win32_Service -Filter "Name='GaltekClassroomAgent'" |
    Select-Object Name, State, StartMode, StartName, PathName
Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\GaltekClassroomAgent' |
    Select-Object DelayedAutostart, FailureActionsOnNonCrashFailures
.\installer\windows\test-agent-service-installation.ps1
```

Uninstall only the Service:

```powershell
.\installer\windows\uninstall-agent-service.ps1
```

The Service uninstaller preserves `Agent\Session\` when present and preserves ProgramData unless `-PurgeData` is passed. With `-PurgeData`, it also attempts to remove the Network Identity CNG machine key only when `network-identity.json` contains a safe Galtek `keyName`.

## Session Agent

Publish only the Session Agent:

```powershell
.\installer\windows\publish-agent-session.ps1
```

Install or update only the Session Agent:

```powershell
.\installer\windows\install-session-agent.ps1
```

The script:

- validates that the published artifact exists;
- stops the scheduled task if it is running;
- stops only installed Session Agent processes whose executable path is `%ProgramFiles%\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe`;
- copies Session Agent binaries to `%ProgramFiles%\Galtek\Classroom\Agent\Session\`;
- creates or replaces scheduled task `GaltekClassroomSessionAgent`;
- validates action, trigger, principal, run level, network setting and multiple-instance policy;
- starts the task for the current session when possible.

Scheduled Task configuration:

```text
TaskName: GaltekClassroomSessionAgent
Description: Galtek Classroom Session Agent
Trigger: AtLogon
Principal: S-1-5-32-545 (Builtin Users)
RunLevel: Limited
Action: GaltekClassroom.Agent.Session.exe --background
MultipleInstances: Parallel
ExecutionTimeLimit: none
Network required: false
Battery start allowed: true
```

`MultipleInstances Parallel` keeps the design compatible with fast user switching and RDP. Duplicate control is done inside the Session Agent with a per-session mutex.

Verify:

```powershell
Get-ScheduledTask -TaskName GaltekClassroomSessionAgent
Get-CimInstance Win32_Process -Filter "Name='GaltekClassroom.Agent.Session.exe'" |
    Select-Object ProcessId, SessionId, ExecutablePath, CommandLine
```

`SessionId` should match the interactive user session and should not be `0` during normal operation.

Uninstall only the Session Agent:

```powershell
.\installer\windows\uninstall-session-agent.ps1
```

The Session uninstaller removes the task and `%ProgramFiles%\Galtek\Classroom\Agent\Session\`. It does not touch ProgramData and has no `-PurgeData` option.

## Credential Provider Product Lifecycle

Publish only the Credential Provider:

```powershell
.\installer\windows\publish-credential-provider.ps1
```

Default output:

```text
artifacts\windows\credential-provider\
  GaltekClassroom.CredentialProvider.dll
  credential-provider.manifest.json
```

The package manifest includes schema, product/component, fixed CLSID, architecture, filename, SHA-256 and deterministic packageId. It does not contain secrets. SHA-256 validates artifact integrity but does not replace Authenticode signing.

Validate a package without elevation:

```powershell
.\installer\windows\test-credential-provider-package.ps1
```

Install or update only the Credential Provider from an elevated x64 PowerShell session:

```powershell
.\installer\windows\install-credential-provider.ps1
```

The script:

- requires Windows x64, PowerShell x64 and elevation;
- validates manifest, SHA-256, PE x64 and Authenticode status;
- fails closed for `SIGNED_INVALID`;
- requires Agent Service `GaltekClassroomAgent` to be installed;
- stages immutable versions under `%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\versions\<packageId>\`;
- registers only Galtek CLSID `{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}` in HKLM x64;
- sets `InprocServer32` to the staged DLL and `ThreadingModel` to `Apartment`;
- never creates a Credential Provider Filter;
- rolls back Galtek registration in-memory if install validation fails.

Verify installed registration without repair:

```powershell
.\installer\windows\test-credential-provider-installation.ps1
```

Uninstall only the Credential Provider:

```powershell
.\installer\windows\uninstall-credential-provider.ps1
```

The uninstaller removes only Galtek provider registration and Galtek COM registration, then attempts package cleanup best-effort. If LogonUI still has a DLL mapped, it does not kill LogonUI/Winlogon and reports `UNREGISTERED_REBOOT_CLEANUP_REQUIRED`.

Authenticode signing remains a release gate before commercial external distribution if no real production certificate is present. `NOT_SIGNED` is tolerated only for controlled/lab deployment; `SIGNED_INVALID` fails closed.

## Credential Provider Lab / Dev

Manual lab-only scripts remain available:

```powershell
.\installer\windows\register-credential-provider-dev.ps1
.\installer\windows\unregister-credential-provider-dev.ps1
```

These scripts are not part of the production installer. Use them only on a disposable lab PC with a known local administrator password and recovery available.

The registration script points by default to:

```text
%ProgramFiles%\Galtek\Classroom\Agent\CredentialProvider\GaltekClassroom.CredentialProvider.dll
```

It refuses DLL paths outside Program Files and does not register a Credential Provider Filter. Standard Windows providers such as password, PIN and Windows Hello must remain visible.

## Diagnostics

Bootstrap startup file diagnostics are off by default. For a deliberate diagnostic run only, set the per-process or per-service environment variable `GALTEK_BOOTSTRAP_STARTUP_DIAGNOSTICS=1`; the normal startup path does not create or flush the bootstrap file.

From development builds:

```powershell
cd agent
dotnet .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-ping
dotnet .\src\GaltekClassroom.Agent.Session\bin\Debug\net8.0\GaltekClassroom.Agent.Session.dll --ipc-status
```

The commands print JSON and terminate. They do not start the background supervisor.

Useful Service lifecycle commands:

```powershell
Stop-Service GaltekClassroomAgent
Start-Service GaltekClassroomAgent
Restart-Service GaltekClassroomAgent
```

## Troubleshooting

- If install or uninstall reports elevation errors, reopen PowerShell as Administrator.
- If publish cannot find the SDK, install .NET SDK 8.0 or set `DOTNET_ROOT`.
- If the Service fails to start with an identity error, inspect `%ProgramData%\Galtek\Classroom\installation.json`; corrupt identity files are not regenerated silently.
- If license status is `ACTIVATION_REQUIRED` or `LICENSE_KEY_NOT_CONFIGURED`, the Windows Service and Session Agent should still remain running and IPC read-only status should remain available.
