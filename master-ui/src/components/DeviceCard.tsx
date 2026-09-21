import { LoaderCircle, MonitorCheck, MonitorOff, TriangleAlert, WifiOff } from "lucide-react";
import type { ClassroomDeviceCardData, DeviceCardStatus } from "../types/classroom";

const statusIcon: Record<DeviceCardStatus, typeof MonitorCheck> = {
  online: MonitorCheck,
  offline: MonitorOff,
  connecting: LoaderCircle,
  unavailable: WifiOff,
  busy: WifiOff,
  error: TriangleAlert
};

type DeviceCardProps = {
  device: ClassroomDeviceCardData;
};

export function DeviceCard({ device }: DeviceCardProps) {
  const Icon = statusIcon[device.status];

  return (
    <article
      className={`device-card device-card--${device.status}`}
      tabIndex={0}
      aria-label={`${device.label}, ${device.studentName}, ${device.statusLabel}, ${device.secondaryStatus}`}
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
          {device.statusLabel}
        </span>
        <small>{device.secondaryStatus}</small>
        <small>{device.note}</small>
      </div>
    </article>
  );
}
