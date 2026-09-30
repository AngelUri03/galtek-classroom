import type { DeviceActivityEvent, ManagedAccountSlot, WindowsAccount } from "../api/windowsAccountsApi";
import { ApiError, revealSensitiveSecret } from "../api/apiClient";
import {
  eligibleAccountsForRole,
  credentialsMatch,
  eligibleRolesForAccount,
  toWindowsAccountInventoryView
} from "./windowsAccountViewModel";
import { resolveSessionActionAvailability, resolveSessionActions } from "./sessionActionResolver";
import { toOperationResultView } from "./quickActionResultViewModel";
import { resolveManagedSessionEligibility } from "./managedSessionEligibility";
import { claimDeviceOperations, deviceOperationLabel, hasBusyDevice, replaceDeviceOperations, type DeviceOperationSnapshot } from "../app/deviceOperationModel";
import { KeyedRegistry } from "../app/keyedRegistry";
import type { ClassroomDeviceCardData } from "../types/classroom";
import {
  deviceActivityText,
  managedMutationFeedback,
  revealFailurePhase,
  showInitialSessionLoading
} from "./deviceInspectorModel";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`WINDOWS_ACCOUNT_VIEWMODEL_TEST_FAILED: ${message}`);
}

function account(
  accountName: string,
  administrator: boolean,
  overrides: Partial<WindowsAccount> = {}
): WindowsAccount {
  return {
    accountName,
    displayName: accountName,
    enabled: true,
    administrator,
    builtIn: false,
    managedRole: "NONE",
    ...overrides
  };
}

const emptySlots: ManagedAccountSlot[] = (["PRIMARY", "SECONDARY", "ADMIN"] as const).map((accountId) => ({
  accountId,
  configured: false,
  credentialConfigured: false,
  credentialStatus: "NOT_CONFIGURED",
  windowsAccountName: null
}));

const school = account("SCHOOL-14", false);
const admin = account("ADMIN-14", true);
const disabled = account("DISABLED-14", false, { enabled: false });
const builtIn = account("Administrator", true, { builtIn: true });

assert(eligibleRolesForAccount(school, emptySlots).join(",") === "PRIMARY,SECONDARY",
  "a school account must only be eligible for PRIMARY/SECONDARY");
assert(eligibleRolesForAccount(admin, emptySlots).join(",") === "ADMIN",
  "an administrator must only be eligible for ADMIN");
assert(eligibleRolesForAccount(disabled, emptySlots).length === 0,
  "disabled accounts must be ineligible");
assert(eligibleRolesForAccount(builtIn, emptySlots).length === 0,
  "built-in accounts must be ineligible");
assert(eligibleAccountsForRole("PRIMARY", [school, admin]).map((item) => item.accountName).join() === "SCHOOL-14",
  "PRIMARY must filter administrators");
assert(eligibleAccountsForRole("ADMIN", [school, admin]).map((item) => item.accountName).join() === "ADMIN-14",
  "ADMIN must require an administrator");
assert(!credentialsMatch("secret", "different") && credentialsMatch("secret", "secret"),
  "credential confirmation must reject mismatch and accept an exact match");

const view = toWindowsAccountInventoryView(
  {
    deviceId: "device-14",
    accounts: [
      { ...school, managedRole: "PRIMARY" },
      { ...admin, managedRole: "ADMIN" }
    ]
  },
  {
    accounts: [
      { ...emptySlots[0], configured: true, credentialStatus: "CREDENTIAL_NOT_CONFIGURED", windowsAccountName: school.accountName },
      emptySlots[1],
      { ...emptySlots[2], configured: true, credentialConfigured: true, credentialStatus: "READY", windowsAccountName: admin.accountName }
    ]
  },
  "PRIMARY_ACTIVE"
);

assert(view.managed.length === 3, "the presentation must always expose three stable slots");
assert(view.managed[0].statusLabel === "Falta contraseña", "partial binding must use missing-password copy");
assert(view.managed[0].active, "PRIMARY_ACTIVE must mark PRIMARY active");
assert(view.managed[2].statusLabel === "Lista para iniciar sesión", "READY must use human session-ready copy");
assert(view.managed[2].credentialLabel === "Contraseña guardada de forma segura",
  "READY must not claim that the password was authenticated");
