import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ApiError } from "./api/apiClient";
import type { BatchOperationResponse } from "./api/quickActionsApi";
import { toClassroomDashboardData } from "./api/classroomDashboardMapper";
import {
  fetchClassroomSnapshot,
  fetchMasterBootstrap,
  type MasterAuthorization
} from "./api/masterApi";
import { AppShell, type HeaderState } from "./app/layout/AppShell";
import { ClassroomDashboard } from "./pages/ClassroomDashboard";
import type { ClassroomDashboardData, ClassroomOption } from "./types/classroom";

type DashboardState =
  | { status: "loading"; header: HeaderState; activeClassrooms: ClassroomOption[]; selectedClassroomId?: string }
  | {
      status: "error";
      header: HeaderState;
      activeClassrooms: ClassroomOption[];
      selectedClassroomId?: string;
      errorDescription?: string;
    }
  | {
      status: "empty";
      header: HeaderState;
      activeClassrooms: ClassroomOption[];
      selectedClassroomId?: string;
      emptyTitle: string;
      emptyDescription: string;
    }
  | {
      status: "ready";
      header: HeaderState;
      activeClassrooms: ClassroomOption[];
      classroomId: string;
      selectedClassroomId: string;
      data: ClassroomDashboardData;
      isRefreshing: boolean;
      refreshError?: string;
    };

const loadingHeader: HeaderState = {
  classroomName: "Cargando aula",
  masterStatus: "Conectando con Master",
  masterStatusTone: "neutral"
};

