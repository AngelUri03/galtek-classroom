import type { DeviceActivityEvent, ManagedAccountSlot, WindowsAccount } from "../api/windowsAccountsApi";
import type { WindowsSessionState } from "../api/quickActionsApi";
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
import {
  beginDeviceOperationRefresh,
  claimDeviceOperations,
  commitDeviceOperationRefresh,
  deviceOperationLabel,
  hasBusyDevice,
  invalidateDeviceOperationRefreshes,
  replaceDeviceOperations,
  sessionMutationControl,
  type DeviceOperationSnapshot
} from "../app/deviceOperationModel";
import { beginSessionRefresh, commitSessionObservations, invalidateSessionRefreshes, type SessionSnapshot } from "../app/sessionStateModel";
import {
  confirmSessionExpectations,
  expectedSessionState,
  planSessionRefresh,
  reconcileSessionOperations,
  SESSION_RECONCILIATION_DEADLINE_MS,
  SESSION_RECONCILIATION_INTERVAL_MS,
  type SessionReconciliationScheduler
} from "../app/sessionReconciliationModel";
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
const originatingLogin = sessionMutationControl(pc14Mutation, "NO_SESSION", "SWITCH_MANAGED_ACCOUNT", "PRIMARY");
const incompatibleLogin = sessionMutationControl(pc14Mutation, "NO_SESSION", "SWITCH_MANAGED_ACCOUNT", "SECONDARY");
assert(originatingLogin.processing && originatingLogin.disabled && originatingLogin.processingLabel === "Iniciando Primaria…",
  "the originating login CTA must become an explicit processing control");
assert(!incompatibleLogin.processing && incompatibleLogin.disabled,
  "incompatible session actions must remain functionally disabled without looking like the originator");
const recovered = replaceDeviceOperations(operationState, ["PC14", "PC15"], [{
  ...pc14Mutation,
  state: "RECONCILIATION_REQUIRED"
}]);
assert(recovered.get("PC14")?.state === "RECONCILIATION_REQUIRED" && !recovered.has("PC15"),
  "reload recovery must replace only the requested device snapshots");
assert(deviceOperationLabel(recovered.get("PC14")!) === "Estado por confirmar",
  "unknown recovery must remain visibly blocked");
assert(deviceOperationLabel({ ...recovered.get("PC14")!, reconciliationStatus: "ACTIVE" })
  === "Confirmando estado del equipo…",
  "an active automatic reconciliation must not imply that manual action is required");
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

const sessionTransitions: [string, WindowsSessionState, WindowsSessionState][] = [
  ["NO_SESSION -> PRIMARY_ACTIVE", "NO_SESSION", "PRIMARY_ACTIVE"],
  ["NO_SESSION -> SECONDARY_ACTIVE", "NO_SESSION", "SECONDARY_ACTIVE"],
  ["NO_SESSION -> ADMIN_ACTIVE", "NO_SESSION", "ADMIN_ACTIVE"],
  ["PRIMARY_ACTIVE -> SECONDARY_ACTIVE", "PRIMARY_ACTIVE", "SECONDARY_ACTIVE"],
  ["SECONDARY_ACTIVE -> PRIMARY_ACTIVE", "SECONDARY_ACTIVE", "PRIMARY_ACTIVE"],
  ["managed -> ADMIN_ACTIVE", "PRIMARY_ACTIVE", "ADMIN_ACTIVE"],
  ["ADMIN_ACTIVE -> managed", "ADMIN_ACTIVE", "SECONDARY_ACTIVE"]
];
for (const [label, initial, terminal] of sessionTransitions) {
  const initialSnapshots = new Map<string, SessionSnapshot>([["PC14", {
    deviceId: "PC14", state: initial, generation: 0
  }]]);
  const started = beginSessionRefresh(new Map([["PC14", 0]]), ["PC14"]);
  const committed = commitSessionObservations(
    initialSnapshots,
    started.generations,
    started.ticket,
    [{ deviceId: "PC14", state: terminal }],
    new Map([["PC14", terminal]])
  );
  const authoritativeState = committed.snapshots.get("PC14")?.state;
  const surfaces = [authoritativeState, authoritativeState, authoritativeState, authoritativeState];
  assert(surfaces.every((surface) => surface === terminal),
    `${label} must update card, command center, inspector header and session tab from one authority`);
}

