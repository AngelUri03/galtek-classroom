import { useEffect, useRef, useState } from "react";
import { Link, LockKeyhole, Power, RotateCcw, UnlockKeyhole } from "lucide-react";
import { ApiError } from "../api/apiClient";
import {
  openUrl,
  powerControl,
  setInputLocked,
  type BatchOperationResponse,
  type QuickActionType
} from "../api/quickActionsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";

type Intent = {
  type: "OPEN_URL" | "RESTART" | "SHUTDOWN";
  classroomId: string;
  classroomName: string;
  targetDeviceIds: Set<string>;
  targetNames: string[];
};

type Props = {
  classroomId: string;
  classroomName: string;
  devices: ClassroomDeviceCardData[];
  selectedDeviceIds: Set<string>;
  onClearSelection?: () => void;
  onStart: () => void;
  onResult: (result: BatchOperationResponse) => void;
  onError: (message: string) => void;
};

const actionName: Record<QuickActionType, string> = {
  OPEN_URL: "Abrir URL", LOCK_INPUT: "Bloquear", UNLOCK_INPUT: "Desbloquear",
  RESTART: "Reiniciar", SHUTDOWN: "Apagar"
};

function sameTargets(targets: ReadonlySet<string>, selected: ReadonlySet<string>) {
  if (targets.size !== selected.size) return false;
  for (const id of targets) {
    if (!selected.has(id)) return false;
  }
  return true;
}