assert(view.managed[2].remoteLoginSupported && !view.managed[2].active,
  "ADMIN must support managed session activation while remaining inactive in this fixture");
assert(view.system.some((item) => item.accountName === "Administrator") === false,
  "the fixture must not invent an account that was not in the inventory");

const systemView = toWindowsAccountInventoryView(
  { deviceId: "device-14", accounts: [builtIn] },
  { accounts: emptySlots },
  "NO_SESSION"
);
assert(systemView.system[0]?.accountName === "Administrator",
  "built-in administrator accounts must remain visible in the system group");

const adminActiveView = toWindowsAccountInventoryView(
  { deviceId: "device-14", accounts: [{ ...admin, managedRole: "ADMIN" }] },
  { accounts: [emptySlots[0], emptySlots[1], { ...emptySlots[2], configured: true, credentialConfigured: true, credentialStatus: "READY", windowsAccountName: admin.accountName }] },
  "ADMIN_ACTIVE"
);
assert(adminActiveView.managed[2].active, "ADMIN_ACTIVE must highlight the bound ADMIN slot");
assert(adminActiveView.managed[2].remoteLoginSupported,
  "ADMIN must have session parity with the other global profiles");
assert(resolveSessionActions("ADMIN_ACTIVE", false).join() === "SWITCH_PRIMARY,SWITCH_SECONDARY,LOGOFF_ADMIN",
  "ADMIN_ACTIVE must switch to academic profiles or log off");
assert(resolveSessionActions("OTHER_SESSION_ACTIVE", false).length === 0,
  "OTHER_SESSION_ACTIVE must expose no remote session mutation");
assert(resolveSessionActions("NO_SESSION", false).join() === "LOGIN_PRIMARY,LOGIN_SECONDARY,LOGIN_ADMIN",
  "NO_SESSION must expose all three global profiles");
assert(resolveSessionActions("PRIMARY_ACTIVE", false).join() === "SWITCH_SECONDARY,SWITCH_ADMIN,LOGOFF_PRIMARY",
  "PRIMARY_ACTIVE must expose SECONDARY/ADMIN switches and typed logout");
assert(resolveSessionActions("SECONDARY_ACTIVE", false).join() === "SWITCH_PRIMARY,SWITCH_ADMIN,LOGOFF_SECONDARY",
  "SECONDARY_ACTIVE must expose PRIMARY/ADMIN switches and typed logout");
assert(resolveSessionActions("UNKNOWN", false).length === 0,
  "UNKNOWN must expose no mutation");
assert(resolveSessionActions("PRIMARY_ACTIVE", true).length === 0,
  "offline devices must expose no session mutation");

