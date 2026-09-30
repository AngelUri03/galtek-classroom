import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Check, ChevronDown, ChevronRight, CircleAlert, Keyboard, Link, LoaderCircle, LogIn, MousePointer2, Power, RotateCcw, Search, UserRound, WifiOff } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { ApiError } from "../api/apiClient";
import { authorizeAdminSession, fetchWindowsSessionStates, openUrl, powerControl, setInputLocked, switchManagedAccount, type BatchOperationResponse, type ManagedSessionRole, type QuickActionType, type WindowsSessionState } from "../api/quickActionsApi";
import { fetchManagedAccounts, type ManagedAccountStatusResponse } from "../api/windowsAccountsApi";
import type { ClassroomDeviceCardData, ClassroomGroupData } from "../types/classroom";
import { resolveActionAvailability, sessionStateLabels, type ActionAvailability, type DeviceSessionContext } from "./actionAvailabilityResolver";
import { OperationResultPanel } from "./OperationResultPanel";
import { DeviceInspector } from "./DeviceInspector";
import { resolveManagedSessionEligibility, type ManagedSessionEligibility } from "./managedSessionEligibility";
import { FormPassword, GaltekCloseButton } from "./FormControls";
import { useAppToast } from "../app/AppToastProvider";
import { useDeviceOperations } from "../app/DeviceOperationState";
import { deviceOperationLabel } from "../app/deviceOperationModel";

type Intent = { type: "OPEN_URL" | "RESTART" | "SHUTDOWN"; classroomId: string; targetDeviceIds: Set<string>; availability: ActionAvailability };
type Overlay = "selector" | "eligibility" | "session" | "session-stepup" | "inspector" | "intent" | "result" | null;
type Props = {
  classroomId: string; classroomName: string; groups: ClassroomGroupData[]; devices: ClassroomDeviceCardData[];
  selectedDeviceIds: Set<string>; operationResult?: BatchOperationResponse | null; onDismissOperationResult?: () => void;
  onToggleDevice?: (deviceId: string) => void; onSelectAll?: () => void; onClearSelection?: () => void;
  onSessionStatesChange?: (states: Map<string, WindowsSessionState>) => void;
  onSnapshotRefresh?: () => Promise<void> | void;
  onStart: () => void; onResult: (result: BatchOperationResponse) => void; onError: (message: string) => void;
};

const actionName: Record<QuickActionType, string> = { OPEN_URL: "Abrir URL", LOCK_INPUT: "Bloquear teclado y mouse", UNLOCK_INPUT: "Desbloquear teclado y mouse", RESTART: "Reiniciar", SHUTDOWN: "Apagar" };
const actionIcons: Record<QuickActionType, LucideIcon> = { OPEN_URL: Link, LOCK_INPUT: Keyboard, UNLOCK_INPUT: MousePointer2, RESTART: RotateCcw, SHUTDOWN: Power };
const actionDescriptions: Record<QuickActionType, string> = {
  OPEN_URL: "Abre una página web en los equipos seleccionados.", LOCK_INPUT: "Impide temporalmente la interacción del alumno.",
  UNLOCK_INPUT: "Restaura teclado y mouse.", RESTART: "Solicita un reinicio a Windows.", SHUTDOWN: "Solicita el apagado a Windows."
};

function sameTargets(targets: ReadonlySet<string>, selected: ReadonlySet<string>) { if (targets.size !== selected.size) return false; for (const id of targets) if (!selected.has(id)) return false; return true; }
function interactiveSession(state: WindowsSessionState) { return state === "PRIMARY_ACTIVE" || state === "SECONDARY_ACTIVE" || state === "ADMIN_ACTIVE" || state === "OTHER_SESSION_ACTIVE"; }