const firstRefresh = beginSessionRefresh(new Map<string, number>(), ["PC14"]);
const secondRefresh = beginSessionRefresh(firstRefresh.generations, ["PC14"]);
const newestCommit = commitSessionObservations(
  new Map(), secondRefresh.generations, secondRefresh.ticket,
  [{ deviceId: "PC14", state: "PRIMARY_ACTIVE" }]
);
const lateOldCommit = commitSessionObservations(
  newestCommit.snapshots, secondRefresh.generations, firstRefresh.ticket,
  [{ deviceId: "PC14", state: "NO_SESSION" }]
);
assert(lateOldCommit.snapshots.get("PC14")?.state === "PRIMARY_ACTIVE",
  "an older response must not overwrite a newer authoritative session observation");

const validBeforeMutation = new Map<string, SessionSnapshot>([["PC14", {
  deviceId: "PC14", state: "PRIMARY_ACTIVE", generation: 1
}]]);
const invalidatedGenerations = invalidateSessionRefreshes(new Map([["PC14", 1]]), ["PC14"]);
assert(validBeforeMutation.get("PC14")?.state === "PRIMARY_ACTIVE" && invalidatedGenerations.get("PC14") === 2,
  "starting a mutation must invalidate old requests without replacing the last valid snapshot");
const postSwitchRefresh = beginSessionRefresh(invalidatedGenerations, ["PC14"]);
const transientCommit = commitSessionObservations(
  validBeforeMutation,
  postSwitchRefresh.generations,
  postSwitchRefresh.ticket,
  [{ deviceId: "PC14", state: "NO_SESSION" }],
  new Map([["PC14", "SECONDARY_ACTIVE"]])
);
assert(transientCommit.rejectedDeviceIds.has("PC14") && transientCommit.snapshots.get("PC14")?.state === "PRIMARY_ACTIVE",
  "a transitional NO_SESSION must not become terminal after a successful switch");
const missingCommit = commitSessionObservations(
  validBeforeMutation,
  postSwitchRefresh.generations,
  postSwitchRefresh.ticket,
  [],
  new Map([["PC14", "SECONDARY_ACTIVE"]])
);
assert(missingCommit.rejectedDeviceIds.has("PC14") && missingCommit.snapshots.get("PC14")?.state === "PRIMARY_ACTIVE",
  "a missing post-mutation observation must preserve the previous snapshot and require reconciliation");

const secondaryReconciliation: DeviceOperationSnapshot = {
  ...pc14Mutation,
  state: "RECONCILIATION_REQUIRED",
  targetProfile: "SECONDARY"
};
const logoutReconciliation: DeviceOperationSnapshot = {
  ...pc14Mutation,
  state: "RECONCILIATION_REQUIRED",
  operationType: "LOGOFF_WINDOWS_SESSION",
  targetProfile: "ADMIN"
};
assert(expectedSessionState(secondaryReconciliation) === "SECONDARY_ACTIVE",
  "switch/login reconciliation must freeze its target profile expectation");
assert(expectedSessionState(logoutReconciliation) === "NO_SESSION",
  "logout reconciliation must require an authoritative NO_SESSION observation");
const reconciliationPlan = planSessionRefresh(["PC14", "PC15"], new Map([
  ["PC14", secondaryReconciliation]
]));
assert(reconciliationPlan.deviceIds.has("PC14") && reconciliationPlan.deviceIds.has("PC15"),
  "a reconciling device must remain readable without blocking a normal second device");
assert(reconciliationPlan.expectedStates.get("PC14") === "SECONDARY_ACTIVE"
  && !reconciliationPlan.expectedStates.has("PC15"),
"only the SESSION reconciliation target must constrain its observation");
const inProgressPlan = planSessionRefresh(["PC14", "PC15"], new Map([
  ["PC14", pc14Mutation]
]));
assert(!inProgressPlan.deviceIds.has("PC14") && inProgressPlan.deviceIds.has("PC15"),
  "IN_PROGRESS may suppress its transitional read but must not block another device");