export function App() {
  const [activeSection, setActiveSection] = useState("classroom");
  const [dashboardState, setDashboardState] = useState<DashboardState>({
    status: "loading",
    header: loadingHeader,
    activeClassrooms: []
  });
  const [selectedDeviceIds, setSelectedDeviceIds] = useState<Set<string>>(() => new Set());
  const [operationResult, setOperationResult] = useState<BatchOperationResponse | null>(null);
  const [operationError, setOperationError] = useState<string>();
  const refreshAbortRef = useRef<AbortController | null>(null);

  const loadDashboard = useCallback(async (signal?: AbortSignal) => {
    setDashboardState({ status: "loading", header: loadingHeader, activeClassrooms: [] });

    try {
      const bootstrap = await fetchMasterBootstrap(signal);
      const activeClassrooms = bootstrap.classrooms
        .filter((classroom) => classroom.active)
        .map((classroom) => ({
          classroomId: classroom.classroomId,
          displayName: classroom.displayName,
          deviceCount: classroom.counts?.deviceCount
        }));
      const headerBase = headerFromAuthorization(bootstrap.authorization);

      if (activeClassrooms.length === 0) {
        setDashboardState({
          status: "empty",
          header: {
            ...headerBase,
            classroomName: "Sin aula activa"
          },
          activeClassrooms,
          emptyTitle: "No hay aulas activas configuradas",
          emptyDescription: "Cuando exista un aula activa en el Master, aparecera aqui."
        });
        return;
      }

      const selectedClassroom = activeClassrooms[0];
      const snapshot = await fetchClassroomSnapshot(selectedClassroom.classroomId, signal);
      const data = toClassroomDashboardData(snapshot);

      setDashboardState({
        status: "ready",
        header: {
          ...headerBase,
          classroomName: data.classroomName
        },
        activeClassrooms,
        classroomId: selectedClassroom.classroomId,
        selectedClassroomId: selectedClassroom.classroomId,
        data,
        isRefreshing: false
      });
      setSelectedDeviceIds(new Set());
    } catch (error) {
      if (signal?.aborted) {
        return;
      }

      setDashboardState({
        status: "error",
        header: {
          ...loadingHeader,
          classroomName: "Aula"
        },
        activeClassrooms: [],
        errorDescription: readableErrorDescription(error)
      });
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void loadDashboard(controller.signal);

    return () => controller.abort();
  }, [loadDashboard]);

  useEffect(() => {
    return () => refreshAbortRef.current?.abort();
  }, []);

  const refreshSnapshot = useCallback(async () => {
    if (dashboardState.status !== "ready" || dashboardState.isRefreshing) {
      return;
    }

    refreshAbortRef.current?.abort();
    const controller = new AbortController();
    refreshAbortRef.current = controller;

    setDashboardState((current) =>
      current.status === "ready" ? { ...current, isRefreshing: true, refreshError: undefined } : current
    );

    try {
      const snapshot = await fetchClassroomSnapshot(dashboardState.classroomId, controller.signal);
      const data = toClassroomDashboardData(snapshot);

      setDashboardState((current) =>
        current.status === "ready"
          ? {
              ...current,
              header: {
                ...current.header,
                classroomName: data.classroomName
              },
              data,
              isRefreshing: false
            }
          : current
      );
      setSelectedDeviceIds((current) => {
        const availableIds = new Set(data.devices.map((device) => device.id));
        return new Set([...current].filter((deviceId) => availableIds.has(deviceId)));
      });
    } catch (error) {
      if (controller.signal.aborted) {
        return;
      }

      setDashboardState((current) =>
        current.status === "ready"
          ? {
              ...current,
              isRefreshing: false,
              refreshError: readableErrorDescription(error)
            }
          : current
      );
    }
  }, [dashboardState]);

  const selectClassroom = useCallback(
    async (classroomId: string) => {
      const selectedClassroom = dashboardState.activeClassrooms.find(
        (classroom) => classroom.classroomId === classroomId
      );

      if (!selectedClassroom || dashboardState.selectedClassroomId === classroomId) {
        return;
      }

      refreshAbortRef.current?.abort();
      setSelectedDeviceIds(new Set());
      setOperationResult(null);
      setOperationError(undefined);
      const controller = new AbortController();
      refreshAbortRef.current = controller;
      const header = {
        ...dashboardState.header,
        classroomName: selectedClassroom.displayName
      };

      setDashboardState({
        status: "loading",
        header,
        activeClassrooms: dashboardState.activeClassrooms,
        selectedClassroomId: classroomId
      });

      try {
        const snapshot = await fetchClassroomSnapshot(classroomId, controller.signal);
        const data = toClassroomDashboardData(snapshot);

        setDashboardState({
          status: "ready",
          header: {
            ...header,
            classroomName: data.classroomName
          },
          activeClassrooms: dashboardState.activeClassrooms,
          classroomId,
          selectedClassroomId: classroomId,
          data,
          isRefreshing: false
        });
      } catch (error) {
        if (controller.signal.aborted) {
          return;
        }

        setDashboardState({
          status: "error",
          header,
          activeClassrooms: dashboardState.activeClassrooms,
          selectedClassroomId: classroomId,
          errorDescription: readableErrorDescription(error)
        });
      }
    },
    [dashboardState]
  );

  const dashboard = useMemo(() => {
    if (dashboardState.status === "ready") {
      return (
        <ClassroomDashboard
          status="ready"
          data={dashboardState.data}
          isRefreshing={dashboardState.isRefreshing}
          refreshError={dashboardState.refreshError}
          selectedDeviceIds={selectedDeviceIds}
          classroomId={dashboardState.classroomId}
          operationResult={operationResult}
          operationError={operationError}
          onOperationStart={() => { setOperationResult(null); setOperationError(undefined); }}
          onOperationResult={(result) => { setOperationResult(result); setOperationError(undefined); }}
          onOperationError={(message) => { setOperationResult(null); setOperationError(message); }}
          onDismissOperationResult={() => setOperationResult(null)}
          onToggleDevice={(deviceId) =>
            setSelectedDeviceIds((current) => {
              const next = new Set(current);
              if (next.has(deviceId)) {
                next.delete(deviceId);
              } else {
                next.add(deviceId);
              }
              return next;
            })
          }
          onSelectAll={() => setSelectedDeviceIds(new Set(dashboardState.data.devices.map((device) => device.id)))}
          onClearSelection={() => setSelectedDeviceIds(new Set())}
          onRetry={() => void loadDashboard()}
          onRefresh={() => void refreshSnapshot()}
        />
      );
    }

    if (dashboardState.status === "empty") {
      return (
        <ClassroomDashboard
          status="empty"
          emptyTitle={dashboardState.emptyTitle}
          emptyDescription={dashboardState.emptyDescription}
          onRetry={() => void loadDashboard()}
          onRefresh={() => void refreshSnapshot()}
        />
      );
    }

    if (dashboardState.status === "error") {
      return (
        <ClassroomDashboard
          status="error"
          errorDescription={dashboardState.errorDescription}
          onRetry={() => void loadDashboard()}
          onRefresh={() => void refreshSnapshot()}
        />
      );
    }

    return (
      <ClassroomDashboard
        status="loading"
        loadingTitle={dashboardState.header.classroomName}
        onRetry={() => void loadDashboard()}
        onRefresh={() => void refreshSnapshot()}
      />
    );
  }, [dashboardState, loadDashboard, refreshSnapshot, selectedDeviceIds, operationResult, operationError]);

  return (
    <AppShell
      activeSection={activeSection}
      header={dashboardState.header}
      classrooms={dashboardState.activeClassrooms}
      selectedClassroomId={dashboardState.selectedClassroomId}
      onClassroomChange={(classroomId) => void selectClassroom(classroomId)}
      onSectionChange={setActiveSection}
    >
      {dashboard}
    </AppShell>
  );
}

function headerFromAuthorization(authorization: MasterAuthorization): HeaderState {
  return {
    classroomName: "Aula",
    masterStatus: authorization.authorized ? "Master autorizado" : authorization.status,
    masterStatusTone: authorization.authorized ? "success" : "warning",
    currentAccountName: authorization.currentAccountDisplayName ?? undefined
  };
}

function readableErrorDescription(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return "La cuenta actual no esta autorizada para leer el Master.";
    }
    if (error.status === 503) {
      return "El Master Backend o el storage local no estan disponibles.";
    }
  }

  return "Revisa que el Master Backend local este disponible y vuelve a intentar.";
}