export function QuickActions({ classroomId, classroomName, groups, devices, selectedDeviceIds, operationResult, onDismissOperationResult, onToggleDevice, onSelectAll, onClearSelection, onSessionStatesChange, onSnapshotRefresh, onStart, onResult, onError }: Props) {
  const [intent, setIntent] = useState<Intent | null>(null);
  const [url, setUrl] = useState("");
  const [urlTouched, setUrlTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const [overlay, setOverlay] = useState<Overlay>(null);
  const [selectorSearch, setSelectorSearch] = useState("");
  const [eligibility, setEligibility] = useState<ActionAvailability | null>(null);
  const [sessionByDeviceId, setSessionByDeviceId] = useState<Map<string, WindowsSessionState>>(() => new Map());
  const [sessionLoading, setSessionLoading] = useState(false);
  const [managedByDeviceId, setManagedByDeviceId] = useState<Map<string, ManagedAccountStatusResponse>>(() => new Map());
  const [managedLoading, setManagedLoading] = useState(false);
  const [sessionTargetRole, setSessionTargetRole] = useState<ManagedSessionRole | null>(null);
  const [masterPassword, setMasterPassword] = useState("");
  const [sessionError, setSessionError] = useState<string | null>(null);
  const appToast = useAppToast();
  const deviceOperations = useDeviceOperations();
  const submittingRef = useRef(false);
  const controllerRef = useRef<AbortController | null>(null);
  const sessionControllerRef = useRef<AbortController | null>(null);
  const managedControllerRef = useRef<AbortController | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);
  const openerRef = useRef<HTMLButtonElement | null>(null);
  const urlInputRef = useRef<HTMLInputElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const selectorRef = useRef<HTMLElement>(null);
  const selectorTriggerRef = useRef<HTMLButtonElement>(null);
  const adminStepUpRef = useRef<HTMLDivElement>(null);
  const adminStepUpOpenerRef = useRef<HTMLElement | null>(null);
  const selectedDevices = useMemo(() => devices.filter((device) => selectedDeviceIds.has(device.id)), [devices, selectedDeviceIds]);
  const contexts = useMemo<DeviceSessionContext[]>(() => selectedDevices.map((device) => ({ device, sessionState: device.rawStatus === "ONLINE" ? sessionByDeviceId.get(device.id) ?? "UNKNOWN" : "UNKNOWN", sessionLoading: sessionLoading && device.rawStatus === "ONLINE" && !sessionByDeviceId.has(device.id) })), [selectedDevices, sessionByDeviceId, sessionLoading]);
  const availability = useMemo(() => ({ OPEN_URL: resolveActionAvailability("OPEN_URL", contexts), LOCK_INPUT: resolveActionAvailability("LOCK_INPUT", contexts), UNLOCK_INPUT: resolveActionAvailability("UNLOCK_INPUT", contexts), RESTART: resolveActionAvailability("RESTART", contexts), SHUTDOWN: resolveActionAvailability("SHUTDOWN", contexts) }), [contexts]);
  const sessionEligibility = useMemo(() => ({
    PRIMARY: resolveManagedSessionEligibility("PRIMARY", contexts, managedByDeviceId, managedLoading),
    SECONDARY: resolveManagedSessionEligibility("SECONDARY", contexts, managedByDeviceId, managedLoading),
    ADMIN: resolveManagedSessionEligibility("ADMIN", contexts, managedByDeviceId, managedLoading)
  }), [contexts, managedByDeviceId, managedLoading]);

  useEffect(() => () => { controllerRef.current?.abort(); sessionControllerRef.current?.abort(); managedControllerRef.current?.abort(); }, []);
  useEffect(() => {
    if (selectedDeviceIds.size === 0) { sessionControllerRef.current?.abort(); const empty = new Map<string, WindowsSessionState>(); setSessionByDeviceId(empty); onSessionStatesChange?.(empty); setSessionLoading(false); return; }
    const onlineTargets = new Set(devices.filter((device) => selectedDeviceIds.has(device.id) && device.rawStatus === "ONLINE").map((device) => device.id));
    if (onlineTargets.size === 0) { const empty = new Map<string, WindowsSessionState>(); setSessionByDeviceId(empty); onSessionStatesChange?.(empty); setSessionLoading(false); return; }
    sessionControllerRef.current?.abort(); const controller = new AbortController(); sessionControllerRef.current = controller; setSessionLoading(true);
    void fetchWindowsSessionStates(classroomId, onlineTargets, controller.signal).then((response) => {
      if (controller.signal.aborted) return; const states = new Map(response.targets.map((target) => [target.deviceId, target.state])); setSessionByDeviceId(states); onSessionStatesChange?.(states);
    }).catch(() => { if (!controller.signal.aborted) { const unknown = new Map([...onlineTargets].map((id) => [id, "UNKNOWN" as WindowsSessionState])); setSessionByDeviceId(unknown); onSessionStatesChange?.(unknown); } }).finally(() => { if (sessionControllerRef.current === controller) { sessionControllerRef.current = null; setSessionLoading(false); } });
  }, [classroomId, devices, selectedDeviceIds, onSessionStatesChange]);
  useEffect(() => {
    if (overlay !== "session" || selectedDeviceIds.size === 0) return;
    managedControllerRef.current?.abort();
    const controller = new AbortController();
    managedControllerRef.current = controller;
    setManagedLoading(true);
    const targets = devices.filter((device) => selectedDeviceIds.has(device.id) && device.rawStatus === "ONLINE");
    void Promise.allSettled(targets.map(async (device) => ({
      deviceId: device.id,
      status: await fetchManagedAccounts(classroomId, device.id, null, controller.signal)
    }))).then((settled) => {
      if (controller.signal.aborted) return;
      const next = new Map<string, ManagedAccountStatusResponse>();
      settled.forEach((item) => { if (item.status === "fulfilled") next.set(item.value.deviceId, item.value.status); });
      setManagedByDeviceId(next);
    }).finally(() => {
      if (managedControllerRef.current === controller) {
        managedControllerRef.current = null;
        setManagedLoading(false);
      }
    });
    return () => controller.abort();
  }, [classroomId, devices, overlay, selectedDeviceIds]);
  useEffect(() => { if (intent && (intent.classroomId !== classroomId || !sameTargets(intent.targetDeviceIds, selectedDeviceIds))) { setIntent(null); setOverlay(null); } }, [classroomId, intent, selectedDeviceIds]);
  useEffect(() => { if (overlay !== "intent" || !intent) return; const dialog = dialogRef.current; dialog?.showModal(); (intent.type === "OPEN_URL" ? urlInputRef.current : cancelRef.current)?.focus(); return () => { dialog?.close(); if (openerRef.current?.isConnected) openerRef.current.focus(); }; }, [intent, overlay]);
  useEffect(() => { if (overlay !== "selector") return; selectorRef.current?.focus(); return () => selectorTriggerRef.current?.focus(); }, [overlay]);
  useEffect(() => {
    if (overlay !== "session-stepup") return;
    adminStepUpRef.current?.querySelector<HTMLInputElement>('input[type="password"]')?.focus();
    return () => { if (adminStepUpOpenerRef.current?.isConnected) adminStepUpOpenerRef.current.focus(); };
  }, [overlay]);
  useEffect(() => { if (selectedDeviceIds.size === 0) { setOverlay(null); setOpenMenu(null); } }, [selectedDeviceIds.size]);

  if (selectedDeviceIds.size === 0) return null;
  const targetCount = selectedDeviceIds.size;
  const activeCount = contexts.filter((context) => context.device.rawStatus === "ONLINE" && interactiveSession(context.sessionState)).length;
  const offlineCount = selectedDevices.filter((device) => device.rawStatus !== "ONLINE").length;
  const noSessionCount = contexts.filter((context) => context.device.rawStatus === "ONLINE" && context.sessionState === "NO_SESSION").length;
  const unknownCount = contexts.filter((context) => context.device.rawStatus === "ONLINE" && context.sessionState === "UNKNOWN").length;
  const primaryCount = contexts.filter((context) => context.sessionState === "PRIMARY_ACTIVE").length;
  const secondaryCount = contexts.filter((context) => context.sessionState === "SECONDARY_ACTIVE").length;
  const singleContext = contexts.length === 1 ? contexts[0] : null;
  const singleState = singleContext?.sessionState ?? "UNKNOWN";
  const initialSessionLoading = contexts.some((context) => context.sessionLoading);
  const sessionControl = sessionPresentation(singleContext, initialSessionLoading);
  const selectedOperation = selectedDevices.map((device) => deviceOperations.operationFor(device.id)).find(Boolean);
  const mutationsDisabled = submitting || Boolean(selectedOperation);
  const pendingLabel = selectedOperation ? deviceOperationLabel(selectedOperation) : "Operación en curso…";
  const urlError = urlTouched ? validateUrl(url) : null;
  const captureTargets = () => ({ targetDeviceIds: new Set(selectedDeviceIds) });
  const closeTransient = () => { setOpenMenu(null); setEligibility(null); setOverlay(null); };
  const showOverlay = (next: Exclude<Overlay, null>) => { setOpenMenu(null); setEligibility(null); setOverlay(next); };
  const openMenuOnly = (id: string) => { setOverlay(null); setEligibility(null); setOpenMenu((current) => current === id ? null : id); };
  const showEligibility = (value: ActionAvailability) => { setOpenMenu(null); setEligibility(value); setOverlay("eligibility"); };
  const openIntent = (type: Intent["type"], currentAvailability: ActionAvailability, opener: HTMLButtonElement) => { if (submittingRef.current) return; if (!currentAvailability.enabled) return showEligibility(currentAvailability); openerRef.current = opener; setUrl(""); setUrlTouched(false); setIntent({ type, classroomId, availability: currentAvailability, ...captureTargets() }); showOverlay("intent"); };
  const dispatch = async (type: QuickActionType, targetClassroomId: string, targetDeviceIds: ReadonlySet<string>, nextUrl?: string) => {
    if (submittingRef.current || deviceOperations.anyBusy(targetDeviceIds)) return;
    if (!deviceOperations.begin(targetDeviceIds, type)) return;
    submittingRef.current = true; setSubmitting(true); closeTransient(); onStart();
    const toastKey = `${[...targetDeviceIds].sort().join(",")}:mutation`;
    appToast.show({ key: toastKey, severity: "info", summary: "Acción en curso", detail: `${actionName[type]} en ${targetDeviceIds.size} ${targetDeviceIds.size === 1 ? "equipo" : "equipos"}.`, sticky: true });
    const controller = new AbortController(); controllerRef.current = controller;
    try {
      const result = type === "OPEN_URL" ? await openUrl(targetClassroomId, targetDeviceIds, nextUrl!, controller.signal) : type === "LOCK_INPUT" || type === "UNLOCK_INPUT" ? await setInputLocked(targetClassroomId, targetDeviceIds, type === "LOCK_INPUT", controller.signal) : await powerControl(targetClassroomId, targetDeviceIds, type, controller.signal);
      if (!controller.signal.aborted) {
        onResult(result);
        const uncertain = result.targets.some((target) => target.status === "PARTIAL" || target.status === "UNKNOWN");
        if (uncertain) deviceOperations.requireReconciliation(targetDeviceIds); else deviceOperations.clear(targetDeviceIds);
        await onSnapshotRefresh?.();
        await deviceOperations.refresh(targetDeviceIds, controller.signal);
        appToast.show({ key: toastKey, severity: uncertain ? "warn" : result.failedCount > 0 ? "error" : "success", summary: uncertain ? "No pudimos confirmar el resultado" : result.failedCount > 0 ? "No se pudo completar la acción" : "Acción completada", detail: uncertain ? "Galtek verificará el estado del equipo antes de permitir otra acción." : `${actionName[type]} · ${targetDeviceIds.size} ${targetDeviceIds.size === 1 ? "equipo" : "equipos"}.`, sticky: uncertain });
      }
    } catch (error) { if (!controller.signal.aborted) {
      const message = error instanceof ApiError ? error.message : "No pudimos enviar la acción."; onError(message);
      if (error instanceof ApiError && error.code === "DEVICE_OPERATION_IN_PROGRESS") {
        deviceOperations.clear(targetDeviceIds); await deviceOperations.refresh(targetDeviceIds);
        appToast.show({ key: toastKey, severity: "warn", summary: "Hay una operación en curso", detail: "Espera a que termine antes de enviar otra acción." });
      } else {
        deviceOperations.requireReconciliation(targetDeviceIds);
        appToast.show({ key: toastKey, severity: "error", summary: "No se pudo completar la acción", detail: message });
      }
    } }
    finally { if (controllerRef.current === controller) controllerRef.current = null; submittingRef.current = false; setSubmitting(false); }
  };
  const dispatchSession = async (role: ManagedSessionRole, sensitiveAuthorization?: string) => {
    const eligibilityForRole = sessionEligibility[role];
    if (submittingRef.current || !eligibilityForRole.enabled || deviceOperations.anyBusy(selectedDeviceIds)) return;
    const targets = new Set(selectedDeviceIds);
    const roleLabel = role === "ADMIN" ? "Administración" : role === "PRIMARY" ? "Primaria" : "Secundaria";
    const toastKey = `${[...targets].sort().join(",")}:session`;
    if (!deviceOperations.begin(targets, "SWITCH_MANAGED_ACCOUNT", role)) return;
    appToast.show({ key: toastKey, severity: "info", summary: "Cambiando sesión", detail: targets.size === 1 ? `${selectedDevices[0]?.label ?? "El equipo"} está cambiando a ${roleLabel}.` : `${targets.size} equipos están cambiando a ${roleLabel}.`, sticky: true });
    submittingRef.current = true; setSubmitting(true); setSessionError(null); setOverlay(null); onStart();
    const controller = new AbortController(); controllerRef.current = controller;
    try {
      const response = await switchManagedAccount(classroomId, targets, role, sensitiveAuthorization, controller.signal);
      const successCount = response.targets.filter((target) => target.status === "SUCCESS" || target.status === "NO_CHANGE").length;
      const result: BatchOperationResponse = {
        operationId: response.operationId,
        type: "SWITCH_MANAGED_ACCOUNT",
        status: response.status,
        targetCount: response.targetCount,
        successCount,
        failedCount: response.targetCount - successCount,
        targets: response.targets
      };
      onResult(result);
      const refreshed = await fetchWindowsSessionStates(classroomId, targets, controller.signal);
      const states = new Map(refreshed.targets.map((target) => [target.deviceId, target.state]));
      setSessionByDeviceId(states); onSessionStatesChange?.(states);
      await onSnapshotRefresh?.();
      await deviceOperations.refresh(targets, controller.signal);
      const partial = response.targets.some((target) => target.status === "PARTIAL");
      const unknown = response.targets.some((target) => target.status === "UNKNOWN");
      const failed = response.targets.some((target) => target.status === "FAILED");
      appToast.show({
        key: toastKey,
        severity: partial || unknown ? "warn" : failed ? "error" : "success",
        summary: partial ? "No se completó el cambio de sesión" : unknown ? "No pudimos confirmar el resultado" : failed ? `No se pudo iniciar ${roleLabel}` : `${roleLabel} iniciada`,
        detail: partial ? `No fue posible completar el cambio a ${roleLabel}. Estamos confirmando el estado del equipo.` : unknown ? "Galtek verificará el estado del equipo antes de permitir otra acción." : failed ? "Windows no pudo completar el cambio de sesión." : targets.size === 1 ? `${selectedDevices[0]?.label ?? "El equipo"} ya está usando el perfil ${roleLabel}.` : `${targets.size} equipos ya usan ${roleLabel}.`,
        sticky: partial || unknown
      });
    } catch (error) {
      if (!controller.signal.aborted) {
        const message = sensitiveActionError(error);
        setSessionError(message); onError(message);
        if (error instanceof ApiError && error.code === "DEVICE_OPERATION_IN_PROGRESS") {
          deviceOperations.clear(targets); await deviceOperations.refresh(targets);
          appToast.show({ key: toastKey, severity: "warn", summary: `${selectedDevices[0]?.label ?? "El equipo"} tiene una operación en curso`, detail: "Espera a que termine antes de enviar otra acción." });
        } else {
          deviceOperations.requireReconciliation(targets);
          appToast.show({ key: toastKey, severity: "error", summary: `No se pudo iniciar ${roleLabel}`, detail: message });
        }
      }
    } finally {
      setMasterPassword(""); setSessionTargetRole(null);
      if (controllerRef.current === controller) controllerRef.current = null;
      submittingRef.current = false; setSubmitting(false);
    }
  };
  const chooseSessionRole = (role: ManagedSessionRole) => {
    if (!sessionEligibility[role].enabled) return;
    setSessionError(null); setSessionTargetRole(role);
    if (role === "ADMIN") { adminStepUpOpenerRef.current = document.activeElement as HTMLElement | null; setMasterPassword(""); setOverlay("session-stepup"); }
    else void dispatchSession(role);
  };
  const submitAdminStepUp = async () => {
    if (!masterPassword || !sessionTargetRole || submittingRef.current || deviceOperations.anyBusy(selectedDeviceIds)) return;
    submittingRef.current = true; setSubmitting(true); setSessionError(null);
    try {
      const targets = new Set(selectedDeviceIds);
      const authorization = await authorizeAdminSession(classroomId, targets, masterPassword);
      submittingRef.current = false; setSubmitting(false);
      await dispatchSession("ADMIN", authorization.sensitiveAuthorizationToken);
    } catch (error) {
      submittingRef.current = false; setSubmitting(false); setSessionError(sensitiveActionError(error));
    }
  };
  const submitIntent = (event: React.FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!intent || submittingRef.current || intent.classroomId !== classroomId || !sameTargets(intent.targetDeviceIds, selectedDeviceIds) || !intent.availability.enabled) return; if (intent.type === "OPEN_URL") { setUrlTouched(true); if (validateUrl(url)) return; } const currentIntent = intent; const trimmedUrl = url.trim(); setIntent(null); setOverlay(null); void dispatch(currentIntent.type, currentIntent.classroomId, currentIntent.targetDeviceIds, trimmedUrl); };

  return <>
    <section className="command-center" aria-label="Centro de comandos" aria-busy={submitting}>
      <div className="command-context">
        <div className="command-context__identity" key={`${targetCount}-${singleState}-${activeCount}-${noSessionCount}-${offlineCount}-${unknownCount}`}>
          {singleContext ? <><div className="command-context__title-row"><strong>{singleContext.device.label}</strong><span className={`command-context__online command-context__online--${singleContext.device.rawStatus === "ONLINE" ? "online" : "offline"}`}><i aria-hidden="true" />{singleContext.device.rawStatus === "ONLINE" ? "En línea" : "Sin comunicación"}</span></div><span className={`command-context__session command-context__session--${sessionTone(singleContext)}`}>{singleContext.device.rawStatus === "ONLINE" ? (initialSessionLoading ? "Consultando sesión" : sessionStateLabels[singleState]) : "Equipo no disponible"}</span></> : <><strong>{targetCount} equipos seleccionados</strong><div className="command-context__breakdown">{activeCount > 0 ? <span>{activeCount} con sesión</span> : null}{noSessionCount > 0 ? <span>{noSessionCount} sin sesión</span> : null}{unknownCount > 0 ? <span>{unknownCount} con estado no disponible</span> : null}{offlineCount > 0 ? <span>{offlineCount} sin comunicación</span> : null}</div>{primaryCount > 0 || secondaryCount > 0 ? <div className="command-context__session-types">{primaryCount > 0 ? <span>Primaria {primaryCount}</span> : null}{secondaryCount > 0 ? <span>Secundaria {secondaryCount}</span> : null}</div> : null}</>}
        </div>
        <div className="command-context__actions"><button ref={selectorTriggerRef} type="button" onClick={() => showOverlay("selector")}>Cambiar selección</button></div>
      </div>
      <div className="command-dock" role="toolbar" aria-label="Comandos disponibles">
        <CommandSlot icon={selectedOperation ? LoaderCircle : sessionControl.icon} label={selectedOperation ? pendingLabel : sessionControl.label} sublabel={selectedOperation ? "Espera a que termine la operación actual." : sessionControl.sublabel} tone={selectedOperation ? "warning" : sessionControl.tone} open={overlay === "session" || overlay === "inspector"} disabled={mutationsDisabled} onClick={() => { if (singleContext?.device.rawStatus === "ONLINE") showOverlay("inspector"); else if (overlay === "session") closeTransient(); else showOverlay("session"); }} />
        <CommandMenu id="interaction" icon={Keyboard} label="Interacción" open={openMenu === "interaction"} locked={!availability.LOCK_INPUT.enabled || !availability.UNLOCK_INPUT.enabled} disabled={mutationsDisabled} onToggle={() => { if (!availability.LOCK_INPUT.enabled || !availability.UNLOCK_INPUT.enabled) return showEligibility(availability.LOCK_INPUT); openMenuOnly("interaction"); }}><ActionItem action="LOCK_INPUT" availability={availability.LOCK_INPUT} disabled={mutationsDisabled} onChoose={(action) => void dispatch(action, classroomId, captureTargets().targetDeviceIds)} onBlocked={showEligibility} /><ActionItem action="UNLOCK_INPUT" availability={availability.UNLOCK_INPUT} disabled={mutationsDisabled} onChoose={(action) => void dispatch(action, classroomId, captureTargets().targetDeviceIds)} onBlocked={showEligibility} /></CommandMenu>
        <CommandMenu id="content" icon={Link} label="Contenido" open={openMenu === "content"} locked={!availability.OPEN_URL.enabled} disabled={mutationsDisabled} onToggle={() => availability.OPEN_URL.enabled ? openMenuOnly("content") : showEligibility(availability.OPEN_URL)}><ActionItem action="OPEN_URL" availability={availability.OPEN_URL} disabled={mutationsDisabled} onChoose={(action, event) => openIntent(action as Intent["type"], availability.OPEN_URL, event.currentTarget)} onBlocked={showEligibility} /></CommandMenu>
        <CommandMenu id="system" icon={Power} label="Sistema" open={openMenu === "system"} disabled={mutationsDisabled} onToggle={() => openMenuOnly("system")}><ActionItem action="RESTART" availability={availability.RESTART} disabled={mutationsDisabled} onChoose={(action, event) => openIntent(action as Intent["type"], availability.RESTART, event.currentTarget)} onBlocked={showEligibility} /><ActionItem action="SHUTDOWN" availability={availability.SHUTDOWN} disabled={mutationsDisabled} danger separator onChoose={(action, event) => openIntent(action as Intent["type"], availability.SHUTDOWN, event.currentTarget)} onBlocked={showEligibility} /></CommandMenu>
        {mutationsDisabled ? <span className="command-dock__progress" role="status"><LoaderCircle size={15} aria-hidden="true" /> {pendingLabel}</span> : null}
      </div>
      {overlay === "session" ? <SessionPopover context={singleContext} contexts={contexts}
        loading={initialSessionLoading || managedLoading} eligibility={sessionEligibility} disabled={mutationsDisabled}
        onChoose={chooseSessionRole} onClose={closeTransient} /> : null}
      {overlay === "eligibility" && eligibility ? <EligibilityPanel availability={eligibility} contexts={contexts} onClose={closeTransient} onSession={() => showOverlay("session")} onAdjust={() => showOverlay("selector")} /> : null}
      {operationResult && onDismissOperationResult ? <OperationResultPanel classroomId={classroomId} result={operationResult} devices={devices} onDismiss={onDismissOperationResult} drawerOpen={overlay === "result"} onDrawerChange={(open) => open ? showOverlay("result") : setOverlay(null)} /> : null}
    </section>
    {overlay === "intent" && intent ? <dialog ref={dialogRef} className="action-dialog" aria-labelledby="action-dialog-title" aria-describedby="action-dialog-description" onCancel={(event) => { event.preventDefault(); setIntent(null); setOverlay(null); }}><form onSubmit={submitIntent} noValidate><span className={`action-dialog__icon action-dialog__icon--${intent.type.toLowerCase()}`} aria-hidden="true">{intent.type === "OPEN_URL" ? <Link size={21} /> : intent.type === "RESTART" ? <RotateCcw size={21} /> : <Power size={21} />}</span><h2 id="action-dialog-title">{intent.type === "OPEN_URL" ? "Abrir página web" : `${actionName[intent.type]} ${intent.targetDeviceIds.size} ${intent.targetDeviceIds.size === 1 ? "equipo" : "equipos"}`}</h2><p id="action-dialog-description">{intent.type === "OPEN_URL" ? `Destino: ${intent.targetDeviceIds.size} ${intent.targetDeviceIds.size === 1 ? "equipo" : "equipos"}` : `Se solicitará a Windows ${intent.type === "RESTART" ? "reiniciar" : "apagar"} los equipos seleccionados. Las sesiones activas se cerrarán.`}</p>{intent.type === "OPEN_URL" ? <label className={`action-dialog__field${urlError ? " action-dialog__field--error" : ""}`}>Dirección web<input ref={urlInputRef} type="url" inputMode="url" value={url} onChange={(event) => setUrl(event.target.value)} onBlur={() => setUrlTouched(true)} placeholder="https://ejemplo.com/material" aria-invalid={Boolean(urlError)} aria-describedby="url-helper" /><small id="url-helper">{urlError ?? "Usa una dirección http o https completa."}</small></label> : <p className="action-dialog__impact">{intent.targetDeviceIds.size} {intent.targetDeviceIds.size === 1 ? "equipo recibirá" : "equipos recibirán"} la solicitud.</p>}<div className="action-dialog__footer"><button ref={cancelRef} type="button" onClick={() => { setIntent(null); setOverlay(null); }}>Cancelar</button><button type="submit" disabled={mutationsDisabled} className={`action-dialog__confirm${intent.type === "SHUTDOWN" ? " action-dialog__confirm--danger" : ""}`}>{intent.type === "OPEN_URL" ? `Abrir en ${intent.targetDeviceIds.size} ${intent.targetDeviceIds.size === 1 ? "equipo" : "equipos"}` : `${actionName[intent.type]} equipos`}</button></div></form></dialog> : null}
    {overlay === "session-stepup" ? <div className="managed-dialog__backdrop" role="presentation" onKeyDown={(event) => { if (event.key === "Escape" && !submitting) { event.stopPropagation(); setOverlay("session"); setMasterPassword(""); setSessionError(null); } if (event.key === "Tab") trapFocus(event, adminStepUpRef.current); }}><div ref={adminStepUpRef} className="managed-dialog" role="dialog" aria-modal="true" aria-labelledby="batch-admin-title" tabIndex={-1}><div className="managed-dialog__header"><h4 id="batch-admin-title">Iniciar Administración en {selectedDeviceIds.size} {selectedDeviceIds.size === 1 ? "equipo" : "equipos"}</h4><GaltekCloseButton ariaLabel="Cerrar" disabled={submitting} onClick={() => { setOverlay("session"); setMasterPassword(""); setSessionError(null); }} /></div><div className="managed-dialog__body"><p className="managed-dialog__warning">Esta acción abrirá sesiones con privilegios administrativos en la selección exacta.</p><FormPassword label="Contraseña maestra" value={masterPassword} onChange={setMasterPassword} autoFocus error={sessionError} help="Confirma tu contraseña maestra para autorizar el acceso a Administración." />{sessionError ? null : null}<div className="managed-dialog__buttons"><button type="button" disabled={submitting} onClick={() => { setOverlay("session"); setMasterPassword(""); setSessionError(null); }}>Cancelar</button><button type="button" disabled={mutationsDisabled || !masterPassword} onClick={() => void submitAdminStepUp()}>{submitting ? "Autorizando…" : "Iniciar Administración"}</button></div></div></div></div> : null}
    {overlay === "selector" ? <div className="target-drawer-shell" role="presentation" onKeyDown={(event) => { if (event.key === "Escape") { event.stopPropagation(); setOverlay(null); } if (event.key === "Tab") trapFocus(event, selectorRef.current); }}><button className="target-drawer-shell__backdrop" type="button" aria-label="Cerrar selección" onClick={() => setOverlay(null)} /><aside ref={selectorRef} className="target-selector" role="dialog" aria-modal="true" aria-labelledby="target-selector-title" tabIndex={-1}><TargetSelector title={classroomName} groups={groups} devices={devices} selectedDeviceIds={selectedDeviceIds} query={selectorSearch} sessionByDeviceId={sessionByDeviceId} onQueryChange={setSelectorSearch} onToggleDevice={onToggleDevice} onSelectAll={onSelectAll} onClearSelection={onClearSelection} onClose={() => setOverlay(null)} /></aside></div> : null}
    {overlay === "inspector" && singleContext ? <div className="result-drawer-shell" role="presentation"><button className="result-drawer-shell__backdrop" type="button" aria-label="Cerrar inspector" onClick={() => setOverlay(null)} /><aside className="result-drawer device-inspector-drawer" role="dialog" aria-modal="true" aria-label={`Sesión y perfiles de ${singleContext.device.label}`}><DeviceInspector classroomId={classroomId} device={singleContext.device} initialTab="session" onSessionStateChange={(state) => onSessionStatesChange?.(new Map([[singleContext.device.id, state]]))} onSnapshotRefresh={onSnapshotRefresh} onClose={() => setOverlay(null)} /></aside></div> : null}
  </>;
}