for (const operationType of ["LOCK_INPUT", "RESTART"] as const) {
  const unrelated: DeviceOperationSnapshot = {
    ...pc14Mutation,
    state: "RECONCILIATION_REQUIRED",
    operationType,
    targetProfile: null
  };
  const plan = planSessionRefresh(["PC14"], new Map([["PC14", unrelated]]));
  assert(plan.deviceIds.has("PC14") && !plan.expectedStates.has("PC14"),
    `a SESSION read must not invent a reconciliation expectation for ${operationType}`);
}

const oldOperationRefresh = beginDeviceOperationRefresh(new Map<string, number>(), ["PC14"]);
const invalidatedOperationGenerations = invalidateDeviceOperationRefreshes(oldOperationRefresh.generations, ["PC14"]);
const operationAfterMutation = new Map<string, DeviceOperationSnapshot>([["PC14", pc14Mutation]]);
const staleOperationCommit = commitDeviceOperationRefresh(
  operationAfterMutation,
  invalidatedOperationGenerations,
  oldOperationRefresh.ticket,
  []
);
assert(staleOperationCommit.get("PC14") === pc14Mutation,
  "an old device-operation response must not clear a newer mutation state");
const activeReconciliation = new Map<string, DeviceOperationSnapshot>([["PC14", {
  ...secondaryReconciliation,
  reconciliationStatus: "ACTIVE"
}]]);
const settledOperationRefresh = beginDeviceOperationRefresh(new Map<string, number>(), ["PC14"]);
const stillPendingCommit = commitDeviceOperationRefresh(
  activeReconciliation,
  settledOperationRefresh.generations,
  settledOperationRefresh.ticket,
  [secondaryReconciliation]
);
assert(stillPendingCommit.get("PC14")?.reconciliationStatus === "ACTIVE",
  "a lease refresh must preserve the active automatic-reconciliation presentation");
const releasedCommit = commitDeviceOperationRefresh(
  activeReconciliation,
  settledOperationRefresh.generations,
  settledOperationRefresh.ticket,
  []
);
assert(!releasedCommit.has("PC14"),
  "the operation refresh after a target observation must not reintroduce a backend lease already removed");

