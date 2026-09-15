import { DeviceCard } from "../components/DeviceCard";
import { SummaryCard } from "../components/SummaryCard";
import { mockClassroomDashboard } from "../mock/mockClassroomDashboard";

export function ClassroomDashboard() {
  return (
    <section className="classroom-dashboard" aria-labelledby="classroom-title">
      <div className="page-heading">
        <div>
          <p className="page-heading__eyebrow">Vista activa</p>
          <h1 id="classroom-title">Aula</h1>
        </div>
        <span className="mock-badge">Datos temporales locales</span>
      </div>

      <div className="summary-grid" aria-label="Resumen del aula con datos temporales">
        {mockClassroomDashboard.summary.map((metric) => (
          <SummaryCard key={metric.id} metric={metric} />
        ))}
      </div>

      <section className="device-overview" aria-labelledby="device-overview-title">
        <div className="section-heading">
          <div>
            <h2 id="device-overview-title">Equipos del aula</h2>
            <p>Estado visual preparado para operacion por equipo y seleccion futura.</p>
          </div>
        </div>
        <div className="device-grid">
          {mockClassroomDashboard.devices.map((device) => (
            <DeviceCard key={device.id} device={device} />
          ))}
        </div>
      </section>
    </section>
  );
}
