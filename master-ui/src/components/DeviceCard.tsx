import { MonitorCheck, MonitorDot, MonitorOff, TriangleAlert, WifiOff } from "lucide-react";
import type { MockClassroomDevice, MockDeviceStatus } from "../types/classroom";

const statusLabel: Record<MockDeviceStatus, string> = {
  online: "En linea",
  offline: "Sin conexion",
  inUse: "En uso",
  attention: "Con atencion",
  degraded: "Degradado"
};

const statusIcon: Record<MockDeviceStatus, typeof MonitorCheck> = {
  online: MonitorCheck,
  offline: MonitorOff,
  inUse: MonitorDot,
  attention: TriangleAlert,
  degraded: WifiOff
};

type DeviceCardProps = {
  device: MockClassroomDevice;
};

export function DeviceCard({ device }: DeviceCardProps) {
  const Icon = statusIcon[device.status];

  return (
    <article
      className={`device-card device-card--${device.status}${device.selected ? " device-card--selected" : ""}`}
      tabIndex={0}
      aria-label={`${device.label}, ${device.studentName}, ${statusLabel[device.status]}, ${device.secondaryStatus}`}
    >
      <div className="device-card__topline">
        <div className="device-card__identity">
          <h3>{device.label}</h3>
          <p>{device.studentName}</p>
        </div>
        <span className="device-card__icon" aria-hidden="true">
          <Icon size={19} strokeWidth={2} />
        </span>
      </div>
      <div className="device-card__footer">
        <span className="device-card__status">
          <span className="device-card__status-dot" aria-hidden="true" />
          {statusLabel[device.status]}
        </span>
        <small>{device.secondaryStatus}</small>
        <small>{device.note}</small>
      </div>
    </article>
  );
}
