import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { fetchWindowsSessionStates, type WindowsSessionState } from "../api/quickActionsApi";
import {
  beginSessionRefresh,
  commitSessionObservations,
  invalidateSessionRefreshes,
  type SessionSnapshot
} from "./sessionStateModel";
import {
  confirmSessionExpectations,
  type SessionReconciliationOutcome
} from "./sessionReconciliationModel";

export type SessionRefreshResult = {
  observed: ReadonlyMap<string, WindowsSessionState>;
  published: ReadonlyMap<string, WindowsSessionState>;
  rejectedDeviceIds: ReadonlySet<string>;
};

type SessionStateApi = {
  snapshots: ReadonlyMap<string, SessionSnapshot>;
  stateFor: (deviceId: string) => WindowsSessionState | undefined;
  hasSnapshot: (deviceId: string) => boolean;
  isLoading: (deviceId: string) => boolean;
  invalidate: (deviceIds: Iterable<string>) => void;
  refresh: (
    classroomId: string,
    deviceIds: Iterable<string>,
    signal?: AbortSignal,
    expectedStates?: ReadonlyMap<string, WindowsSessionState>
  ) => Promise<SessionRefreshResult>;
  reconcile: (
    classroomId: string,
    expectedStates: ReadonlyMap<string, WindowsSessionState>,
    sourceStates: ReadonlyMap<string, WindowsSessionState>,
    signal?: AbortSignal
  ) => Promise<SessionReconciliationOutcome>;
};

const SessionStateContext = createContext<SessionStateApi | null>(null);

export function SessionStateProvider({ children }: { children: ReactNode }) {
  const [snapshots, setSnapshots] = useState<Map<string, SessionSnapshot>>(() => new Map());
  const [loadingGenerations, setLoadingGenerations] = useState<Map<string, number>>(() => new Map());
  const snapshotsRef = useRef(snapshots);
  const generationsRef = useRef<Map<string, number>>(new Map());
  const reconciliationGenerationsRef = useRef<Map<string, number>>(new Map());
  const loadingRef = useRef(loadingGenerations);

  const invalidate = useCallback((deviceIds: Iterable<string>) => {
    const ids = [...new Set(deviceIds)];
    generationsRef.current = invalidateSessionRefreshes(generationsRef.current, ids);
    reconciliationGenerationsRef.current = invalidateSessionRefreshes(
      reconciliationGenerationsRef.current,
      ids
    );
    const loading = new Map(loadingRef.current);
    ids.forEach((deviceId) => loading.delete(deviceId));
    loadingRef.current = loading;
    setLoadingGenerations(loading);
  }, []);

  const refresh = useCallback(async (
    classroomId: string,
    deviceIds: Iterable<string>,
    signal?: AbortSignal,
    expectedStates: ReadonlyMap<string, WindowsSessionState> = new Map()
  ): Promise<SessionRefreshResult> => {
    const ids = [...new Set(deviceIds)];
    if (ids.length === 0) return { observed: new Map(), published: new Map(), rejectedDeviceIds: new Set() };

    const started = beginSessionRefresh(generationsRef.current, ids);
    generationsRef.current = started.generations;
    const loading = new Map(loadingRef.current);
    started.ticket.forEach((generation, deviceId) => loading.set(deviceId, generation));
    loadingRef.current = loading;
    setLoadingGenerations(loading);

    try {
      const response = await fetchWindowsSessionStates(classroomId, new Set(ids), signal);
      const committed = commitSessionObservations(
        snapshotsRef.current,
        generationsRef.current,
        started.ticket,
        response.targets,
        expectedStates
      );
      snapshotsRef.current = committed.snapshots;
      setSnapshots(committed.snapshots);
      return {
        observed: committed.observed,
        published: committed.published,
        rejectedDeviceIds: committed.rejectedDeviceIds
      };
    } finally {
      const nextLoading = new Map(loadingRef.current);
      started.ticket.forEach((generation, deviceId) => {
        if (nextLoading.get(deviceId) === generation) nextLoading.delete(deviceId);
      });
      loadingRef.current = nextLoading;
      setLoadingGenerations(nextLoading);
    }
  }, []);

  const reconcile = useCallback(async (
    classroomId: string,
    expectedStates: ReadonlyMap<string, WindowsSessionState>,
    sourceStates: ReadonlyMap<string, WindowsSessionState>,
    signal?: AbortSignal
  ) => {
    const ids = [...expectedStates.keys()];
    const started = beginSessionRefresh(reconciliationGenerationsRef.current, ids);
    reconciliationGenerationsRef.current = started.generations;
    return confirmSessionExpectations(
      new Set(ids),
      expectedStates,
      sourceStates,
      (deviceIds, expected) => refresh(classroomId, deviceIds, signal, expected),
      {
        signal,
        isCurrent: (deviceId) => reconciliationGenerationsRef.current.get(deviceId)
          === started.ticket.get(deviceId)
      }
    );
  }, [refresh]);

  const value = useMemo<SessionStateApi>(() => ({
    snapshots,
    stateFor: (deviceId) => snapshots.get(deviceId)?.state,
    hasSnapshot: (deviceId) => snapshots.has(deviceId),
    isLoading: (deviceId) => loadingGenerations.has(deviceId),
    invalidate,
    refresh,
    reconcile
  }), [snapshots, loadingGenerations, invalidate, refresh, reconcile]);

  return <SessionStateContext.Provider value={value}>{children}</SessionStateContext.Provider>;
}

export function useSessionStates() {
  const context = useContext(SessionStateContext);
  if (!context) throw new Error("useSessionStates must be used inside SessionStateProvider.");
  return context;
}