const device = (id: string, capabilities: string[], rawStatus = "ONLINE"): ClassroomDeviceCardData => ({
  id, label: id, studentName: "Alumno", status: rawStatus === "ONLINE" ? "online" : "offline",
  rawStatus, statusLabel: rawStatus, secondaryStatus: "", note: "", capabilities,
  assignedStudentId: null, assignedStudentGroupId: null, assignedStudentGroupName: null, hostname: id
});
const readyManaged = new Map([["PC14", { accounts: [
  { ...emptySlots[0], configured: true, credentialConfigured: true, credentialStatus: "READY" as const, windowsAccountName: "PRIMARY-14" },
  { ...emptySlots[1], configured: true, credentialConfigured: true, credentialStatus: "READY" as const, windowsAccountName: "SECONDARY-14" },
  { ...emptySlots[2], configured: true, credentialConfigured: true, credentialStatus: "READY" as const, windowsAccountName: "ADMIN-14" }
] }]]);
const currentCapabilities = ["WINDOWS_SESSION_LOGON_V1", "WINDOWS_SESSION_SWITCH_V1", "ADMIN_MANAGED_SESSION_V1"];
for (const role of ["PRIMARY", "SECONDARY"] as const) {
  assert(resolveSessionActionAvailability({
    online: true,
    sessionState: "ADMIN_ACTIVE",
    targetRole: role,
    profile: readyManaged.get("PC14")?.accounts.find((slot) => slot.accountId === role),
    capabilities: currentCapabilities
  }) === "AVAILABLE", `ADMIN_ACTIVE must enable ready ${role}`);
}
for (const role of ["PRIMARY", "SECONDARY", "ADMIN"] as const) {
  const availability = resolveSessionActionAvailability({
    online: true,
    sessionState: "NO_SESSION",
    targetRole: role,
    profile: readyManaged.get("PC14")?.accounts.find((slot) => slot.accountId === role),
    capabilities: currentCapabilities
  });
  assert(availability === (role === "ADMIN" ? "REQUIRES_STEP_UP" : "AVAILABLE"),
    `NO_SESSION must expose ready ${role} with only ADMIN requiring step-up`);
}
assert(resolveSessionActionAvailability({
  online: true,
  sessionState: "NO_SESSION",
  targetRole: "PRIMARY",
  profile: emptySlots[0],
  capabilities: currentCapabilities
}) === "PROFILE_NOT_BOUND", "an empty slot must not be interpreted from presentation copy");
assert(resolveManagedSessionEligibility("ADMIN", [{ device: device("PC14", currentCapabilities), sessionState: "PRIMARY_ACTIVE" }], readyManaged, false).enabled,
  "a current client with a ready ADMIN profile must be eligible for exact-target ADMIN switch");
const oldClient = resolveManagedSessionEligibility("ADMIN", [{ device: device("PC14", ["WINDOWS_SESSION_SWITCH_V1"]), sessionState: "PRIMARY_ACTIVE" }], readyManaged, false);
assert(!oldClient.enabled && oldClient.reasons[0]?.includes("actualizar"),
  "a 0.0.4-style client must show human update copy for ADMIN sessions");
const mixed = resolveManagedSessionEligibility("ADMIN", [
  { device: device("PC14", currentCapabilities), sessionState: "PRIMARY_ACTIVE" },
  { device: device("PC15", currentCapabilities, "OFFLINE"), sessionState: "UNKNOWN" }
], readyManaged, false);
assert(!mixed.enabled && mixed.ready === 1 && mixed.blocked === 1,
  "batch ADMIN must never silently dispatch an eligible subset");

const combinedView = toWindowsAccountInventoryView(
  { deviceId: "device-14", accounts: [{ ...school, managedRole: "PRIMARY" }] },
  { accounts: [
    { ...emptySlots[0], configured: true, credentialConfigured: true, credentialStatus: "READY", windowsAccountName: school.accountName, vaultCredentialConfigured: false, combinedCredentialState: "CLIENT_ONLY_NOT_REVEALABLE" },
    emptySlots[1], emptySlots[2]
  ] },
  "NO_SESSION"
);
assert(combinedView.managed[0].credentialLabel === "Disponible en equipo, no revelable",
  "combined Client-only state must not claim the secret is revealable");

const partialResult = toOperationResultView({
  operationId: "operation-partial",
  type: "SWITCH_MANAGED_ACCOUNT",
  status: "PARTIAL_SUCCESS",
  targetCount: 1,
  successCount: 0,
  failedCount: 1,
  targets: [{
    deviceId: "PC14",
    status: "PARTIAL",
    errorCode: "CREDENTIAL_PROVIDER_UNAVAILABLE",
    message: "internal technical detail",
    attempt: 1
  }]
}, [device("PC14", currentCapabilities)]);
assert(partialResult.targets[0]?.resultText === "La sesión anterior se cerró, pero no se pudo iniciar la nueva.",
  "partial switch must use human copy and not expose a raw error");
assert(!partialResult.targets[0]?.resultText.includes("CREDENTIAL_PROVIDER_UNAVAILABLE"),
  "partial result copy must not expose enum values");