function CommandSlot({ icon: Icon, label, sublabel, tone, open, disabled, onClick }: { icon: LucideIcon; label: string; sublabel?: string; tone: string; open: boolean; disabled: boolean; onClick: () => void }) { return <button type="button" className={`command-slot command-slot--${tone}`} aria-expanded={open} disabled={disabled} title={disabled ? "Espera a que termine la operación actual." : undefined} onClick={onClick}><Icon size={17} aria-hidden="true" /><span><strong>{label}</strong>{sublabel ? <small>{sublabel}</small> : null}</span><ChevronDown size={15} aria-hidden="true" /></button>; }

function CommandMenu({ id, icon: Icon, label, open, locked = false, disabled, onToggle, children }: { id: string; icon: LucideIcon; label: string; open: boolean; locked?: boolean; disabled: boolean; onToggle: () => void; children: ReactNode }) {
  const menuRef = useRef<HTMLDivElement>(null);
  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => { if (event.key === "Escape" && open) { event.stopPropagation(); onToggle(); } if ((event.key === "ArrowDown" || event.key === "ArrowUp") && open) { event.preventDefault(); const items = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>("[data-menu-item]") ?? []); if (items.length === 0) return; const current = items.indexOf(document.activeElement as HTMLButtonElement); items[event.key === "ArrowDown" ? (current + 1 + items.length) % items.length : (current - 1 + items.length) % items.length].focus(); } };
  return <div className="command-menu" onKeyDown={handleKeyDown} ref={menuRef}><button type="button" className={`command-menu__trigger${locked ? " command-menu__trigger--locked" : ""}`} aria-expanded={open} aria-controls={`command-menu-${id}`} disabled={disabled} title={disabled ? "Espera a que termine la operación actual." : undefined} onClick={onToggle}><Icon size={16} aria-hidden="true" /><span>{label}{locked ? <small>Requiere sesión</small> : null}</span><ChevronDown size={15} aria-hidden="true" /></button>{open ? <div className="command-menu__panel" id={`command-menu-${id}`} role="menu">{children}</div> : null}</div>;
}

