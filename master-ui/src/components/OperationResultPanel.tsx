import {
  AlertTriangle,
  Check,
  CircleHelp,
  Clock3,
  Link,
  LockKeyhole,
  Minus,
  Power,
  RotateCcw,
  LogIn,
  LogOut,
  UnlockKeyhole,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import type { LucideIcon } from "lucide-react";
import type { BatchOperationResult, OperationActionType } from "../api/quickActionsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";
import { toOperationResultView, type OperationResultTargetView } from "./quickActionResultViewModel";
import { DeviceInspector } from "./DeviceInspector";
import { GaltekCloseButton } from "./FormControls";

type Props = {
  classroomId: string;
  result: BatchOperationResult;
  devices: ClassroomDeviceCardData[];
  onDismiss: () => void;
  drawerOpen: boolean;
  onDrawerChange: (open: boolean) => void;
};

const actionIcons: Record<OperationActionType, LucideIcon> = {
  OPEN_URL: Link,
  LOCK_INPUT: LockKeyhole,
  UNLOCK_INPUT: UnlockKeyhole,
  RESTART: RotateCcw,
  SHUTDOWN: Power,
  SWITCH_MANAGED_ACCOUNT: LogIn,
  LOGOFF_WINDOWS_SESSION: LogOut
};

export function OperationResultPanel({ classroomId, result, devices, onDismiss, drawerOpen, onDrawerChange }: Props) {
  const [inspectedDeviceId, setInspectedDeviceId] = useState<string | null>(null);
  const detailTriggerRef = useRef<HTMLButtonElement>(null);
  const drawerRef = useRef<HTMLElement>(null);
  const view = toOperationResultView(result, devices);
  const ActionIcon = actionIcons[view.actionType];
  const inspectedDevice = inspectedDeviceId
    ? devices.find((device) => device.id === inspectedDeviceId) ?? null
    : null;

  useEffect(() => {
    if (drawerOpen) drawerRef.current?.focus();
    else if (detailTriggerRef.current?.isConnected) detailTriggerRef.current.focus();
  }, [drawerOpen]);

  return (
    <>
      <section className={`operation-result operation-result--${view.tone}`} aria-labelledby="operation-result-title">
        <div className="operation-result__header">
          <div className="operation-result__title">
            <span className="operation-result__action-icon" aria-hidden="true">
              <ActionIcon size={17} aria-hidden="true" />
            </span>
            <div>
              <p className="operation-result__eyebrow">Última acción</p>
              <h3 id="operation-result-title">{view.actionName}</h3>
            </div>
          </div>
          <div className="operation-result__summary" aria-live="polite">
            <strong>{view.globalText}</strong>
            <span>{view.successSummary}</span>
            <span>{view.problemSummary}</span>
          </div>
          <div className="operation-result__actions">
            <button ref={detailTriggerRef} type="button" onClick={() => onDrawerChange(true)}>Ver detalle</button>
            <GaltekCloseButton ariaLabel="Cerrar resultado" onClick={onDismiss} />
          </div>
        </div>
      </section>
      {drawerOpen ? (
        <div className="result-drawer-shell" role="presentation" onKeyDown={(event) => {
          if (event.key === "Escape") {
            event.stopPropagation();
            if (inspectedDeviceId) setInspectedDeviceId(null);
            else onDrawerChange(false);
          }
          if (event.key === "Tab") trapFocus(event, drawerRef.current);
        }}>
          <button className="result-drawer-shell__backdrop" type="button" aria-label="Cerrar detalle"
            onClick={() => { onDrawerChange(false); setInspectedDeviceId(null); }} />
          <aside ref={drawerRef} className="result-drawer" role="dialog" aria-modal="true" aria-labelledby="result-drawer-title" tabIndex={-1}>
            <div className="result-drawer__header">
              <div>
                <p className="operation-result__eyebrow">Detalle</p>
                <h2 id="result-drawer-title">{view.actionName}</h2>
              </div>
              <GaltekCloseButton ariaLabel="Cerrar detalle"
                onClick={() => { onDrawerChange(false); setInspectedDeviceId(null); }} />
            </div>
            {inspectedDevice ? (
              <DeviceInspector classroomId={classroomId} device={inspectedDevice} onBack={() => setInspectedDeviceId(null)} />
            ) : (
              <>
                <section className="result-drawer__section" aria-labelledby="result-summary-title">
                  <h3 id="result-summary-title">Resumen</h3>
                  <div className="result-drawer__summary">
                    <strong>{view.globalText}</strong>
                    <span>{view.successSummary}</span>
                    <span>{view.problemSummary}</span>
                  </div>
                </section>
                <section className="result-drawer__section" aria-labelledby="result-targets-title">
                  <h3 id="result-targets-title">Equipos</h3>
                  <div className="result-drawer__targets">
                    {view.targets.map((target) => (
                      <button key={target.deviceId} type="button" onClick={() => setInspectedDeviceId(target.deviceId)}>
                        <TargetResultRow target={target} />
                      </button>
                    ))}
                  </div>
                </section>
              </>
            )}
          </aside>
        </div>
      ) : null}
    </>
  );
}

function TargetResultRow({ target }: { target: OperationResultTargetView }) {
  const StatusIcon = statusIcon(target.tone);

  return (
    <div className={`operation-result__target operation-result__target--${target.tone}`} role="listitem">
      <span className="operation-result__status-icon" aria-hidden="true">
        <StatusIcon size={14} aria-hidden="true" />
      </span>
      <div className="operation-result__target-copy">
        <strong>{target.deviceName}</strong>
        <span>{target.resultText}</span>
      </div>
    </div>
  );
}

function statusIcon(tone: OperationResultTargetView["tone"]) {
  if (tone === "success") return Check;
  if (tone === "unknown") return CircleHelp;
  if (tone === "pending") return Clock3;
  if (tone === "problem") return AlertTriangle;
  return Minus;
}

function trapFocus(event: React.KeyboardEvent, container: HTMLElement | null) {
  if (!container) return;
  const focusable = Array.from(container.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])'
  ));
  if (focusable.length === 0) return;
  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
}