export function QuickActions({ classroomId, classroomName, devices, selectedDeviceIds,
  onClearSelection, onStart, onResult, onError }: Props) {
  const [intent, setIntent] = useState<Intent | null>(null);
  const [url, setUrl] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const controllerRef = useRef<AbortController | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);
  const openerRef = useRef<HTMLButtonElement | null>(null);
  const urlInputRef = useRef<HTMLInputElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => () => controllerRef.current?.abort(), []);

  useEffect(() => {
    if (intent && (intent.classroomId !== classroomId ||
      !sameTargets(intent.targetDeviceIds, selectedDeviceIds))) {
      setIntent(null);
    }
  }, [classroomId, intent, selectedDeviceIds]);

  useEffect(() => {
    if (!intent) return;
    const dialog = dialogRef.current;
    dialog?.showModal();
    (intent.type === "OPEN_URL" ? urlInputRef.current : cancelRef.current)?.focus();
    return () => {
      dialog?.close();
      if (openerRef.current?.isConnected) openerRef.current.focus();
    };
  }, [intent]);

  if (selectedDeviceIds.size === 0) return null;

  const targetCount = selectedDeviceIds.size;
  const countLabel = `${targetCount} ${targetCount === 1 ? "equipo seleccionado" : "equipos seleccionados"}`;

  const captureTargets = () => {
    const targetDeviceIds = new Set(selectedDeviceIds);
    const targetNames: string[] = [];
    targetDeviceIds.forEach((id) => targetNames.push(devices.find((device) => device.id === id)?.label ?? "Equipo"));
    return {
      targetDeviceIds,
      targetNames
    };
  };

  const openIntent = (type: Intent["type"], opener: HTMLButtonElement) => {
    if (submittingRef.current) return;
    openerRef.current = opener;
    setUrl("");
    setIntent({ type, classroomId, classroomName, ...captureTargets() });
  };

  const dispatch = async (type: QuickActionType, targetClassroomId: string,
    targetDeviceIds: ReadonlySet<string>, nextUrl?: string) => {
    if (submittingRef.current) return;
    submittingRef.current = true;
    setSubmitting(true);
    onStart();
    const controller = new AbortController();
    controllerRef.current = controller;
    try {
      const result = type === "OPEN_URL"
        ? await openUrl(targetClassroomId, targetDeviceIds, nextUrl!, controller.signal)
        : type === "LOCK_INPUT" || type === "UNLOCK_INPUT"
          ? await setInputLocked(targetClassroomId, targetDeviceIds, type === "LOCK_INPUT", controller.signal)
          : await powerControl(targetClassroomId, targetDeviceIds, type, controller.signal);
      if (!controller.signal.aborted) onResult(result);
    } catch (error) {
      if (!controller.signal.aborted) {
        onError(error instanceof ApiError ? error.message : "No pudimos enviar la operación. Inténtalo de nuevo.");
      }
    } finally {
      if (controllerRef.current === controller) controllerRef.current = null;
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  const submitIntent = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!intent || submittingRef.current || intent.classroomId !== classroomId ||
      !sameTargets(intent.targetDeviceIds, selectedDeviceIds)) return;
    const trimmedUrl = url.trim();
    if (intent.type === "OPEN_URL" && !trimmedUrl) return;
    void dispatch(intent.type, intent.classroomId, intent.targetDeviceIds, trimmedUrl);
    setIntent(null);
  };

  const targetDescription = intent ? intent.targetNames.slice(0, 3).join(", ") +
    (intent.targetNames.length > 3 ? ` y ${intent.targetNames.length - 3} más` : "") : "";

  return (
    <>
      <div className="selection-summary quick-actions" role="group"
        aria-label="Acciones para equipos seleccionados" aria-busy={submitting}>
        <div className="quick-actions__selection">
          <strong aria-live="polite">{countLabel}</strong>
          <button className="selection-action selection-action--clear" type="button"
            onClick={onClearSelection} disabled={submitting}>Limpiar selección</button>
        </div>
        <div className="quick-actions__buttons">
          <button type="button" disabled={submitting} onClick={(event) => openIntent("OPEN_URL", event.currentTarget)}><Link size={16} aria-hidden="true" />Abrir URL</button>
          <button type="button" disabled={submitting} onClick={() => void dispatch("LOCK_INPUT", classroomId, captureTargets().targetDeviceIds)}><LockKeyhole size={16} aria-hidden="true" />Bloquear</button>
          <button type="button" disabled={submitting} onClick={() => void dispatch("UNLOCK_INPUT", classroomId, captureTargets().targetDeviceIds)}><UnlockKeyhole size={16} aria-hidden="true" />Desbloquear</button>
          <button type="button" disabled={submitting} onClick={(event) => openIntent("RESTART", event.currentTarget)}><RotateCcw size={16} aria-hidden="true" />Reiniciar</button>
          <button type="button" disabled={submitting} onClick={(event) => openIntent("SHUTDOWN", event.currentTarget)}><Power size={16} aria-hidden="true" />Apagar</button>
        </div>
        {submitting ? <span className="quick-actions__progress" role="status">Enviando…</span> : null}
      </div>
      {intent ? (
        <dialog ref={dialogRef} className="action-dialog" aria-labelledby="action-dialog-title"
          aria-describedby="action-dialog-description"
          onCancel={(event) => { event.preventDefault(); setIntent(null); }}>
          <form onSubmit={submitIntent}>
            <p className="action-dialog__eyebrow">Galtek Classroom</p>
            <h2 id="action-dialog-title">{intent.type === "OPEN_URL" ? "Abrir URL" :
              `¿${actionName[intent.type]} ${intent.targetDeviceIds.size} ${intent.targetDeviceIds.size === 1 ? "equipo" : "equipos"}?`}</h2>
            <p id="action-dialog-description">Aula: {intent.classroomName}. {intent.targetDeviceIds.size} {intent.targetDeviceIds.size === 1 ? "equipo objetivo" : "equipos objetivo"}: {targetDescription}.</p>
            {intent.type === "SHUTDOWN" ? <p className="action-dialog__warning">Apagar puede cerrar trabajo no guardado.</p> : null}
            {intent.type === "OPEN_URL" ? (
              <label className="action-dialog__field">URL
                <input ref={urlInputRef} type="text" inputMode="url" value={url} onChange={(event) => setUrl(event.target.value)}
                  placeholder="https://ejemplo.com/material" required />
              </label>
            ) : null}
            <div className="action-dialog__footer">
              <button ref={cancelRef} type="button" onClick={() => setIntent(null)}>Cancelar</button>
              <button type="submit" className={intent.type === "SHUTDOWN" ? "action-dialog__confirm action-dialog__confirm--danger" : "action-dialog__confirm"}
                disabled={intent.type === "OPEN_URL" && !url.trim()}>{intent.type === "OPEN_URL" ? "Abrir" : actionName[intent.type]}</button>
            </div>
          </form>
        </dialog>
      ) : null}
    </>
  );
}