function ActionItem({ action, availability, disabled, danger = false, separator = false, onChoose, onBlocked }: { action: QuickActionType; availability: ActionAvailability; disabled: boolean; danger?: boolean; separator?: boolean; onChoose: (action: QuickActionType, event: React.MouseEvent<HTMLButtonElement>) => void; onBlocked: (availability: ActionAvailability) => void }) { const Icon = actionIcons[action]; const blocked = disabled || !availability.enabled; return <button type="button" role="menuitem" data-menu-item className={`command-menu__item${danger ? " command-menu__item--danger" : ""}${separator ? " command-menu__item--separator" : ""}`} disabled={blocked} aria-disabled={blocked} onClick={(event) => availability.enabled ? onChoose(action, event) : onBlocked(availability)}><Icon size={16} aria-hidden="true" /><span><strong>{actionName[action]}</strong><small>{actionDescriptions[action]}</small></span>{!availability.enabled ? <CircleAlert size={15} aria-hidden="true" /> : null}</button>; }

function SessionPopover({ context, contexts, loading, eligibility, disabled, onChoose, onClose }: {
  context: DeviceSessionContext | null;
  contexts: DeviceSessionContext[];
  loading: boolean;
  eligibility: Record<ManagedSessionRole, ManagedSessionEligibility>;
  disabled: boolean;
  onChoose: (role: ManagedSessionRole) => void;
  onClose: () => void;
}) {
  const active = contexts.filter((item) => interactiveSession(item.sessionState)).length;
  return <aside className="command-popover session-popover" role="dialog" aria-label="Cambiar perfil de Windows"><PopoverHeader icon={UserRound} title={context ? sessionStateLabels[context.sessionState] : "Estado de las sesiones"} onClose={onClose} />{loading ? <p>Consultando perfiles y sesiones…</p> : <><p>{context ? `${context.device.label} · ${sessionStateLabels[context.sessionState]}` : `${active} de ${contexts.length} equipos tienen una sesión interactiva.`}</p><div className="session-popover__actions">{(["PRIMARY", "SECONDARY", "ADMIN"] as ManagedSessionRole[]).map((role) => { const item = eligibility[role]; const label = role === "PRIMARY" ? "Primaria" : role === "SECONDARY" ? "Secundaria" : "Administración"; return <button key={role} type="button" disabled={disabled || !item.enabled} onClick={() => onChoose(role)}><strong>{contexts.length === 1 && contexts[0].sessionState === `${role}_ACTIVE` ? `${label} activa` : `Cambiar a ${label}`}</strong><small>{item.ready} listos{item.blocked ? ` · ${item.blocked} requieren atención` : ""}</small>{item.reasons[0] ? <span>{item.reasons[0]}</span> : null}</button>; })}</div></>}</aside>;
}