const unknownResult = toOperationResultView({
  operationId: "operation-unknown",
  type: "SWITCH_MANAGED_ACCOUNT",
  status: "PARTIAL_SUCCESS",
  targetCount: 1,
  successCount: 0,
  failedCount: 1,
  targets: [{
    deviceId: "PC14",
    status: "UNKNOWN",
    errorCode: "OPERATION_RESULT_UNKNOWN",
    message: null,
    attempt: 1
  }]
}, [device("PC14", currentCapabilities)]);
assert(unknownResult.tone === "unknown" && unknownResult.globalText === "No se pudo confirmar el resultado",
  "unknown results must be visible and distinct from failure");

const pc14Mutation: DeviceOperationSnapshot = {
  deviceId: "PC14",
  state: "IN_PROGRESS",
  operationType: "SWITCH_MANAGED_ACCOUNT",
  targetProfile: "PRIMARY",
  startedAtUtc: "2026-09-29T18:00:00Z"
};
const operationState = new Map([["PC14", pc14Mutation]]);
assert(hasBusyDevice(operationState, ["PC14"]), "the active device must be mutation-locked");
assert(!hasBusyDevice(operationState, ["PC15"]), "a busy PC14 must not block PC15");
assert(deviceOperationLabel(pc14Mutation) === "Cambiando a Primaria…",
  "pending UI must describe the frozen target rather than claim final state");
const recovered = replaceDeviceOperations(operationState, ["PC14", "PC15"], [{
  ...pc14Mutation,
  state: "RECONCILIATION_REQUIRED"
}]);
assert(recovered.get("PC14")?.state === "RECONCILIATION_REQUIRED" && !recovered.has("PC15"),
  "reload recovery must replace only the requested device snapshots");
assert(deviceOperationLabel(recovered.get("PC14")!) === "Estado por confirmar",
  "unknown recovery must remain visibly blocked");
const firstClaim = claimDeviceOperations(new Map(), ["PC14"], "SWITCH_MANAGED_ACCOUNT", "PRIMARY", "2026-09-29T18:00:00Z");
const doubleClickClaim = claimDeviceOperations(firstClaim.operations, ["PC14"], "SWITCH_MANAGED_ACCOUNT", "PRIMARY", "2026-09-29T18:00:00Z");
assert(firstClaim.accepted && !doubleClickClaim.accepted && doubleClickClaim.operations.size === 1,
  "an immediate repeated click must not create or dispatch a second local operation");
const atomicBatchClaim = claimDeviceOperations(firstClaim.operations, ["PC14", "PC15"], "RESTART", null, "2026-09-29T18:00:01Z");
assert(!atomicBatchClaim.accepted && !atomicBatchClaim.operations.has("PC15"),
  "a frontend batch containing a busy device must not claim an implicit subset");
const toastRegistry = new KeyedRegistry<{ summary: string }>();
const processingToast = { summary: "Cambiando sesión" };
const resultToast = { summary: "Primaria iniciada" };
assert(toastRegistry.replace("PC14:session", processingToast) === undefined,
  "the first operation toast must not replace an unrelated message");
assert(toastRegistry.replace("PC14:session", resultToast) === processingToast && toastRegistry.size === 1,
  "a terminal toast must replace the processing toast with the same operation key");
assert(!toastRegistry.deleteIfCurrent("PC14:session", processingToast) && toastRegistry.get("PC14:session") === resultToast,
  "an expired processing timer must not remove its newer terminal toast");

const failedRevealActivity: DeviceActivityEvent = {
  eventId: "event-1", classroomId: "room-1", deviceId: "PC14",
  eventType: "MANAGED_CREDENTIAL_REVEALED", actor: "LOCAL_MASTER",
  occurredAtUtc: "2026-09-29T18:00:00Z", role: "ADMIN",
  accountReference: "PC14\\ADMIN-14", result: "FAILED", message: null
};
assert(deviceActivityText(failedRevealActivity) === "No se pudo visualizar la contraseña de Administración",
  "failed reveal audit must never be presented as a successfully viewed password");
assert(showInitialSessionLoading(true, false), "an initial active fetch without a snapshot must show session loading");
assert(!showInitialSessionLoading(true, true), "a background refresh with a valid snapshot must preserve that snapshot");
assert(!showInitialSessionLoading(false, true), "a completed fetch with a snapshot must not remain loading");
assert(deviceOperationLabel(pc14Mutation) === "Cambiando a Primaria…" && !showInitialSessionLoading(true, true),
  "mutation pending and background session fetch pending must remain separate states");

