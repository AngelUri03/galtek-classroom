import { LoaderCircle, MonitorCheck, MonitorOff, TriangleAlert, WifiOff } from "lucide-react";
import type { WindowsSessionState } from "../api/quickActionsApi";
import type { ClassroomDeviceCardData, DeviceCardStatus } from "../types/classroom";
import { sessionStateLabels } from "./actionAvailabilityResolver";

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
  selected: boolean;
  sessionState?: WindowsSessionState;
  onToggle: () => void;
};

export function DeviceCard({ device, selected, sessionState, onToggle }: DeviceCardProps) {
  const Icon = statusIcon[device.status];

  const handleKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.key === " " || event.key === "Enter") {
      event.preventDefault();
      onToggle();
    }
  };

  return (
    <article
      className={`device-card device-card--${device.status}${selected ? " device-card--selected" : ""}`}
      tabIndex={0}
      role="checkbox"
      aria-checked={selected}
      aria-label={`${device.label}, ${device.studentName}, ${device.statusLabel}, ${device.secondaryStatus}`}
      onClick={onToggle}
      onKeyDown={handleKeyDown}
    >
      <div className="device-card__topline">
        <div className="device-card__identity">
          <h3>{device.label}</h3>
          <p>{device.studentName}</p>
        </div>
        <div className="device-card__controls">
          <span className="device-card__selection-control" aria-hidden="true">
            {selected ? "✓" : ""}
          </span>
          <span className="device-card__icon" aria-hidden="true">
            <Icon size={19} strokeWidth={2} />
          </span>
        </div>
      </div>
      <div className="device-card__footer">
        <span className="device-card__status">
          <span className="device-card__status-dot" aria-hidden="true" />
          {device.statusLabel}
        </span>
        <small>{device.secondaryStatus}</small>
        {sessionState && device.rawStatus === "ONLINE" ? (
          <small className={`device-card__session device-card__session--${sessionState.toLowerCase().replace(/_/g, "-")}`}>
            {sessionStateLabels[sessionState]}
          </small>
        ) : null}
        <small>{device.note}</small>
      </div>
    </article>
  );
}