function EligibilityPanel({ availability, contexts, onClose, onAdjust, onSession }: { availability: ActionAvailability; contexts: DeviceSessionContext[]; onClose: () => void; onAdjust: () => void; onSession: () => void }) {
  const Icon = actionIcons[availability.action]; const single = contexts.length === 1 ? contexts[0] : null; const noSession = single?.device.rawStatus === "ONLINE" && single.sessionState === "NO_SESSION"; const title = `${availability.action === "OPEN_URL" ? "Contenido" : "Interacción"} no disponible`;
  return <aside className="command-popover eligibility-popover" role="status" aria-live="polite"><PopoverHeader icon={Icon} title={title} onClose={onClose} />{single ? <><div className="eligibility-popover__device"><strong>{single.device.label}</strong><span>{availability.reasons[0]?.reason ?? "No disponible"}</span></div><p>{availability.action === "OPEN_URL" ? "Para abrir URLs o aplicaciones primero debe existir una sesión interactiva de Windows." : "Para controlar teclado y mouse primero debe existir una sesión interactiva de Windows."}</p></> : <><div className="eligibility-popover__counts"><strong>{availability.eligibleCount} disponibles</strong><span>{availability.blockedCount} requieren atención</span></div><div className="eligibility-popover__list">{availability.reasons.slice(0, 8).map((reason) => <div key={reason.deviceId}><strong>{reason.deviceName}</strong><span>{reason.reason}</span></div>)}</div></>}<button type="button" className="eligibility-popover__adjust" onClick={noSession ? onSession : onAdjust}>{noSession ? "Ver estado de sesión" : "Ajustar selección"}</button></aside>;
}

