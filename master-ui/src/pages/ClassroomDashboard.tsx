import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCw } from "lucide-react";
import { DeviceCard } from "../components/DeviceCard";
import { QuickActions } from "../components/QuickActions";
import { SummaryCard } from "../components/SummaryCard";
import { type BatchOperationResponse } from "../api/quickActionsApi";
import type { ClassroomDashboardData } from "../types/classroom";
import { useDeviceOperations } from "../app/DeviceOperationState";
import { useSessionStates } from "../app/SessionState";
import { reconcileSessionOperations } from "../app/sessionReconciliationModel";

type ClassroomDashboardProps = {
  data?: ClassroomDashboardData;
  status: "loading" | "ready" | "empty" | "error";
  loadingTitle?: string;
  emptyTitle?: string;
  emptyDescription?: string;
  errorTitle?: string;
  errorDescription?: string;
  refreshError?: string;
  isRefreshing?: boolean;
  selectedDeviceIds?: Set<string>;
  classroomId?: string;
  operationResult?: BatchOperationResponse | null;
  operationError?: string;
  onOperationStart?: () => void;
  onOperationResult?: (result: BatchOperationResponse) => void;
  onOperationError?: (message: string) => void;
  onDismissOperationResult?: () => void;
  onToggleDevice?: (deviceId: string) => void;
  onSelectAll?: () => void;
  onClearSelection?: () => void;
  onRetry: () => void;
  onRefresh: () => void;
};

export function ClassroomDashboard({
  data,
  status,
  loadingTitle = "Aula",
  emptyTitle = "Aun no hay equipos en esta aula",
  emptyDescription = "Los equipos registrados apareceran aqui.",
  errorTitle = "No pudimos cargar el aula",
  errorDescription = "Revisa que el Master Backend local este disponible y vuelve a intentar.",
  refreshError,
  isRefreshing = false,
  selectedDeviceIds = new Set(),
  classroomId,
  operationResult,
  operationError,
  onOperationStart,
  onOperationResult,
  onOperationError,
  onDismissOperationResult,
  onToggleDevice,
  onSelectAll,
  onClearSelection,
  onRetry,
  onRefresh
}: ClassroomDashboardProps) {
  const title = data?.classroomName ?? loadingTitle;
  const deviceOperations = useDeviceOperations();
  const sessionStates = useSessionStates();
  const [sessionRefreshing, setSessionRefreshing] = useState(false);
  const onlineDeviceKey = useMemo(() => data?.devices
    .filter((device) => device.rawStatus === "ONLINE")
    .map((device) => device.id)
    .sort()
    .join("\u0000") ?? "", [data?.devices]);
  const onlineDeviceIds = useMemo(
    () => onlineDeviceKey ? onlineDeviceKey.split("\u0000") : [],
    [onlineDeviceKey]
  );
  const refreshSessionAuthority = useCallback(async (signal?: AbortSignal) => {
    if (!classroomId || onlineDeviceIds.length === 0) return;
    await reconcileSessionOperations(
      onlineDeviceIds,
      (deviceIds) => deviceOperations.refresh(deviceIds, signal),
      (deviceIds, expectedStates) => sessionStates.refresh(
        classroomId,
        deviceIds,
        signal,
        expectedStates
      )
    );
  }, [classroomId, deviceOperations.refresh, onlineDeviceIds, sessionStates.refresh]);

  useEffect(() => {
    if (status !== "ready" || !classroomId || onlineDeviceIds.length === 0) return;
    const controller = new AbortController();
    void refreshSessionAuthority(controller.signal).catch(() => undefined);
    return () => controller.abort();
  }, [classroomId, onlineDeviceKey, refreshSessionAuthority, status]);

  const handleManualRefresh = useCallback(async () => {
    setSessionRefreshing(true);
    try {
      await onRefresh();
      await refreshSessionAuthority();
    } finally {
      setSessionRefreshing(false);
    }
  }, [onRefresh, refreshSessionAuthority]);

  const refreshPending = isRefreshing || sessionRefreshing;

  return (
    <section className="classroom-dashboard" aria-labelledby="classroom-title">
      <div className="page-heading">
        <div>
          <p className="page-heading__eyebrow">Vista activa</p>
          <h1 id="classroom-title">{title}</h1>
        </div>
        {status === "ready" ? (
          <button
            className="refresh-button"
            type="button"
            aria-label="Actualizar snapshot del aula"
            aria-busy={refreshPending}
            disabled={refreshPending}
            onClick={() => void handleManualRefresh()}
          >
            <RefreshCw size={17} aria-hidden="true" />
            <span>{refreshPending ? "Actualizando" : "Actualizar"}</span>
          </button>
        ) : null}
      </div>

      {status === "loading" ? <ClassroomLoadingState /> : null}

      {status === "error" ? (
        <ClassroomStatePanel
          title={errorTitle}
          description={errorDescription}
          actionLabel="Reintentar"
          onAction={onRetry}
        />
      ) : null}

      {status === "empty" ? <ClassroomStatePanel title={emptyTitle} description={emptyDescription} /> : null}

      {status === "ready" && refreshError ? (
        <p className="refresh-notice" role="status" aria-live="polite">
          No pudimos actualizar el aula. {refreshError}
        </p>
      ) : null}

      {status === "ready" && data ? (
        <ClassroomContent
          data={data}
          selectedDeviceIds={selectedDeviceIds}
          classroomId={classroomId}
          operationResult={operationResult}
          operationError={operationError}
          onOperationStart={onOperationStart}
          onOperationResult={onOperationResult}
          onOperationError={onOperationError}
          onDismissOperationResult={onDismissOperationResult}
          onToggleDevice={onToggleDevice}
          onSelectAll={onSelectAll}
          onClearSelection={onClearSelection}
          onSnapshotRefresh={onRefresh}
        />
      ) : null}
    </section>
  );
}

