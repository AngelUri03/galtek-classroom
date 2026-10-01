import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { getJson } from "../api/apiClient";
import {
  beginDeviceOperationRefresh,
  claimDeviceOperations,
  commitDeviceOperationRefresh,
  hasBusyDevice,
  invalidateDeviceOperationRefreshes,
  type DeviceOperationSnapshot
} from "./deviceOperationModel";

export type DeviceOperation = DeviceOperationSnapshot;

type DeviceOperationApi = {
  operations: ReadonlyMap<string, DeviceOperation>;
  operationFor: (deviceId: string) => DeviceOperation | undefined;
  anyBusy: (deviceIds: Iterable<string>) => boolean;
  begin: (deviceIds: Iterable<string>, operationType: string, targetProfile?: string | null) => boolean;
  requireReconciliation: (deviceIds: Iterable<string>) => void;
  markReconciliationTimedOut: (deviceIds: Iterable<string>) => void;
  clear: (deviceIds: Iterable<string>) => void;
  refresh: (deviceIds: Iterable<string>, signal?: AbortSignal) => Promise<ReadonlyMap<string, DeviceOperation>>;
};

const DeviceOperationContext = createContext<DeviceOperationApi | null>(null);

export function DeviceOperationProvider({ children }: { children: ReactNode }) {
  const [operations, setOperations] = useState<Map<string, DeviceOperation>>(() => new Map());
  const operationsRef = useRef(operations);
  const generationsRef = useRef<Map<string, number>>(new Map());

  const invalidateRefreshes = (deviceIds: Iterable<string>) => {
    generationsRef.current = invalidateDeviceOperationRefreshes(generationsRef.current, deviceIds);
  };

  const begin = useCallback((deviceIds: Iterable<string>, operationType: string, targetProfile?: string | null) => {
    const claim = claimDeviceOperations(
      operationsRef.current, deviceIds, operationType, targetProfile, new Date().toISOString()
    );
    if (!claim.accepted) return false;
    invalidateRefreshes(deviceIds);
    operationsRef.current = claim.operations;
    setOperations(claim.operations);
    return claim.accepted;
  }, []);

  const requireReconciliation = useCallback((deviceIds: Iterable<string>) => {
    const ids = [...new Set(deviceIds)];
    invalidateRefreshes(ids);
    const next = new Map(operationsRef.current);
    for (const deviceId of ids) {
      const existing = next.get(deviceId);
      if (existing) next.set(deviceId, {
        ...existing,
        state: "RECONCILIATION_REQUIRED",
        reconciliationStatus: "ACTIVE"
      });
    }
    operationsRef.current = next;
    setOperations(next);
  }, []);

  const markReconciliationTimedOut = useCallback((deviceIds: Iterable<string>) => {
    const next = new Map(operationsRef.current);
    for (const deviceId of new Set(deviceIds)) {
      const existing = next.get(deviceId);
      if (existing?.state === "RECONCILIATION_REQUIRED") {
        next.set(deviceId, { ...existing, reconciliationStatus: "TIMED_OUT" });
      }
    }
    operationsRef.current = next;
    setOperations(next);
  }, []);

  const clear = useCallback((deviceIds: Iterable<string>) => {
    const ids = [...new Set(deviceIds)];
    invalidateRefreshes(ids);
    const next = new Map(operationsRef.current);
    for (const deviceId of ids) next.delete(deviceId);
    operationsRef.current = next;
    setOperations(next);
  }, []);

  const refresh = useCallback(async (deviceIds: Iterable<string>, signal?: AbortSignal) => {
    const ids = [...new Set(deviceIds)];
    if (ids.length === 0) return new Map(operationsRef.current);
    const started = beginDeviceOperationRefresh(generationsRef.current, ids);
    generationsRef.current = started.generations;
    const query = new URLSearchParams();
    ids.forEach((deviceId) => query.append("deviceId", deviceId));
    const response = await getJson<{ devices: DeviceOperation[] }>(`/api/device-operations?${query}`, signal);
    const next = commitDeviceOperationRefresh(
      operationsRef.current,
      generationsRef.current,
      started.ticket,
      response.devices
    );
    operationsRef.current = next;
    setOperations(next);
    const planning = new Map(next);
    response.devices.forEach((operation) => {
      if (!planning.has(operation.deviceId)) planning.set(operation.deviceId, operation);
    });
    return planning;
  }, []);

  const value = useMemo<DeviceOperationApi>(() => ({
    operations,
    operationFor: (deviceId) => operations.get(deviceId),
    anyBusy: (deviceIds) => hasBusyDevice(operations, deviceIds),
    begin, requireReconciliation, markReconciliationTimedOut, clear, refresh
  }), [operations, begin, requireReconciliation, markReconciliationTimedOut, clear, refresh]);

  return <DeviceOperationContext.Provider value={value}>{children}</DeviceOperationContext.Provider>;
}

export function useDeviceOperations() {
  const context = useContext(DeviceOperationContext);
  if (!context) throw new Error("useDeviceOperations must be used inside DeviceOperationProvider.");
  return context;
}