function PopoverHeader({ icon: Icon, title, onClose }: { icon: LucideIcon; title: string; onClose: () => void }) { return <div className="eligibility-popover__header"><span><Icon size={17} aria-hidden="true" />{title}</span><GaltekCloseButton ariaLabel="Cerrar" onClick={onClose} /></div>; }

function TargetSelector({ title, groups, devices, selectedDeviceIds, query, sessionByDeviceId, onQueryChange, onToggleDevice, onSelectAll, onClose }: { title: string; groups: ClassroomGroupData[]; devices: ClassroomDeviceCardData[]; selectedDeviceIds: Set<string>; query: string; sessionByDeviceId: Map<string, WindowsSessionState>; onQueryChange: (value: string) => void; onToggleDevice?: (deviceId: string) => void; onSelectAll?: () => void; onClearSelection?: () => void; onClose: () => void }) {
  const normalized = query.trim().toLocaleLowerCase("es-MX"); const visibleDevices = devices.filter((device) => !normalized || `${device.label} ${device.studentName} ${device.hostname ?? ""}`.toLocaleLowerCase("es-MX").includes(normalized)); const groupsWithDevices = groups.map((group) => ({ group, devices: visibleDevices.filter((device) => device.assignedStudentGroupId === group.id) })).filter(({ devices: items }) => items.length > 0); const unassigned = visibleDevices.filter((device) => !device.assignedStudentGroupId);
  return <div className="target-selector__content"><div className="target-selector__header"><div><p className="target-selector__eyebrow">Seleccionar equipos</p><h2 id="target-selector-title">{title}</h2></div><GaltekCloseButton ariaLabel="Cerrar selector" onClick={onClose} /></div><label className="target-selector__search"><Search size={16} aria-hidden="true" /><input value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Buscar equipo o alumno" /></label><div className="target-selector__toolbar"><button type="button" onClick={onSelectAll}>Seleccionar todos</button><span>{selectedDeviceIds.size} seleccionados</span></div><div className="target-tree" role="tree" aria-label="Selección de equipos">{groupsWithDevices.map(({ group, devices: groupDevices }) => <TreeGroup key={group.id} label={group.label} devices={groupDevices} selectedDeviceIds={selectedDeviceIds} sessionByDeviceId={sessionByDeviceId} onToggleDevice={onToggleDevice} />)}{unassigned.length > 0 ? <TreeGroup label="Sin asignar" devices={unassigned} selectedDeviceIds={selectedDeviceIds} sessionByDeviceId={sessionByDeviceId} onToggleDevice={onToggleDevice} /> : null}{visibleDevices.length === 0 ? <p className="target-tree__empty">No hay equipos que coincidan con la búsqueda.</p> : null}</div></div>;
}