function ClassroomContent({
  data,
  selectedDeviceIds,
  classroomId,
  operationResult,
  operationError,
  onOperationStart,
  onOperationResult,
  onOperationError,
  onDismissOperationResult,
  onToggleDevice,
  onSelectAll,
  onClearSelection
  , onSnapshotRefresh
}: {
  data: ClassroomDashboardData;
  selectedDeviceIds: Set<string>;
  classroomId?: string;
  operationResult?: BatchOperationResponse | null;
  operationError?: string;
  onOperationStart?: () => void;
  onOperationResult?: (result: BatchOperationResponse) => void;
  onOperationError?: (message: string) => void;
  onDismissOperationResult?: () => void;
  onToggleDevice?: (deviceId: string) => void;
  onSelectAll?: () => void;
  onClearSelection?: () => void;
  onSnapshotRefresh?: () => Promise<void> | void;
}) {
  const deviceOperations = useDeviceOperations();
  const sessionStates = useSessionStates();

  if (data.devices.length === 0) {
    return (
      <>
        <div className="summary-grid" aria-label="Resumen del aula">
          {data.summary.map((metric) => (
            <SummaryCard key={metric.id} metric={metric} />
          ))}
        </div>
        <ClassroomStatePanel
          title="Aun no hay equipos en esta aula"
          description="Los equipos registrados apareceran aqui."
        />
      </>
    );
  }

  return (
    <>
      <div className="summary-grid" aria-label="Resumen del aula">
        {data.summary.map((metric) => (
          <SummaryCard key={metric.id} metric={metric} />
        ))}
      </div>
      <section className="device-overview" aria-labelledby="device-overview-title">
        <div className="section-heading">
          <div>
            <h2 id="device-overview-title">Equipos del aula</h2>
            <p>{data.devices.length} equipos registrados</p>
          </div>
          <button
            className="selection-action"
            type="button"
            onClick={selectedDeviceIds.size > 0 ? onClearSelection : onSelectAll}
          >
            {selectedDeviceIds.size > 0 ? "Limpiar selección" : "Seleccionar todos"}
          </button>
        </div>
        {classroomId && onOperationStart && onOperationResult && onOperationError ? (
          <QuickActions key={classroomId} classroomId={classroomId} classroomName={data.classroomName}
            groups={data.groups} devices={data.devices} selectedDeviceIds={selectedDeviceIds}
            operationResult={operationResult} onDismissOperationResult={onDismissOperationResult}
            onToggleDevice={onToggleDevice} onSelectAll={onSelectAll} onClearSelection={onClearSelection}
            onSnapshotRefresh={onSnapshotRefresh}
            onStart={onOperationStart} onResult={onOperationResult} onError={onOperationError} />
        ) : null}
        {operationError ? <p className="operation-feedback operation-feedback--error" role="alert">{operationError}</p> : null}
        <div className="device-grid">
          {data.devices.map((device) => (
            <DeviceCard
              key={device.id}
              device={device}
              selected={selectedDeviceIds.has(device.id)}
              sessionState={sessionStates.stateFor(device.id)}
              operation={deviceOperations.operationFor(device.id)}
              onToggle={() => onToggleDevice?.(device.id)}
            />
          ))}
        </div>
      </section>
    </>
  );
}

function ClassroomLoadingState() {
  return (
    <>
      <div className="summary-grid" aria-hidden="true">
        {["devices", "online", "assigned", "free"].map((item) => (
          <div key={item} className="summary-card summary-card--skeleton" />
        ))}
      </div>
      <div className="device-grid" aria-hidden="true">
        {["pc-1", "pc-2", "pc-3", "pc-4", "pc-5", "pc-6"].map((item) => (
          <div key={item} className="device-card device-card--skeleton" />
        ))}
      </div>
    </>
  );
}

function ClassroomStatePanel({
  title,
  description,
  actionLabel,
  onAction
}: {
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
}) {
  return (
    <div className="classroom-state" role="status" aria-live="polite">
      <h2>{title}</h2>
      <p>{description}</p>
      {actionLabel && onAction ? (
        <button type="button" className="state-button" onClick={onAction}>
          {actionLabel}
        </button>
      ) : null}
    </div>
  );
}