const activeUnbind = managedMutationFeedback(
  "unbind", "ADMIN", "PC14", "MANAGED_ACCOUNT_SESSION_ACTIVE",
  "Managed profile cannot be removed while its session is active."
);
assert(activeUnbind.summary === "No se puede desvincular Administración" && activeUnbind.detail.includes("activa en PC14"),
  "active-profile unbind must produce human device-specific feedback");
assert(!activeUnbind.detail.includes("MANAGED_ACCOUNT_SESSION_ACTIVE"),
  "active-profile feedback must not expose an internal enum");
const conflictToast = managedMutationFeedback(
  "unbind", "ADMIN", "PC14", "DEVICE_OPERATION_IN_PROGRESS", "technical detail"
);
assert(conflictToast.severity === "warn" && conflictToast.summary.includes("operación en curso"),
  "a 409/device lease conflict must produce a human warning toast model");

async function apiContractTests() {
  const originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = async () => new Response(new TextEncoder().encode("fixture-password"), {
      status: 200,
      headers: { "Content-Type": "application/octet-stream" }
    });
    const revealed = await revealSensitiveSecret("/fixture", "authorization");
    assert(revealed.length === 16, "successful reveal must decode the octet-stream UTF-8 body");

    globalThis.fetch = async () => new Response(JSON.stringify({ code: "CREDENTIAL_NOT_FOUND", message: "missing" }), {
      status: 404,
      headers: { "Content-Type": "application/json" }
    });
    let missing: unknown;
    try { await revealSensitiveSecret("/fixture", "authorization"); } catch (error) { missing = error; }
    assert(missing instanceof ApiError && missing.status === 404 && revealFailurePhase(missing) === "missing",
      "missing reveal must retain its typed 404 state");

    globalThis.fetch = async () => new Response(JSON.stringify({ code: "REVEAL_FAILED", message: "failed" }), {
      status: 500,
      headers: { "Content-Type": "application/json" }
    });
    let failed: unknown;
    try { await revealSensitiveSecret("/fixture", "authorization"); } catch (error) { failed = error; }
    assert(failed instanceof ApiError && revealFailurePhase(failed) === "failed",
      "failed reveal must retain a retryable failed state");

    let retryCalls = 0;
    globalThis.fetch = async () => {
      retryCalls += 1;
      return retryCalls === 1
        ? new Response(JSON.stringify({ code: "REVEAL_FAILED", message: "failed" }), { status: 503, headers: { "Content-Type": "application/json" } })
        : new Response(new TextEncoder().encode("retry-fixture"), { status: 200, headers: { "Content-Type": "application/octet-stream; charset=binary" } });
    };
    try { await revealSensitiveSecret("/fixture", "authorization"); } catch { /* explicit user retry follows */ }
    const retried = await revealSensitiveSecret("/fixture", "authorization");
    assert(retryCalls === 2 && retried.length === 13, "an explicit reveal retry must issue exactly one additional request and recover");

    globalThis.fetch = async () => new Response(JSON.stringify({ value: "not-accepted" }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    let invalidContentType: unknown;
    try { await revealSensitiveSecret("/fixture", "authorization"); } catch (error) { invalidContentType = error; }
    assert(invalidContentType instanceof ApiError && invalidContentType.code === "INVALID_SECRET_RESPONSE",
      "reveal must fail closed when a wrapper or endpoint returns JSON instead of octet-stream");
  } finally {
    globalThis.fetch = originalFetch;
  }
}

void apiContractTests().then(() => {
  console.log("WINDOWS_ACCOUNT_VIEWMODEL_TESTS_PASS role-filters status-copy partial-state admin-session-parity batch-admin combined-credential-state reveal-octet-stream reveal-failed reveal-missing reveal-retry audit-copy active-unbind-feedback conflict-toast session-loading refresh-snapshot operation-fetch-separation result-feedback device-operation-lock double-click atomic-batch recovery-state toast-deduplication other-device-isolation");
}).catch((error) => {
  setTimeout(() => { throw error; }, 0);
});