function TreeGroup({ label, devices, selectedDeviceIds, sessionByDeviceId, onToggleDevice }: { label: string; devices: ClassroomDeviceCardData[]; selectedDeviceIds: Set<string>; sessionByDeviceId: Map<string, WindowsSessionState>; onToggleDevice?: (id: string) => void }) {
  const [expanded, setExpanded] = useState(true); const selectedCount = devices.filter((device) => selectedDeviceIds.has(device.id)).length; const allSelected = devices.length > 0 && selectedCount === devices.length; const toggleGroup = () => devices.forEach((device) => { if (selectedDeviceIds.has(device.id) === allSelected) onToggleDevice?.(device.id); });
  return <div className="target-tree__group" role="group"><div className="target-tree__group-label"><button type="button" className="target-tree__chevron" aria-label={`${expanded ? "Contraer" : "Expandir"} ${label}`} aria-expanded={expanded} onClick={() => setExpanded(!expanded)}><ChevronRight size={16} aria-hidden="true" /></button><button type="button" className="target-tree__group-check" role="checkbox" aria-checked={allSelected ? true : selectedCount > 0 ? "mixed" : false} onClick={toggleGroup}><span className={`target-tree__check${selectedCount > 0 ? " target-tree__check--selected" : ""}`}>{allSelected ? <Check size={12} /> : selectedCount > 0 ? "−" : ""}</span><strong>{label}</strong><span>{selectedCount}/{devices.length}</span></button></div>{expanded ? <div className="target-tree__children">{devices.map((device) => <TreeDevice key={device.id} device={device} selected={selectedDeviceIds.has(device.id)} sessionState={sessionByDeviceId.get(device.id) ?? "UNKNOWN"} onToggle={() => onToggleDevice?.(device.id)} />)}</div> : null}</div>;
}

