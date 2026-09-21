import { RefreshCw } from "lucide-react";
import { DeviceCard } from "../components/DeviceCard";
import { SummaryCard } from "../components/SummaryCard";
import type { ClassroomDashboardData } from "../types/classroom";

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
  onRetry,
  onRefresh
}: ClassroomDashboardProps) {
  const title = data?.classroomName ?? loadingTitle;

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
            aria-busy={isRefreshing}
            disabled={isRefreshing}
            onClick={onRefresh}
          >
            <RefreshCw size={17} aria-hidden="true" />
            <span>{isRefreshing ? "Actualizando" : "Actualizar"}</span>
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

      {status === "ready" && data ? <ClassroomContent data={data} /> : null}
    </section>
  );
}

function ClassroomContent({ data }: { data: ClassroomDashboardData }) {
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
        </div>
        <div className="device-grid">
          {data.devices.map((device) => (
            <DeviceCard key={device.id} device={device} />
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