async function sessionReconciliationContractTests() {
  assert(SESSION_RECONCILIATION_DEADLINE_MS === 8_000
    && SESSION_RECONCILIATION_INTERVAL_MS === 750,
  "post-mutation reconciliation must keep an explicit small deadline and conservative frequency");

  const runSequence = async (
    observations: WindowsSessionState[],
    expected: WindowsSessionState = "SECONDARY_ACTIVE",
    source: WindowsSessionState = "PRIMARY_ACTIVE",
    deadlineMs = SESSION_RECONCILIATION_DEADLINE_MS
  ) => {
    let readCount = 0;
    let mutationCount = 1;
    const scheduler = new FakeSessionScheduler();
    const outcome = await confirmSessionExpectations(
      new Set(["PC14"]),
      new Map([["PC14", expected]]),
      new Map([["PC14", source]]),
      async (_deviceIds, expectedStates) => {
        const observedState = observations[Math.min(readCount++, observations.length - 1)];
        return {
          observed: new Map([["PC14", observedState]]),
          rejectedDeviceIds: new Set(observedState === expectedStates.get("PC14") ? [] : ["PC14"])
        };
      },
      { scheduler, deadlineMs, intervalMs: SESSION_RECONCILIATION_INTERVAL_MS }
    );
    return { outcome, readCount, mutationCount, scheduler };
  };

  const immediate = await runSequence(["SECONDARY_ACTIVE"]);
  assert(immediate.readCount === 1 && immediate.outcome.confirmedDeviceIds.has("PC14")
    && immediate.scheduler.waits.length === 0,
  "target on the first read must reconcile immediately without a duplicate read");

  const physicalTransition = await runSequence(["NO_SESSION", "UNKNOWN", "SECONDARY_ACTIVE"]);
  assert(physicalTransition.readCount === 3 && physicalTransition.outcome.confirmedDeviceIds.has("PC14")
    && physicalTransition.scheduler.waits.length === 2,
  "SUCCESS -> NO_SESSION -> UNKNOWN -> SECONDARY_ACTIVE must converge automatically");
  assert(physicalTransition.mutationCount === 1,
    "automatic reconciliation must only issue reads and never repeat the original mutation");

  const unknownThenTarget = await runSequence(["UNKNOWN", "SECONDARY_ACTIVE"]);
  assert(unknownThenTarget.readCount === 2 && unknownThenTarget.outcome.confirmedDeviceIds.has("PC14"),
    "UNKNOWN must remain retryable inside the bounded window and converge on the target");

  const sourceTransition = await runSequence(["PRIMARY_ACTIVE", "SECONDARY_ACTIVE"]);
  assert(sourceTransition.readCount === 2 && sourceTransition.outcome.confirmedDeviceIds.has("PC14"),
    "the still-active source may be observed before the target");

  const timedOut = await runSequence(["UNKNOWN"], "SECONDARY_ACTIVE", "PRIMARY_ACTIVE", 1_500);
  assert(timedOut.outcome.timedOutDeviceIds.has("PC14") && !timedOut.outcome.confirmedDeviceIds.has("PC14")
    && timedOut.readCount === 3,
  "deadline exhaustion must retain reconciliation after a finite number of reads");

  const unexpected = await runSequence(["ADMIN_ACTIVE"]);
  assert(unexpected.outcome.incompatibleDeviceIds.has("PC14") && unexpected.readCount === 1
    && unexpected.scheduler.waits.length === 0,
  "an unexpected profile must stop the automatic window without fabricating success");

  const logout = await runSequence(
    ["PRIMARY_ACTIVE", "UNKNOWN", "NO_SESSION"],
    "NO_SESSION",
    "PRIMARY_ACTIVE"
  );
  assert(logout.outcome.confirmedDeviceIds.has("PC14") && logout.readCount === 3,
    "logout must tolerate source -> UNKNOWN and release only on NO_SESSION");

  const cancellation = new AbortController();
  const cancelled = await confirmSessionExpectations(
    new Set(["PC14"]),
    new Map([["PC14", "SECONDARY_ACTIVE"]]),
    new Map([["PC14", "PRIMARY_ACTIVE"]]),
    async () => {
      cancellation.abort();
      return { observed: new Map([["PC14", "NO_SESSION"]]), rejectedDeviceIds: new Set(["PC14"]) };
    },
    { scheduler: new FakeSessionScheduler(), signal: cancellation.signal }
  );
  assert(cancelled.cancelledDeviceIds.has("PC14") && cancelled.timedOutDeviceIds.size === 0,
    "unmount, classroom change or a new mutation must cancel pending reconciliation");

  const multiDeviceScheduler = new FakeSessionScheduler();
  let multiRead = 0;
  const isolated = await confirmSessionExpectations(
    new Set(["PC14", "PC15"]),
    new Map([["PC14", "SECONDARY_ACTIVE"], ["PC15", "PRIMARY_ACTIVE"]]),
    new Map([["PC14", "PRIMARY_ACTIVE"], ["PC15", "NO_SESSION"]]),
    async (deviceIds) => {
      multiRead += 1;
      const observed = new Map<string, WindowsSessionState>();
      if (deviceIds.has("PC14")) observed.set("PC14", "NO_SESSION");
      if (deviceIds.has("PC15")) observed.set("PC15", "PRIMARY_ACTIVE");
      return { observed, rejectedDeviceIds: new Set(observed.get("PC14") === "NO_SESSION" ? ["PC14"] : []) };
    },
    { scheduler: multiDeviceScheduler, deadlineMs: 750, intervalMs: 750 }
  );
  assert(isolated.confirmedDeviceIds.has("PC15") && isolated.timedOutDeviceIds.has("PC14")
    && multiRead === 2,
  "a reconciling Device A must not prevent Device B from confirming independently");

  let observedState: WindowsSessionState = "NO_SESSION";
  let backendOperations = new Map<string, DeviceOperationSnapshot>([["PC14", secondaryReconciliation]]);
  let snapshots = new Map<string, SessionSnapshot>([["PC14", {
    deviceId: "PC14", state: "PRIMARY_ACTIVE", generation: 1
  }]]);
  let generations = new Map<string, number>([["PC14", 1]]);
  const calls: string[] = [];
  const refreshOperations = async () => {
    calls.push("operations");
    return new Map(backendOperations);
  };
  const refreshSessions = async (
    deviceIds: ReadonlySet<string>,
    expectedStates: ReadonlyMap<string, WindowsSessionState>
  ) => {
    calls.push("session");
    const started = beginSessionRefresh(generations, deviceIds);
    generations = started.generations;
    const committed = commitSessionObservations(
      snapshots,
      generations,
      started.ticket,
      [...deviceIds].map((deviceId) => ({ deviceId, state: observedState })),
      expectedStates
    );
    snapshots = committed.snapshots;
    if (!committed.rejectedDeviceIds.has("PC14") && expectedStates.get("PC14") === observedState) {
      backendOperations.delete("PC14");
    }
    return committed;
  };

  const transient = await reconcileSessionOperations(["PC14"], refreshOperations, refreshSessions);
  assert(transient.operations.has("PC14") && snapshots.get("PC14")?.state === "PRIMARY_ACTIVE",
    "a transient NO_SESSION after switch SUCCESS must retain reconciliation and the last valid snapshot");
  assert(calls.join() === "operations,session,operations",
    "refresh must hydrate leases, read SESSION, then re-read leases in a deterministic order");

  observedState = "SECONDARY_ACTIVE";
  calls.length = 0;
  const recovered = await reconcileSessionOperations(["PC14"], refreshOperations, refreshSessions);
  assert(!recovered.operations.has("PC14") && snapshots.get("PC14")?.state === "SECONDARY_ACTIVE",
    "a later target observation must release reconciliation without reload");
  assert(calls.join() === "operations,session,operations",
    "manual refresh and bootstrap must use the same bounded reconciliation sequence");

  const runEquivalentRecovery = async () => {
    let operations = new Map<string, DeviceOperationSnapshot>([["PC14", secondaryReconciliation]]);
    const result = await reconcileSessionOperations(
      ["PC14"],
      async () => new Map(operations),
      async (_ids, expectedStates) => {
        if (expectedStates.get("PC14") === "SECONDARY_ACTIVE") operations = new Map();
        return "SECONDARY_ACTIVE";
      }
    );
    return { state: result.sessionResult, busy: result.operations.has("PC14") };
  };
  const manual = await runEquivalentRecovery();
  const reload = await runEquivalentRecovery();
  assert(JSON.stringify(manual) === JSON.stringify(reload) && !manual.busy,
    "manual refresh and full reload must converge through the same reconciliation contract");

  let absentObservationOperations = new Map<string, DeviceOperationSnapshot>([["PC14", {
    ...secondaryReconciliation,
    targetProfile: "PRIMARY"
  }]]);
  await reconcileSessionOperations(
    ["PC14"],
    async () => new Map(absentObservationOperations),
    async () => {
      // A missing observation cannot reconcile the backend lease.
      return undefined;
    }
  );
  assert(absentObservationOperations.has("PC14"),
    "login SUCCESS with an absent observation must remain recoverable on a later read");
}

class FakeSessionScheduler implements SessionReconciliationScheduler {
  current = 0;
  waits: number[] = [];

  now() { return this.current; }

  async wait(milliseconds: number, signal?: AbortSignal) {
    if (signal?.aborted) {
      const error = new Error("cancelled");
      error.name = "AbortError";
      throw error;
    }
    this.waits.push(milliseconds);
    this.current += milliseconds;
  }
}

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
  await sessionReconciliationContractTests();
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
  console.log("WINDOWS_ACCOUNT_VIEWMODEL_TESTS_PASS role-filters status-copy partial-state admin-session-parity batch-admin combined-credential-state reveal-octet-stream reveal-failed reveal-missing reveal-retry audit-copy active-unbind-feedback conflict-toast session-loading authoritative-session-transitions stale-response-guard transient-no-session-guard pending-snapshot-preservation refresh-snapshot operation-fetch-separation result-feedback processing-cta incompatible-disabled device-operation-lock double-click atomic-batch recovery-state toast-deduplication other-device-isolation");
}).catch((error) => {
  setTimeout(() => { throw error; }, 0);
});