function TreeDevice({ device, selected, sessionState, onToggle }: { device: ClassroomDeviceCardData; selected: boolean; sessionState: WindowsSessionState; onToggle: () => void }) { const offline = device.rawStatus !== "ONLINE"; const tone = offline ? "offline" : sessionState === "NO_SESSION" ? "no-session" : sessionState === "UNKNOWN" ? "unknown" : "active"; return <button type="button" className="target-tree__device" role="treeitem" aria-selected={selected} onClick={onToggle}><span className={`target-tree__check${selected ? " target-tree__check--selected" : ""}`} aria-hidden="true">{selected ? <Check size={12} /> : null}</span><span className={`target-tree__state-icon target-tree__state-icon--${tone}`} aria-hidden="true">{offline ? <WifiOff size={14} /> : <UserRound size={14} />}</span><span className="target-tree__identity"><strong>{device.label}</strong><small>{device.studentName}</small></span><span className={`target-tree__status target-tree__status--${tone}`}>{offline ? "Sin comunicación" : sessionStateLabels[sessionState]}</span></button>; }
function sessionPresentation(context: DeviceSessionContext | null, loading: boolean): { icon: LucideIcon; label: string; sublabel?: string; tone: string } { if (loading) return { icon: LoaderCircle, label: "Consultando sesión", tone: "neutral" }; if (!context) return { icon: UserRound, label: "Sesiones", tone: "neutral" }; if (context.device.rawStatus !== "ONLINE") return { icon: WifiOff, label: "Sin comunicación", tone: "offline" }; if (context.sessionState === "NO_SESSION") return { icon: LogIn, label: "Iniciar sesión", sublabel: "Windows sin sesión", tone: "attention" }; return { icon: UserRound, label: sessionStateLabels[context.sessionState], tone: context.sessionState === "UNKNOWN" ? "warning" : "active" }; }
function sessionTone(context: DeviceSessionContext) { if (context.device.rawStatus !== "ONLINE") return "offline"; if (context.sessionState === "NO_SESSION") return "no-session"; if (context.sessionState === "UNKNOWN") return "unknown"; return "active"; }
function sensitiveActionError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === "CREDENTIAL_VAULT_UNLOCK_FAILED") return "La contraseña maestra no es correcta.";
    if (error.code === "SENSITIVE_AUTHORIZATION_EXPIRED") return "La autorización caducó. Confirma nuevamente tu contraseña maestra.";
    if (error.code === "SENSITIVE_AUTHORIZATION_RATE_LIMITED") return "Demasiados intentos fallidos. Espera un momento y vuelve a intentarlo.";
    if (error.code === "SENSITIVE_AUTHORIZATION_USED") return "La autorización ya fue utilizada. Confirma nuevamente tu contraseña maestra.";
    if (error.code === "CAPABILITY_NOT_SUPPORTED") return "Este equipo necesita actualizar Galtek Classroom.";
    if (error.code === "WINDOWS_ACCOUNT_ADMIN_REQUIRED") return "La cuenta ya no tiene permisos de administrador.";
    if (error.code === "WINDOWS_SESSION_CHANGED") return "La sesión cambió durante la operación.";
    if (error.code === "WINDOWS_SESSION_UNKNOWN") return "No se pudo confirmar el estado de la sesión.";
  }
  return error instanceof Error ? error.message : "No pudimos completar la acción.";
}
function validateUrl(value: string) { const trimmed = value.trim(); if (!trimmed) return "Escribe una dirección web."; try { const parsed = new URL(trimmed); if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return "La dirección debe comenzar con http:// o https://."; return null; } catch { return "Escribe una dirección web válida."; } }
function trapFocus(event: React.KeyboardEvent, container: HTMLElement | null) { if (!container) return; const focusable = Array.from(container.querySelectorAll<HTMLElement>('button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])')); if (focusable.length === 0) return; const first = focusable[0]; const last = focusable[focusable.length - 1]; if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); } else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); } }
