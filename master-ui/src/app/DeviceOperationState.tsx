import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { getJson } from "../api/apiClient";
import { claimDeviceOperations, hasBusyDevice, replaceDeviceOperations, type DeviceOperationSnapshot } from "./deviceOperationModel";

export type DeviceOperation = DeviceOperationSnapshot;

type DeviceOperationApi = {
  operations: ReadonlyMap<string, DeviceOperation>;
  operationFor: (deviceId: string) => DeviceOperation | undefined;
  anyBusy: (deviceIds: Iterable<string>) => boolean;
  begin: (deviceIds: Iterable<string>, operationType: string, targetProfile?: string | null) => boolean;
  requireReconciliation: (deviceIds: Iterable<string>) => void;
  clear: (deviceIds: Iterable<string>) => void;
  refresh: (deviceIds: Iterable<string>, signal?: AbortSignal) => Promise<void>;
};

const DeviceOperationContext = createContext<DeviceOperationApi | null>(null);

export function DeviceOperationProvider({ children }: { children: ReactNode }) {
  const [operations, setOperations] = useState<Map<string, DeviceOperation>>(() => new Map());
  const operationsRef = useRef(operations);

  const begin = useCallback((deviceIds: Iterable<string>, operationType: string, targetProfile?: string | null) => {
    const claim = claimDeviceOperations(
      operationsRef.current, deviceIds, operationType, targetProfile, new Date().toISOString()
    );
    if (!claim.accepted) return false;
    operationsRef.current = claim.operations;
    setOperations(claim.operations);
    return claim.accepted;
  }, []);

  const requireReconciliation = useCallback((deviceIds: Iterable<string>) => {
    setOperations((current) => {
      const next = new Map(current);
      for (const deviceId of deviceIds) {
        const existing = next.get(deviceId);
        if (existing) next.set(deviceId, { ...existing, state: "RECONCILIATION_REQUIRED" });
      }
      operationsRef.current = next;
      return next;
    });
  }, []);

  const clear = useCallback((deviceIds: Iterable<string>) => {
    setOperations((current) => {
      const next = new Map(current);
      for (const deviceId of deviceIds) next.delete(deviceId);
      operationsRef.current = next;
      return next;
    });
  }, []);

  const refresh = useCallback(async (deviceIds: Iterable<string>, signal?: AbortSignal) => {
    const ids = [...new Set(deviceIds)];
    if (ids.length === 0) return;
    const query = new URLSearchParams();
    ids.forEach((deviceId) => query.append("deviceId", deviceId));
    const response = await getJson<{ devices: DeviceOperation[] }>(`/api/device-operations?${query}`, signal);
    const server = new Map(response.devices.map((operation) => [operation.deviceId, operation]));
    setOperations((current) => {
      const next = replaceDeviceOperations(current, ids, server.values());
      operationsRef.current = next;
      return next;
    });
  }, []);

  const value = useMemo<DeviceOperationApi>(() => ({
    operations,
    operationFor: (deviceId) => operations.get(deviceId),
    anyBusy: (deviceIds) => hasBusyDevice(operations, deviceIds),
    begin, requireReconciliation, clear, refresh
  }), [operations, begin, requireReconciliation, clear, refresh]);

  return <DeviceOperationContext.Provider value={value}>{children}</DeviceOperationContext.Provider>;
}

export function useDeviceOperations() {
  const context = useContext(DeviceOperationContext);
  if (!context) throw new Error("useDeviceOperations must be used inside DeviceOperationProvider.");
  return context;
}
