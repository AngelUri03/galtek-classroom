import type { WindowsSessionState } from "../api/quickActionsApi";
import type { DeviceOperationSnapshot } from "./deviceOperationModel";

export type SessionRefreshPlan = {
  deviceIds: Set<string>;
  expectedStates: Map<string, WindowsSessionState>;
};

export type SessionExpectationResult = {
  observed: ReadonlyMap<string, WindowsSessionState>;
  rejectedDeviceIds: ReadonlySet<string>;
};

export type SessionReconciliationScheduler = {
  now: () => number;
  wait: (milliseconds: number, signal?: AbortSignal) => Promise<void>;
};

export type SessionReconciliationOutcome = {
  confirmedDeviceIds: ReadonlySet<string>;
  timedOutDeviceIds: ReadonlySet<string>;
  incompatibleDeviceIds: ReadonlySet<string>;
  cancelledDeviceIds: ReadonlySet<string>;
};

export const SESSION_RECONCILIATION_DEADLINE_MS = 8_000;
export const SESSION_RECONCILIATION_INTERVAL_MS = 750;

export function expectedSessionState(
  operation: DeviceOperationSnapshot | undefined
): WindowsSessionState | undefined {
  if (!operation || operation.state !== "RECONCILIATION_REQUIRED") return undefined;
  if (operation.operationType === "LOGOFF_WINDOWS_SESSION") return "NO_SESSION";
  if (operation.operationType !== "SWITCH_MANAGED_ACCOUNT") return undefined;
  if (operation.targetProfile === "PRIMARY") return "PRIMARY_ACTIVE";
  if (operation.targetProfile === "SECONDARY") return "SECONDARY_ACTIVE";
  if (operation.targetProfile === "ADMIN") return "ADMIN_ACTIVE";
  return undefined;
}

export function planSessionRefresh(
  deviceIds: Iterable<string>,
  operations: ReadonlyMap<string, DeviceOperationSnapshot>
): SessionRefreshPlan {
  const refreshIds = new Set<string>();
  const expectedStates = new Map<string, WindowsSessionState>();
  for (const deviceId of new Set(deviceIds)) {
    const operation = operations.get(deviceId);
    if (operation?.state === "IN_PROGRESS") continue;
    refreshIds.add(deviceId);
    const expected = expectedSessionState(operation);
    if (expected) expectedStates.set(deviceId, expected);
  }
  return { deviceIds: refreshIds, expectedStates };
}

/**
 * One bounded recovery pass shared by bootstrap and every manual refresh:
 * hydrate leases, perform the authoritative SESSION read, then re-read leases.
 */
export async function reconcileSessionOperations<T>(
  deviceIds: Iterable<string>,
  refreshOperations: (deviceIds: ReadonlySet<string>) => Promise<ReadonlyMap<string, DeviceOperationSnapshot>>,
  refreshSessions: (
    deviceIds: ReadonlySet<string>,
    expectedStates: ReadonlyMap<string, WindowsSessionState>
  ) => Promise<T>
) {
  const ids = new Set(deviceIds);
  const hydrated = await refreshOperations(ids);
  const plan = planSessionRefresh(ids, hydrated);
  let sessionResult: T | undefined;
  let sessionError: unknown;
  try {
    if (plan.deviceIds.size > 0) {
      sessionResult = await refreshSessions(plan.deviceIds, plan.expectedStates);
    }
  } catch (error) {
    sessionError = error;
  }
  const settledOperations = await refreshOperations(ids);
  if (sessionError !== undefined) throw sessionError;
  return { plan, sessionResult, operations: settledOperations };
}

/** Bounded, post-mutation-only observation window. It never repeats a mutation. */
export async function confirmSessionExpectations(
  deviceIds: ReadonlySet<string>,
  expectedStates: ReadonlyMap<string, WindowsSessionState>,
  sourceStates: ReadonlyMap<string, WindowsSessionState>,
  refresh: (
    deviceIds: ReadonlySet<string>,
    expectedStates: ReadonlyMap<string, WindowsSessionState>
  ) => Promise<SessionExpectationResult>,
  options: {
    signal?: AbortSignal;
    deadlineMs?: number;
    intervalMs?: number;
    scheduler?: SessionReconciliationScheduler;
    isCurrent?: (deviceId: string) => boolean;
  } = {}
): Promise<SessionReconciliationOutcome> {
  const pending = new Set([...deviceIds].filter((deviceId) => expectedStates.has(deviceId)));
  const confirmed = new Set<string>();
  const timedOut = new Set<string>();
  const incompatible = new Set<string>();
  const cancelled = new Set<string>();
  const scheduler = options.scheduler ?? defaultScheduler;
  const deadlineMs = options.deadlineMs ?? SESSION_RECONCILIATION_DEADLINE_MS;
  const intervalMs = options.intervalMs ?? SESSION_RECONCILIATION_INTERVAL_MS;
  const startedAt = scheduler.now();

  while (pending.size > 0) {
    removeCancelled(pending, cancelled, options.signal, options.isCurrent);
    if (pending.size === 0) break;

    const expected = new Map<string, WindowsSessionState>();
    pending.forEach((deviceId) => expected.set(deviceId, expectedStates.get(deviceId)!));
    try {
      const observation = await refresh(new Set(pending), expected);
      for (const deviceId of [...pending]) {
        const observed = observation.observed.get(deviceId);
        if (observed === undefined) continue;
        const target = expectedStates.get(deviceId)!;
        if (observed === target) {
          pending.delete(deviceId);
          confirmed.add(deviceId);
        } else if (!isPotentiallyTransient(observed, sourceStates.get(deviceId))) {
          pending.delete(deviceId);
          incompatible.add(deviceId);
        }
      }
    } catch (error) {
      if (options.signal?.aborted || isAbortError(error)) {
        pending.forEach((deviceId) => cancelled.add(deviceId));
        pending.clear();
        break;
      }
      // A read failure is uncertainty, not evidence that the target was missed.
    }

    removeCancelled(pending, cancelled, options.signal, options.isCurrent);
    if (pending.size === 0) break;
    const elapsed = Math.max(0, scheduler.now() - startedAt);
    if (elapsed >= deadlineMs) {
      pending.forEach((deviceId) => timedOut.add(deviceId));
      pending.clear();
      break;
    }
    try {
      await scheduler.wait(Math.min(intervalMs, deadlineMs - elapsed), options.signal);
    } catch (error) {
      if (!options.signal?.aborted && !isAbortError(error)) throw error;
      pending.forEach((deviceId) => cancelled.add(deviceId));
      pending.clear();
    }
  }

  return {
    confirmedDeviceIds: confirmed,
    timedOutDeviceIds: timedOut,
    incompatibleDeviceIds: incompatible,
    cancelledDeviceIds: cancelled
  };
}

function isPotentiallyTransient(observed: WindowsSessionState, source: WindowsSessionState | undefined) {
  return observed === "UNKNOWN" || observed === "NO_SESSION" || observed === source;
}

function removeCancelled(
  pending: Set<string>,
  cancelled: Set<string>,
  signal?: AbortSignal,
  isCurrent: (deviceId: string) => boolean = () => true
) {
  for (const deviceId of [...pending]) {
    if (signal?.aborted || !isCurrent(deviceId)) {
      pending.delete(deviceId);
      cancelled.add(deviceId);
    }
  }
}

function isAbortError(error: unknown) {
  return error instanceof Error && error.name === "AbortError";
}

const defaultScheduler: SessionReconciliationScheduler = {
  now: () => performance.now(),
  wait: (milliseconds, signal) => new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      reject(abortError());
      return;
    }
    const timer = window.setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, milliseconds);
    const onAbort = () => {
      window.clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
      reject(abortError());
    };
    signal?.addEventListener("abort", onAbort, { once: true });
  })
};

function abortError() {
  const error = new Error("Session reconciliation was cancelled.");
  error.name = "AbortError";
  return error;
}
