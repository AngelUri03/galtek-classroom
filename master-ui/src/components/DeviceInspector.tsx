import {
  ChevronDown,
  CircleAlert,
  Eye,
  EyeOff,
  LoaderCircle,
  Monitor,
  Shield,
  UserRound,
  WifiOff
} from "lucide-react";
import { Button } from "primereact/button";
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { ApiError } from "../api/apiClient";
import { authorizeAdminSession, fetchWindowsSessionStates, logoffWindowsSession, switchManagedAccount, type WindowsSessionState } from "../api/quickActionsApi";
import {
  bindManagedAccount,
  authorizeCredentialReveal,
  fetchDeviceActivity,
  fetchManagedAccounts,
  fetchVaultStatus,
  fetchWindowsAccounts,
  initializeVault,
  provisionManagedCredential,
  removeManagedCredential,
  revealManagedCredential,
  unbindManagedAccount,
  unlockVault,
  type ManagedAccountRole,
  type DeviceActivityEvent,
  type ManagedAccountStatusResponse,
  type WindowsAccount,
  type WindowsAccountInventoryResponse
} from "../api/windowsAccountsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";
import { sessionStateLabels } from "./actionAvailabilityResolver";
import {
  resolveSessionActionAvailability,
  resolveSessionActions,
  sessionAvailabilityMessage
} from "./sessionActionResolver";
import { deviceFeatureCompatibility, type DeviceFeature } from "./deviceFeatureCompatibility";
import { FormPassword, GaltekCloseButton } from "./FormControls";
import { useAppToast } from "../app/AppToastProvider";
import { useDeviceOperations } from "../app/DeviceOperationState";
import { deviceOperationLabel } from "../app/deviceOperationModel";
import {
  deviceActivityText,
  isDeterministicHttpRejection,
  managedMutationFeedback,
  revealFailurePhase,
  showInitialSessionLoading,
  type RevealRowPhase
} from "./deviceInspectorModel";
import {
  eligibleAccountsForRole,
  eligibleRolesForAccount,
  credentialsMatch,
  managedRoleLabels,
  toWindowsAccountInventoryView
} from "./windowsAccountViewModel";

export type DeviceInspectorTab = "summary" | "session" | "accounts" | "capabilities";

type Props = {
  classroomId: string;
  device: ClassroomDeviceCardData;
  initialTab?: DeviceInspectorTab;
  onClose?: () => void;
  onBack?: () => void;
  onSessionStateChange?: (state: WindowsSessionState) => void;
  onSnapshotRefresh?: () => Promise<void> | void;
};

export function DeviceInspector({ classroomId, device, initialTab = "summary", onClose, onBack, onSessionStateChange, onSnapshotRefresh }: Props) {
  const [tab, setTab] = useState<DeviceInspectorTab>(initialTab);
  const [sessionSnapshot, setSessionSnapshot] = useState<{ deviceId: string; state: WindowsSessionState } | null>(null);
  const [sessionLoading, setSessionLoading] = useState(device.rawStatus === "ONLINE");
  const [inventory, setInventory] = useState<WindowsAccountInventoryResponse | null>(null);
  const [managed, setManaged] = useState<ManagedAccountStatusResponse | null>(null);
  const [activity, setActivity] = useState<DeviceActivityEvent[]>([]);
  const [vaultToken, setVaultToken] = useState<string | null>(null);
  const [accountsLoading, setAccountsLoading] = useState(false);
  const [accountsError, setAccountsError] = useState<string | null>(null);
  const [loadVersion, setLoadVersion] = useState(0);
  const [activityVersion, setActivityVersion] = useState(0);
  const accountsControllerRef = useRef<AbortController | null>(null);
  const onSessionStateChangeRef = useRef(onSessionStateChange);
  const deviceOperations = useDeviceOperations();
  const activeOperation = deviceOperations.operationFor(device.id);
  onSessionStateChangeRef.current = onSessionStateChange;
  const hasSessionSnapshot = sessionSnapshot?.deviceId === device.id;
  const sessionState = hasSessionSnapshot ? sessionSnapshot.state : "UNKNOWN";
  const initialSessionLoading = showInitialSessionLoading(sessionLoading, hasSessionSnapshot);

  useEffect(() => {
    if (device.rawStatus !== "ONLINE") {
      setSessionSnapshot(null);
      setSessionLoading(false);
      return;
    }
    const controller = new AbortController();
    setSessionLoading(true);
    void fetchWindowsSessionStates(classroomId, new Set([device.id]), controller.signal)
      .then((response) => {
        const target = response.targets.find((item) => item.deviceId === device.id);
        if (!controller.signal.aborted) {
          const nextState = target?.state ?? "UNKNOWN";
          setSessionSnapshot({ deviceId: device.id, state: nextState });
          onSessionStateChangeRef.current?.(nextState);
        }
      })
      .catch(() => { if (!controller.signal.aborted) setSessionSnapshot({ deviceId: device.id, state: "UNKNOWN" }); })
      .finally(() => { if (!controller.signal.aborted) setSessionLoading(false); });
    return () => controller.abort();
  }, [classroomId, device.id, device.rawStatus, loadVersion]);

  useEffect(() => {
    if ((tab !== "accounts" && tab !== "session" && tab !== "summary") || device.rawStatus !== "ONLINE") return;
    accountsControllerRef.current?.abort();
    const controller = new AbortController();
    accountsControllerRef.current = controller;
    setAccountsLoading(true);
    setAccountsError(null);
    void Promise.all([
      fetchWindowsAccounts(classroomId, device.id, controller.signal),
      fetchManagedAccounts(classroomId, device.id, vaultToken, controller.signal)
    ]).then(([nextInventory, nextManaged]) => {
      if (controller.signal.aborted) return;
      setInventory(nextInventory);
      setManaged(nextManaged);
    }).catch((requestError) => {
      if (!controller.signal.aborted) {
        setAccountsError(requestError instanceof ApiError && requestError.code === "CAPABILITY_NOT_SUPPORTED"
          ? "Este equipo necesita actualizarse para administrar sus cuentas."
          : requestError instanceof ApiError ? requestError.message : "No pudimos consultar las cuentas locales.");
      }
    }).finally(() => {
      if (accountsControllerRef.current === controller) {
        accountsControllerRef.current = null;
        setAccountsLoading(false);
      }
    });
    return () => controller.abort();
  }, [classroomId, device.id, device.rawStatus, loadVersion, tab, vaultToken]);

  useEffect(() => {
    const controller = new AbortController();
    void fetchDeviceActivity(classroomId, device.id, controller.signal)
      .then((response) => { if (!controller.signal.aborted) setActivity(response.events); })
      .catch(() => { if (!controller.signal.aborted) setActivity([]); });
    return () => controller.abort();
  }, [activityVersion, classroomId, device.id, tab]);

  const retryAccounts = useCallback(() => setLoadVersion((version) => version + 1), []);
  const refreshActivity = useCallback(() => setActivityVersion((version) => version + 1), []);
  const refreshAll = useCallback(() => { retryAccounts(); refreshActivity(); }, [refreshActivity, retryAccounts]);
  const sessionLabel = activeOperation
    ? deviceOperationLabel(activeOperation)
    : device.rawStatus !== "ONLINE"
    ? "Sin comunicación"
    : initialSessionLoading ? "Consultando sesión" : sessionStateLabels[sessionState];

  return <div className="device-inspector">
    <div className="device-inspector__toolbar">
      {onBack ? <button type="button" className="device-inspector__back" onClick={onBack}>Volver al resultado</button> : <span />}
      {onClose ? <GaltekCloseButton ariaLabel="Cerrar inspector" onClick={onClose} /> : null}
    </div>
    <div className="device-inspector__title">
      <Monitor size={18} aria-hidden="true" />
      <div><h3>{device.label}</h3><p>{device.studentName} · {device.rawStatus === "ONLINE" ? "En línea" : "Sin comunicación"}</p></div>
    </div>
    <div className="device-inspector__session-banner">
      {device.rawStatus !== "ONLINE" ? <WifiOff size={16} /> : activeOperation || initialSessionLoading ? <LoaderCircle className="spin" size={16} /> : <UserRound size={16} />}
      <div><span>{activeOperation ? "Operación en curso" : "Sesión actual"}</span><strong>{sessionLabel}</strong>{sessionLoading && hasSessionSnapshot && !activeOperation ? <small>Actualizando estado…</small> : null}{sessionState === "NO_SESSION" && device.rawStatus === "ONLINE" && !activeOperation ? <small>Este equipo no tiene una sesión iniciada.</small> : null}</div>
    </div>
    <div className="device-inspector__tabs" role="tablist" aria-label="Inspector de equipo">
      <InspectorTab id="summary" current={tab} label="Resumen" onSelect={setTab} />
      <InspectorTab id="session" current={tab} label="Sesión" onSelect={setTab} />
      <InspectorTab id="accounts" current={tab} label="Cuentas" onSelect={setTab} />
      <InspectorTab id="capabilities" current={tab} label="Funciones" onSelect={setTab} />
    </div>
    <div className="device-inspector__content" key={tab} role="tabpanel">
      {tab === "summary" ? <SummaryPanel device={device} state={sessionState} managed={managed}
        activity={activity} onRefresh={refreshAll} /> : null}
      {tab === "session" ? <SessionPanel classroomId={classroomId} deviceId={device.id}
        deviceName={device.label}
        state={sessionState} offline={device.rawStatus !== "ONLINE"} managed={managed}
        capabilities={device.capabilities} onRefresh={retryAccounts} onSnapshotRefresh={onSnapshotRefresh} /> : null}
      {tab === "accounts" ? <AccountsPanel classroomId={classroomId} deviceId={device.id}
        deviceName={device.label}
        inventory={inventory} managed={managed} state={sessionState} offline={device.rawStatus !== "ONLINE"}
        loading={accountsLoading} error={accountsError} onRetry={retryAccounts}
        onActivity={refreshActivity}
        vaultToken={vaultToken} onVaultToken={setVaultToken} onSnapshotRefresh={onSnapshotRefresh} /> : null}
      {tab === "capabilities" ? <CapabilitiesPanel capabilities={device.capabilities} /> : null}
    </div>
  </div>;
}

function InspectorTab({ id, current, label, onSelect }: { id: DeviceInspectorTab; current: DeviceInspectorTab; label: string; onSelect: (tab: DeviceInspectorTab) => void }) {
  return <button type="button" role="tab" aria-selected={current === id} onClick={() => onSelect(id)}>{label}</button>;
}

function SummaryPanel({ device, state, managed, activity, onRefresh }: {
  device: ClassroomDeviceCardData;
  state: WindowsSessionState;
  managed: ManagedAccountStatusResponse | null;
  activity: DeviceActivityEvent[];
  onRefresh: () => void;
}) {
  const activeRole = state === "PRIMARY_ACTIVE" ? "PRIMARY" : state === "SECONDARY_ACTIVE" ? "SECONDARY"
    : state === "ADMIN_ACTIVE" ? "ADMIN" : null;
  const activeAccount = activeRole
    ? managed?.accounts.find((account) => account.accountId === activeRole)?.windowsAccountName
    : null;
  const configuredCredentials = managed?.accounts.filter((account) => account.credentialConfigured).length ?? 0;
  return <div className="device-summary">
    <section><h4>Estado actual</h4><div className="device-summary__status"><i aria-hidden="true" /><div><strong>{device.rawStatus === "ONLINE" ? "En línea" : "Sin comunicación"}</strong><span>Sesión actual: {sessionStateLabels[state]}</span>{activeAccount ? <span>Cuenta: {activeAccount}</span> : null}</div></div></section>
    <section><h4>Configuración</h4><div className="device-summary__profiles">{(["PRIMARY", "SECONDARY", "ADMIN"] as ManagedAccountRole[]).map((role) => { const slot = managed?.accounts.find((account) => account.accountId === role); return <div key={role}><span>{managedRoleLabels[role]}</span><strong>{slot?.configured ? slot.credentialConfigured ? "Lista" : "Falta contraseña" : "Sin cuenta"}</strong></div>; })}</div><p>Credenciales: {configuredCredentials} de 3 configuradas</p></section>
    <section><div className="device-summary__heading"><h4>Actividad de hoy</h4><button type="button" onClick={onRefresh}>Actualizar</button></div>{activity.length === 0 ? <p>No hay actividad registrada hoy.</p> : <ol className="device-activity">{activity.map((event) => <li key={event.eventId}><time>{new Date(event.occurredAtUtc).toLocaleTimeString("es-MX", { hour: "2-digit", minute: "2-digit" })}</time><div><strong>{activityText(event)}</strong><span>por {event.actor}</span></div></li>)}</ol>}</section>
  </div>;
}

function activityText(event: DeviceActivityEvent) {
  return deviceActivityText(event);
}

type SessionOperationFeedback = {
  state: "processing" | "success" | "partial" | "failed" | "unknown";
  title: string;
  detail?: string;
  retryRole?: ManagedAccountRole;
};

function SessionPanel({ classroomId, deviceId, deviceName, state, offline, managed, capabilities, onRefresh, onSnapshotRefresh }: {
  classroomId: string; deviceId: string; deviceName: string; state: WindowsSessionState; offline: boolean;
  managed: ManagedAccountStatusResponse | null; capabilities: string[]; onRefresh: () => void;
  onSnapshotRefresh?: () => Promise<void> | void;
}) {
  const [busy, setBusy] = useState(false);
  const appToast = useAppToast();
  const deviceOperations = useDeviceOperations();
  const activeOperation = deviceOperations.operationFor(deviceId);
  const mutationBusy = busy || Boolean(activeOperation);
  const [feedback, setFeedback] = useState<SessionOperationFeedback | null>(null);
  const [adminStepUp, setAdminStepUp] = useState(false);
  const [masterPassword, setMasterPassword] = useState("");
  const [stepUpError, setStepUpError] = useState<string | null>(null);
  const slot = (role: ManagedAccountRole) => managed?.accounts.find((item) => item.accountId === role);
  const activeRole = state === "PRIMARY_ACTIVE" ? "PRIMARY"
    : state === "SECONDARY_ACTIVE" ? "SECONDARY"
    : state === "ADMIN_ACTIVE" ? "ADMIN" : null;
  const activeAccountName = activeRole ? slot(activeRole)?.windowsAccountName : null;
  const reasonFor = (role: ManagedAccountRole) => sessionAvailabilityMessage(
    resolveSessionActionAvailability({
      online: !offline,
      sessionState: state,
      targetRole: role,
      profile: slot(role),
      capabilities
    })
  );
  const runSwitch = async (role: ManagedAccountRole, sensitiveAuthorization?: string) => {
    if (activeOperation) return;
    const verb = state === "NO_SESSION" ? "Iniciando" : "Cambiando a";
    const toastKey = `${deviceId}:session`;
    if (!deviceOperations.begin([deviceId], "SWITCH_MANAGED_ACCOUNT", role)) return;
    appToast.show({ key: toastKey, severity: "info", summary: "Cambiando sesión", detail: `${deviceName} está cambiando a ${managedRoleLabels[role]}.`, sticky: true });
    setBusy(true);
    setFeedback({ state: "processing", title: `${verb} ${managedRoleLabels[role]}…` });
    try {
      const response = await switchManagedAccount(classroomId, new Set([deviceId]), role, sensitiveAuthorization);
      const target = response.targets[0];
      if (!target) throw new Error("No se pudo confirmar el resultado.");
      if (target.status === "PARTIAL") {
        deviceOperations.requireReconciliation([deviceId]);
        setFeedback({
          state: "partial",
          title: "No se completó el cambio de sesión.",
          detail: `${activeRole ? managedRoleLabels[activeRole] : "La sesión anterior"} se cerró, pero no fue posible iniciar ${managedRoleLabels[role]}. El equipo quedó sin sesión iniciada.`,
          retryRole: role
        });
      } else if (target.status === "UNKNOWN" || target.errorCode === "OPERATION_RESULT_UNKNOWN") {
        deviceOperations.requireReconciliation([deviceId]);
        setFeedback({ state: "unknown", title: "No se pudo confirmar el resultado." });
      } else if (target.status === "FAILED") {
        deviceOperations.clear([deviceId]);
        setFeedback({ state: "failed", title: `No se pudo iniciar ${managedRoleLabels[role]}.`, detail: target.message ?? undefined });
      } else {
        deviceOperations.clear([deviceId]);
        setFeedback({ state: "success", title: target.status === "NO_CHANGE" ? `Ya estaba en ${managedRoleLabels[role]}.` : `${managedRoleLabels[role]} iniciada.` });
      }
      const uncertain = target.status === "PARTIAL" || target.status === "UNKNOWN";
      appToast.show({ key: toastKey, severity: uncertain ? "warn" : target.status === "FAILED" ? "error" : "success", summary: uncertain ? "No pudimos confirmar el resultado" : target.status === "FAILED" ? `No se pudo iniciar ${managedRoleLabels[role]}` : `${managedRoleLabels[role]} iniciada`, detail: uncertain ? "Galtek verificará el estado del equipo antes de permitir otra acción." : target.status === "FAILED" ? "Windows no pudo completar el cambio de sesión." : `${deviceName} ya está usando el perfil ${managedRoleLabels[role]}.`, sticky: uncertain });
    } catch (error) {
      if (error instanceof ApiError && error.code === "DEVICE_OPERATION_IN_PROGRESS") {
        deviceOperations.clear([deviceId]);
        await deviceOperations.refresh([deviceId]);
        appToast.show({ key: toastKey, severity: "warn", summary: `${deviceName} tiene una operación en curso`, detail: "Espera a que termine antes de enviar otra acción." });
      } else {
        deviceOperations.requireReconciliation([deviceId]);
        appToast.show({ key: toastKey, severity: "error", summary: `No se pudo iniciar ${managedRoleLabels[role]}`, detail: error instanceof Error ? error.message : "Windows no pudo completar el cambio de sesión." });
      }
      setFeedback({ state: "failed", title: `No se pudo iniciar ${managedRoleLabels[role]}.`, detail: error instanceof Error ? error.message : undefined });
    } finally {
      setBusy(false);
      onRefresh();
      await onSnapshotRefresh?.();
      await deviceOperations.refresh([deviceId]).catch(() => undefined);
    }
  };
  const runAdmin = async () => {
    setBusy(true); setStepUpError(null);
    try {
      const authorization = await authorizeAdminSession(classroomId, new Set([deviceId]), masterPassword);
      setMasterPassword(""); setAdminStepUp(false);
      setFeedback({ state: "processing", title: "Autorización correcta. Iniciando Administración…" });
      await runSwitch("ADMIN", authorization.sensitiveAuthorizationToken);
    } catch (error) {
      setStepUpError(humanSensitiveError(error)); setBusy(false); onRefresh();
    }
  };
  const runLogoff = async (role: ManagedAccountRole) => {
    if (activeOperation) return;
    const toastKey = `${deviceId}:session`;
    if (!deviceOperations.begin([deviceId], "LOGOFF_WINDOWS_SESSION", role)) return;
    appToast.show({ key: toastKey, severity: "info", summary: "Cerrando sesión", detail: `${deviceName} está cerrando la sesión.`, sticky: true });
    setBusy(true); setFeedback({ state: "processing", title: "Cerrando sesión…" });
    try {
      const response = await logoffWindowsSession(classroomId, new Set([deviceId]), role);
      const target = response.targets[0];
      if (!target) throw new Error("No se pudo confirmar el resultado.");
      if (target.status === "UNKNOWN" || target.errorCode === "OPERATION_RESULT_UNKNOWN") {
        deviceOperations.requireReconciliation([deviceId]);
        setFeedback({ state: "unknown", title: "No se pudo confirmar el resultado." });
      } else if (target.status === "FAILED") throw new Error(target.message ?? "No se pudo cerrar la sesión.");
      else { deviceOperations.clear([deviceId]); setFeedback({ state: "success", title: target.status === "NO_CHANGE" ? "La sesión ya estaba cerrada." : "Sesión cerrada." }); }
      appToast.show({ key: toastKey, severity: target.status === "UNKNOWN" ? "warn" : "success", summary: target.status === "UNKNOWN" ? "No pudimos confirmar el resultado" : "Sesión cerrada", detail: target.status === "UNKNOWN" ? "Galtek verificará el estado del equipo antes de permitir otra acción." : `${deviceName} quedó sin sesión iniciada.`, sticky: target.status === "UNKNOWN" });
    } catch (error) {
      if (error instanceof ApiError && error.code === "DEVICE_OPERATION_IN_PROGRESS") {
        deviceOperations.clear([deviceId]); await deviceOperations.refresh([deviceId]);
        appToast.show({ key: toastKey, severity: "warn", summary: `${deviceName} tiene una operación en curso`, detail: "Espera a que termine antes de enviar otra acción." });
      } else {
        deviceOperations.requireReconciliation([deviceId]);
        appToast.show({ key: toastKey, severity: "error", summary: "No se pudo cerrar la sesión", detail: error instanceof Error ? error.message : "Windows no pudo cerrar la sesión." });
      }
      setFeedback({ state: "failed", title: "No se pudo cerrar la sesión.", detail: error instanceof Error ? error.message : undefined });
    } finally {
      setBusy(false);
      onRefresh();
      await onSnapshotRefresh?.();
      await deviceOperations.refresh([deviceId]).catch(() => undefined);
    }
  };
  const action = (role: ManagedAccountRole, label: string) => {
    const reason = reasonFor(role);
    return <span className="session-action"><button type="button" disabled={mutationBusy || Boolean(reason)} title={activeOperation ? "Espera a que termine la operación actual." : undefined} onClick={() => role === "ADMIN" ? setAdminStepUp(true) : void runSwitch(role)}>{label}</button>{reason ? <small>{reason}</small> : null}</span>;
  };
  const logoffAction = (role: ManagedAccountRole, label: string) => {
    const reason = capabilities.includes("WINDOWS_SESSION_LOGOFF_V1") ? null : "Este Client necesita actualizarse para cerrar la sesión.";
    return <span className="session-action"><button className="button-danger" type="button" disabled={mutationBusy || Boolean(reason)} title={activeOperation ? "Espera a que termine la operación actual." : undefined} onClick={() => void runLogoff(role)}>{label}</button>{reason ? <small>{reason}</small> : null}</span>;
  };
  const retryPartialLogon = (role: ManagedAccountRole) => {
    if (role === "ADMIN") {
      setAdminStepUp(true);
      return;
    }
    void runSwitch(role);
  };
  const copy = offline ? "No hay comunicación con el Agent."
    : state === "OTHER_SESSION_ACTIVE" ? "Galtek no tiene esta sesión asociada a un perfil administrado. Ciérrala en el equipo o administra primero esa cuenta si corresponde."
    : state === "ADMIN_ACTIVE" ? "La cuenta vinculada al perfil Administración está activa. Puedes cambiar directamente a un perfil escolar o cerrar la sesión."
    : state === "UNKNOWN" ? "No fue posible confirmar el estado actual; no se permiten mutaciones."
    : state === "NO_SESSION" ? "Este equipo no tiene una sesión iniciada."
    : "Windows confirmó una cuenta Galtek activa por su identidad local.";
  const availableActions = resolveSessionActions(state, offline);
  return <section className="identity-section session-management"><div className="identity-section__heading"><span>Sesión actual</span></div>
    <div className="session-identity"><UserRound size={20} /><div><strong>{offline ? "Sin comunicación" : sessionStateLabels[state]}</strong>{activeAccountName ? <span>{activeAccountName}</span> : null}<small>{copy}</small></div></div>
    {activeOperation ? <div className="session-management__message session-management__message--processing" role="status"><LoaderCircle className="spin" size={16} /><strong>{activeOperation.state === "RECONCILIATION_REQUIRED" ? "Estamos confirmando el estado del equipo antes de permitir otra acción." : deviceOperationLabel(activeOperation)}</strong></div> : null}
    {!offline ? <div className="session-management__actions">
      {availableActions.includes("LOGIN_PRIMARY") ? action("PRIMARY", "Iniciar Primaria") : null}
      {availableActions.includes("LOGIN_SECONDARY") ? action("SECONDARY", "Iniciar Secundaria") : null}
      {availableActions.includes("LOGIN_ADMIN") ? action("ADMIN", "Iniciar Administración") : null}
      {availableActions.includes("SWITCH_PRIMARY") ? action("PRIMARY", "Cambiar a Primaria") : null}
      {availableActions.includes("SWITCH_SECONDARY") ? action("SECONDARY", "Cambiar a Secundaria") : null}
      {availableActions.includes("SWITCH_ADMIN") ? action("ADMIN", "Cambiar a Administración") : null}
      {availableActions.includes("LOGOFF_PRIMARY") ? logoffAction("PRIMARY", "Cerrar sesión") : null}
      {availableActions.includes("LOGOFF_SECONDARY") ? logoffAction("SECONDARY", "Cerrar sesión") : null}
      {availableActions.includes("LOGOFF_ADMIN") ? logoffAction("ADMIN", "Cerrar sesión") : null}
    </div> : null}
    {feedback ? <div className={`session-management__message session-management__message--${feedback.state}`} role={feedback.state === "failed" ? "alert" : "status"}>
      <strong>{feedback.title}</strong>{feedback.detail ? <span>{feedback.detail}</span> : null}
      {feedback.retryRole ? <div><button type="button" disabled={mutationBusy || state !== "NO_SESSION"} onClick={() => retryPartialLogon(feedback.retryRole!)}>Reintentar iniciar {managedRoleLabels[feedback.retryRole]}</button><button type="button" onClick={() => setFeedback(null)}>Cerrar</button></div> : null}
    </div> : null}
    {adminStepUp ? <Modal title={state === "NO_SESSION" ? "Iniciar sesión de Administración" : "Cambiar a Administración"} onClose={() => { setAdminStepUp(false); setMasterPassword(""); setStepUpError(null); }} busy={busy}>
      <dl className="managed-dialog__summary"><dt>Equipo</dt><dd>{deviceName}</dd><dt>Cuenta</dt><dd>{slot("ADMIN")?.windowsAccountName ?? "Administración"}</dd></dl>
      <p className="managed-dialog__warning">Esta acción abrirá una sesión con privilegios administrativos.</p>
      <PasswordField label="Contraseña maestra" value={masterPassword} onChange={setMasterPassword} autoFocus error={stepUpError} help="Confirma tu contraseña maestra para autorizar el acceso a Administración." />
      <div className="managed-dialog__buttons"><button type="button" onClick={() => { setAdminStepUp(false); setMasterPassword(""); setStepUpError(null); }}>Cancelar</button><button type="button" disabled={busy || !masterPassword} onClick={() => void runAdmin()}>{busy ? "Autorizando…" : "Iniciar Administración"}</button></div>
    </Modal> : null}
  </section>;
}

type AccountAction =
  | { kind: "manage"; role: ManagedAccountRole; accountName: string; credentialConfigured: boolean; active: boolean }
  | { kind: "bind"; role?: ManagedAccountRole; account?: WindowsAccount }
  | { kind: "credential"; role: ManagedAccountRole; accountName: string }
  | { kind: "removeCredential"; role: ManagedAccountRole; accountName: string }
  | { kind: "unbind"; role: ManagedAccountRole; accountName: string };

function AccountsPanel({ classroomId, deviceId, deviceName, inventory, managed, state, offline, loading, error, onRetry, onActivity, vaultToken, onVaultToken, onSnapshotRefresh }: {
  classroomId: string; deviceId: string; deviceName: string; inventory: WindowsAccountInventoryResponse | null;
  managed: ManagedAccountStatusResponse | null; state: WindowsSessionState; offline: boolean;
  loading: boolean; error: string | null; onRetry: () => void; onActivity: () => void;
  vaultToken: string | null; onVaultToken: (token: string | null) => void;
  onSnapshotRefresh?: () => Promise<void> | void;
}) {
  const [detail, setDetail] = useState<WindowsAccount | null>(null);
  const [systemOpen, setSystemOpen] = useState(false);
  const [action, setAction] = useState<AccountAction | null>(null);
  const [revealRole, setRevealRole] = useState<ManagedAccountRole | null | undefined>(undefined);
  const deviceOperations = useDeviceOperations();
  const mutationsDisabled = Boolean(deviceOperations.operationFor(deviceId));
  const view = useMemo(() => inventory && managed ? toWindowsAccountInventoryView(inventory, managed, state) : null, [inventory, managed, state]);
  if (offline) return <InlineState icon={WifiOff} title="Sin comunicación" copy="Conecta el equipo para consultar sus cuentas locales." />;
  if (loading && !view) return <AccountsSkeleton />;
  if (error) return <div className="accounts-inline-error" role="alert"><CircleAlert size={18} /><div><strong>No pudimos cargar las cuentas</strong><span>{error}</span></div><button type="button" onClick={onRetry}>Reintentar</button></div>;
  if (!view || !inventory || !managed) return <AccountsSkeleton />;

  return <div className="accounts-view">
    <section className="identity-section"><div className="identity-section__heading"><span>PERFILES ADMINISTRADOS</span><button type="button" onClick={() => setRevealRole(null)}>Ver contraseñas</button></div>
      <div className="managed-account-grid">{view.managed.map((slot) => <article key={slot.role} className={`managed-account-card${slot.active ? " managed-account-card--active" : ""}`}>
        <span className="managed-account-card__role">{slot.roleLabel}</span>
        <strong>{slot.accountName ?? "Sin perfil asignado"}</strong>
        {slot.configured ? <small>{slot.enabled === null ? "Estado local no disponible" : slot.enabled ? "Cuenta local habilitada" : "Cuenta deshabilitada"}{slot.administrator === true ? " · Administradora" : slot.administrator === false ? " · Estándar" : ""}</small> : null}
        <span className={slot.statusLabel === "Lista para iniciar sesión" ? "account-state account-state--ready" : "account-state account-state--attention"}>{slot.statusLabel}</span>
        <small>{slot.credentialLabel}</small>
        <div className="managed-account-card__actions">{slot.configured && slot.accountName ? <>
          <button type="button" onClick={() => setAction({ kind: "manage", role: slot.role, accountName: slot.accountName!, credentialConfigured: slot.credentialConfigured, active: slot.active })}>Administrar perfil</button>
        </> : <button type="button" disabled={mutationsDisabled} onClick={() => setAction({ kind: "bind", role: slot.role })}>Asignar cuenta</button>}</div>
      </article>)}</div>
    </section>
    {view.administrators.length > 0 ? <AccountSection title="Administración" accounts={view.administrators} icon="admin" managed={managed} mutationsDisabled={mutationsDisabled} onDetail={setDetail} onManage={(account) => setAction({ kind: "bind", account })} /> : null}
    {view.others.length > 0 ? <AccountSection title="Otras cuentas" accounts={view.others} icon="user" managed={managed} mutationsDisabled={mutationsDisabled} onDetail={setDetail} onManage={(account) => setAction({ kind: "bind", account })} /> : null}
    {view.system.length > 0 ? <section className="identity-section identity-section--system"><button className="system-accounts-toggle" type="button" aria-expanded={systemOpen} onClick={() => setSystemOpen(!systemOpen)}><span>Cuentas del sistema · {view.system.length}</span><ChevronDown size={16} /></button>{systemOpen ? <div className="account-list">{view.system.map((account) => <AccountRow key={account.accountName} account={account} managed={managed} mutationsDisabled={mutationsDisabled} onDetail={setDetail} onManage={() => undefined} />)}</div> : null}</section> : null}
    {inventory.accounts.length === 0 ? <p className="accounts-empty">No encontramos cuentas locales disponibles.</p> : null}
    {detail ? <AccountDetail account={detail} onClose={() => setDetail(null)} /> : null}
    {revealRole !== undefined ? <CredentialRevealDialog classroomId={classroomId} deviceId={deviceId} deviceName={deviceName} managed={managed} initialRole={revealRole} onActivity={onActivity} onClose={() => setRevealRole(undefined)} /> : null}
    {action ? <ManagedAccountDialog action={action} inventory={inventory} managed={managed}
      classroomId={classroomId} deviceId={deviceId} deviceName={deviceName} vaultToken={vaultToken} onVaultToken={onVaultToken}
      onClose={() => setAction(null)}
      onAction={setAction}
      onReveal={(role) => { setAction(null); setRevealRole(role); }}
      onRefresh={onRetry}
      onSnapshotRefresh={onSnapshotRefresh}
      onChanged={() => { setAction(null); setDetail(null); onRetry(); }} /> : null}
  </div>;
}

function AccountSection({ title, accounts, icon, managed, mutationsDisabled, onDetail, onManage }: {
  title: string; accounts: WindowsAccount[]; icon: "admin" | "user"; managed: ManagedAccountStatusResponse;
  mutationsDisabled: boolean;
  onDetail: (account: WindowsAccount) => void; onManage: (account: WindowsAccount) => void;
}) {
  return <section className="identity-section"><div className="identity-section__heading"><span>{title}</span></div><div className="account-list">{accounts.map((account) => <AccountRow key={account.accountName} account={account} icon={icon} managed={managed} mutationsDisabled={mutationsDisabled} onDetail={onDetail} onManage={onManage} />)}</div></section>;
}

function AccountRow({ account, managed, icon = "user", mutationsDisabled, onDetail, onManage }: {
  account: WindowsAccount; managed: ManagedAccountStatusResponse; icon?: "admin" | "user";
  mutationsDisabled: boolean;
  onDetail: (account: WindowsAccount) => void; onManage: (account: WindowsAccount) => void;
}) {
  const Icon = icon === "admin" ? Shield : UserRound;
  const eligible = eligibleRolesForAccount(account, managed.accounts).length > 0;
  return <div className="account-row">
    <button type="button" className="account-row__detail" onClick={() => onDetail(account)}><span className="account-row__icon"><Icon size={17} /></span><span><strong>{account.displayName}</strong><small>{account.administrator ? "Administrador local" : account.builtIn ? "Cuenta del sistema" : "Usuario local"}{account.managedRole !== "NONE" ? ` · ${managedRoleLabels[account.managedRole]}` : ""}</small></span><em>{account.enabled ? "Habilitada" : "Deshabilitada"}</em></button>
    {eligible ? <button type="button" className="account-row__manage" disabled={mutationsDisabled} onClick={() => onManage(account)}>Administrar con Galtek</button> : null}
  </div>;
}

function AccountDetail({ account, onClose }: { account: WindowsAccount; onClose: () => void }) {
  return <Modal title="Detalle de cuenta" onClose={onClose} busy={false}><dl className="managed-dialog__summary"><dt>Nombre</dt><dd>{account.accountName}</dd><dt>Tipo</dt><dd>{account.builtIn ? "Cuenta del sistema" : "Usuario local"}</dd><dt>Estado</dt><dd>{account.enabled ? "Habilitada" : "Deshabilitada"}</dd><dt>Administrador</dt><dd>{account.administrator ? "Sí" : "No"}</dd><dt>Perfil Galtek</dt><dd>{account.managedRole === "NONE" ? "Ninguno" : managedRoleLabels[account.managedRole]}</dd></dl><div className="managed-dialog__buttons"><button type="button" onClick={onClose}>Cerrar</button></div></Modal>;
}

function ManagedAccountDialog({ action, inventory, managed, classroomId, deviceId, deviceName, vaultToken, onVaultToken, onClose, onChanged, onRefresh, onSnapshotRefresh, onAction, onReveal }: {
  action: AccountAction; inventory: WindowsAccountInventoryResponse; managed: ManagedAccountStatusResponse;
  classroomId: string; deviceId: string; deviceName: string; vaultToken: string | null; onVaultToken: (token: string | null) => void;
  onClose: () => void; onChanged: () => void; onRefresh: () => void;
  onSnapshotRefresh?: () => Promise<void> | void;
  onAction: (action: AccountAction) => void;
  onReveal: (role: ManagedAccountRole) => void;
}) {
  const roleOptions = action.kind === "bind" && action.account ? eligibleRolesForAccount(action.account, managed.accounts) : [];
  const initialRole = action.kind === "bind" ? action.role ?? roleOptions[0] : action.role;
  const [role, setRole] = useState<ManagedAccountRole | undefined>(initialRole);
  const accountOptions = role ? eligibleAccountsForRole(role, inventory.accounts) : [];
  const initialAccount = action.kind === "bind" ? action.account?.accountName ?? accountOptions[0]?.accountName ?? "" : action.accountName;
  const [accountName, setAccountName] = useState(initialAccount);
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [vaultPassword, setVaultPassword] = useState("");
  const [vaultPasswordConfirm, setVaultPasswordConfirm] = useState("");
  const [vaultInitialized, setVaultInitialized] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ kind: "error" | "partial"; message: string } | null>(null);
  const appToast = useAppToast();
  const deviceOperations = useDeviceOperations();
  const activeOperation = deviceOperations.operationFor(deviceId);
  const mutationBusy = busy || Boolean(activeOperation);

  useEffect(() => {
    if (action.kind === "bind" || action.kind === "manage" || vaultToken) return;
    const controller = new AbortController();
    void fetchVaultStatus(controller.signal).then((status) => setVaultInitialized(status.initialized)).catch(() => setVaultInitialized(null));
    return () => controller.abort();
  }, [action.kind, vaultToken]);

  const selectedAccount = inventory.accounts.find((account) => account.accountName === accountName);
  const clearSecrets = () => { setPassword(""); setConfirmPassword(""); setVaultPassword(""); setVaultPasswordConfirm(""); };
  const close = () => { clearSecrets(); onClose(); };
  const ensureVault = async () => {
    if (vaultToken) return vaultToken;
    if (!vaultPassword) throw new Error("Ingresa la contraseña maestra de la bóveda.");
    if (vaultInitialized === false && !credentialsMatch(vaultPassword, vaultPasswordConfirm)) {
      throw new Error("Las contraseñas maestras no coinciden.");
    }
    if (vaultInitialized === false) await initializeVault(vaultPassword);
    const unlocked = await unlockVault(vaultPassword);
    onVaultToken(unlocked.vaultSessionToken);
    setVaultPassword("");
    return unlocked.vaultSessionToken;
  };

  const submit = async () => {
    if (!role || activeOperation || busy) return;
    const operationType = action.kind === "unbind" ? "UNBIND_MANAGED_ACCOUNT" : action.kind === "bind" ? "BIND_MANAGED_ACCOUNT" : action.kind === "removeCredential" ? "REMOVE_MANAGED_CREDENTIAL" : "CONFIGURE_MANAGED_CREDENTIAL";
    const toastKey = `${deviceId}:profile`;
    if (!deviceOperations.begin([deviceId], operationType, role)) return;
    setBusy(true);
    setResult(null);
    try {
      if (action.kind === "unbind") {
        const token = await ensureVault();
        const response = await unbindManagedAccount(classroomId, deviceId, role, token);
        if (response.status !== "SUCCESS") {
          const feedback = managedMutationFeedback("unbind", role, deviceName, response.errorCode, response.message);
          if (response.status === "PARTIAL" || response.status === "UNKNOWN" || response.errorCode === "OPERATION_RESULT_UNKNOWN") {
            deviceOperations.requireReconciliation([deviceId]);
          } else {
            deviceOperations.clear([deviceId]);
          }
          setResult({ kind: response.status === "PARTIAL" || response.status === "UNKNOWN" ? "partial" : "error", message: feedback.detail });
          appToast.show({ key: toastKey, ...feedback, sticky: response.status === "PARTIAL" || response.status === "UNKNOWN" });
          return;
        }
        deviceOperations.clear([deviceId]);
        appToast.show({ key: toastKey, severity: "success", summary: "Perfil desvinculado", detail: `${managedRoleLabels[role]} ya no está asociado a esta cuenta.` });
        onChanged();
        return;
      }
      if (action.kind === "bind") {
        if (!accountName) throw new Error("Selecciona una cuenta de Windows.");
        const binding = await bindManagedAccount(classroomId, deviceId, role, accountName);
        if (binding.status !== "SUCCESS") {
          const feedback = managedMutationFeedback("bind", role, deviceName, binding.errorCode, binding.message);
          deviceOperations.clear([deviceId]);
          setResult({ kind: "error", message: feedback.detail });
          appToast.show({ key: toastKey, ...feedback });
          return;
        }
        deviceOperations.clear([deviceId]);
        appToast.show({ key: toastKey, severity: "success", summary: "Perfil vinculado", detail: `${managedRoleLabels[role]} quedó asociado a la cuenta seleccionada.` });
        onChanged();
        return;
      }
      const token = await ensureVault();
      if (action.kind === "removeCredential") {
        const response = await removeManagedCredential(classroomId, deviceId, role, token);
        if (response.status !== "SUCCESS") {
          const uncertain = response.status === "PARTIAL" || response.status === "UNKNOWN" || response.errorCode === "OPERATION_RESULT_UNKNOWN";
          const feedback = managedMutationFeedback("removeCredential", role, deviceName, response.errorCode, response.message);
          if (uncertain) deviceOperations.requireReconciliation([deviceId]); else deviceOperations.clear([deviceId]);
          setResult({ kind: uncertain ? "partial" : "error", message: feedback.detail });
          appToast.show({ key: toastKey, ...feedback, sticky: uncertain });
          return;
        }
        deviceOperations.clear([deviceId]);
        appToast.show({ key: toastKey, severity: "success", summary: "Contraseña eliminada", detail: "La copia protegida fue eliminada." });
        onChanged();
        return;
      }
      if (!password) throw new Error("Ingresa la contraseña de Windows.");
      if (password !== confirmPassword) throw new Error("Las contraseñas no coinciden.");
      const credential = await provisionManagedCredential(classroomId, deviceId, role, password, token);
      if (credential.provisioningStatus !== "SUCCESS") {
        setResult({ kind: "partial", message: "La contraseña quedó guardada en la bóveda, pero no se pudo sincronizar con el equipo. Intenta actualizarla nuevamente cuando el equipo esté disponible." });
        deviceOperations.requireReconciliation([deviceId]);
        appToast.show({ key: toastKey, severity: "warn", summary: "No se completó la actualización", detail: "Estamos confirmando el estado del equipo.", sticky: true });
        onRefresh();
        return;
      }
      deviceOperations.clear([deviceId]);
      appToast.show({ key: toastKey, severity: "success", summary: action.kind === "credential" ? "Contraseña guardada" : "Contraseña actualizada", detail: "La contraseña quedó protegida y disponible para iniciar esta cuenta." });
      onChanged();
    } catch (requestError) {
      if (requestError instanceof ApiError && requestError.code === "CREDENTIAL_VAULT_LOCKED") onVaultToken(null);
      const mutationKind = action.kind === "manage" ? "credential" : action.kind;
      const feedback = managedMutationFeedback(
        mutationKind,
        role,
        deviceName,
        requestError instanceof ApiError ? requestError.code : null,
        requestError instanceof ApiError ? humanSensitiveError(requestError) : requestError instanceof Error ? requestError.message : null
      );
      if (requestError instanceof ApiError && requestError.code === "DEVICE_OPERATION_IN_PROGRESS") {
        deviceOperations.clear([deviceId]);
        await deviceOperations.refresh([deviceId]);
      } else if (isDeterministicHttpRejection(requestError) || (requestError instanceof Error && !(requestError instanceof TypeError))) {
        deviceOperations.clear([deviceId]);
      } else {
        deviceOperations.requireReconciliation([deviceId]);
      }
      appToast.show({ key: toastKey, ...feedback });
      setResult({ kind: "error", message: feedback.detail });
    } finally {
      setBusy(false);
      await onSnapshotRefresh?.();
      await deviceOperations.refresh([deviceId]).catch(() => undefined);
    }
  };

  const title = action.kind === "manage" ? `Perfil ${managedRoleLabels[action.role]}`
    : action.kind === "unbind" ? "Desvincular perfil"
    : action.kind === "credential" ? "Guardar contraseña de Windows"
    : action.kind === "removeCredential" ? "Eliminar contraseña guardada"
    : "Configurar perfil";
  return <Modal title={title} onClose={close} busy={mutationBusy}>
    {action.kind === "manage" ? <>
      <dl className="managed-dialog__summary"><dt>Cuenta Windows</dt><dd>{accountName}</dd><dt>Estado</dt><dd>{action.credentialConfigured ? "Lista para iniciar sesión" : "Falta contraseña"}</dd><dt>Contraseña</dt><dd>{action.credentialConfigured ? "Guardada" : "Sin registrar"}</dd></dl>
      <div className="managed-profile-actions"><button type="button" disabled={Boolean(activeOperation)} onClick={() => onAction({ kind: "credential", role: action.role, accountName })}>{action.credentialConfigured ? "Actualizar contraseña" : "Registrar contraseña"}</button>{action.credentialConfigured ? <button type="button" onClick={() => onReveal(action.role)}>Ver contraseña</button> : null}</div>
      <details><summary>Opciones avanzadas</summary><div className="managed-profile-actions">{action.credentialConfigured ? <button type="button" className="text-danger" disabled={Boolean(activeOperation)} onClick={() => onAction({ kind: "removeCredential", role: action.role, accountName })}>Eliminar contraseña guardada</button> : null}<button type="button" className="text-danger" disabled={action.active || Boolean(activeOperation)} onClick={() => onAction({ kind: "unbind", role: action.role, accountName })}>Desvincular perfil</button>{action.active ? <small>Este perfil está activo. Cambia de perfil o cierra la sesión antes de desvincularlo.</small> : null}</div></details>
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cerrar</button></div>
    </> : action.kind === "unbind" ? <>
      <p>Se eliminará la asociación del perfil y la contraseña guardada por Galtek. La cuenta Windows no será eliminada.</p>
      <dl className="managed-dialog__summary"><dt>Cuenta</dt><dd>{accountName}</dd><dt>Perfil</dt><dd>{managedRoleLabels[role!]}</dd></dl>
      {role === "ADMIN" ? <p className="managed-dialog__warning">Este perfil tiene privilegios administrativos en Windows.</p> : null}
      {!vaultToken ? <PasswordField label="Confirma tu contraseña maestra" value={vaultPassword} onChange={setVaultPassword} help="Confirma tu contraseña maestra para continuar." /> : null}
      {result ? <div className="managed-dialog__result managed-dialog__result--error" role="alert">{result.message}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" className="button-danger" disabled={mutationBusy || (!vaultToken && !vaultPassword)} onClick={() => void submit()}>Desvincular perfil</button></div>
    </> : action.kind === "removeCredential" ? <>
      <p>La cuenta y el perfil permanecerán vinculados. Solo se eliminará la copia protegida que Galtek utiliza.</p>
      <dl className="managed-dialog__summary"><dt>Cuenta</dt><dd>{accountName}</dd><dt>Perfil</dt><dd>{managedRoleLabels[role!]}</dd></dl>
      {!vaultToken ? <><PasswordField label="Confirma tu contraseña maestra" value={vaultPassword} onChange={setVaultPassword} newPassword={vaultInitialized === false} help={vaultInitialized === false ? "Crea la contraseña maestra para inicializar la bóveda." : "Confirma tu contraseña maestra para continuar."} />{vaultInitialized === false ? <PasswordField label="Confirmar contraseña maestra" value={vaultPasswordConfirm} onChange={setVaultPasswordConfirm} newPassword help={vaultPasswordConfirm && !credentialsMatch(vaultPassword, vaultPasswordConfirm) ? "Las contraseñas maestras no coinciden." : "Confirma la contraseña de la nueva bóveda."} /> : null}</> : null}
      {result ? <div className="managed-dialog__result managed-dialog__result--error" role="alert">{result.message}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" className="button-danger" disabled={mutationBusy || (!vaultToken && (!vaultPassword || (vaultInitialized === false && !credentialsMatch(vaultPassword, vaultPasswordConfirm))))} onClick={() => void submit()}>Eliminar contraseña</button></div>
    </> : action.kind === "bind" ? <>
        {action.role ? <CustomCombobox label="Cuenta de Windows" value={accountName} onChange={setAccountName} options={accountOptions.map((account) => ({ value: account.accountName, label: account.displayName === account.accountName ? account.accountName : account.displayName, detail: account.administrator ? "Administrador" : "Usuario estándar" }))} /> : null}
        {action.account ? <CustomCombobox label="Perfil Galtek" value={role ?? ""} onChange={(value) => setRole(value as ManagedAccountRole)} options={roleOptions.map((option) => ({ value: option, label: managedRoleLabels[option] }))} /> : null}
        <p className="managed-dialog__account">Cuenta seleccionada: <strong>{selectedAccount?.accountName ?? accountName}</strong></p>
      {role === "ADMIN" ? <p className="managed-dialog__warning">Este perfil tiene privilegios administrativos en Windows. Iniciarlo requerirá una autorización maestra fresca.</p> : null}
      {result ? <div className={`managed-dialog__result managed-dialog__result--${result.kind}`} role="alert">{result.message}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" disabled={mutationBusy || !role || !accountName} onClick={() => void submit()}>{busy ? "Asignando…" : `Asignar como ${role ? managedRoleLabels[role] : "perfil"}`}</button></div>
    </> : <>
      <dl className="managed-dialog__summary"><dt>Cuenta</dt><dd>{accountName}</dd><dt>Perfil</dt><dd>{managedRoleLabels[role!]}</dd></dl>
      <p>Galtek no cambiará la contraseña de Windows. Esta copia se guarda cifrada para poder iniciar este perfil desde el aula.</p>
      <p className="managed-dialog__section-label">SEGURIDAD</p>
      {!vaultToken ? <><PasswordField label="Confirma tu contraseña maestra" value={vaultPassword} onChange={setVaultPassword} newPassword={vaultInitialized === false} help={vaultInitialized === false ? "Crea la contraseña maestra para inicializar la bóveda." : "Confirma tu contraseña maestra para continuar."} />{vaultInitialized === false ? <PasswordField label="Confirmar contraseña maestra" value={vaultPasswordConfirm} onChange={setVaultPasswordConfirm} newPassword help={vaultPasswordConfirm && !credentialsMatch(vaultPassword, vaultPasswordConfirm) ? "Las contraseñas maestras no coinciden." : "Confirma la contraseña de la nueva bóveda."} /> : null}</> : null}
      <p className="managed-dialog__section-label">CONTRASEÑA DE WINDOWS</p>
      <FormPassword label="Contraseña" value={password} onChange={setPassword} newPassword />
      <FormPassword label="Confirmar contraseña" value={confirmPassword} onChange={setConfirmPassword} newPassword
        error={confirmPassword && password !== confirmPassword ? "Las contraseñas no coinciden." : null}
        help="Galtek guarda esta contraseña de forma cifrada para iniciar esta cuenta." />
      {result ? <div className={`managed-dialog__result managed-dialog__result--${result.kind}`} role="alert">{result.message}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" disabled={mutationBusy || !credentialsMatch(password, confirmPassword) || (!vaultToken && (!vaultPassword || (vaultInitialized === false && !credentialsMatch(vaultPassword, vaultPasswordConfirm))))} onClick={() => void submit()}>{busy ? "Guardando…" : "Guardar contraseña"}</button></div>
    </>}
  </Modal>;
}

function CustomCombobox({ label, value, options, onChange }: {
  label: string; value: string; options: { value: string; label: string; detail?: string }[]; onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value));
  const [activeIndex, setActiveIndex] = useState(selectedIndex);
  const selected = options.find((option) => option.value === value);
  const choose = (index: number) => { const option = options[index]; if (option) onChange(option.value); setOpen(false); };
  const keyDown = (event: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault(); setOpen(true);
      setActiveIndex((current) => event.key === "ArrowDown" ? Math.min(options.length - 1, current + 1) : Math.max(0, current - 1));
    } else if (event.key === "Enter") { event.preventDefault(); open ? choose(activeIndex) : setOpen(true); }
    else if (event.key === "Escape") { event.preventDefault(); setOpen(false); }
  };
  return <div className="custom-combobox"><span className="custom-combobox__label">{label}</span><button type="button" role="combobox" aria-expanded={open} aria-haspopup="listbox" onClick={() => setOpen((current) => !current)} onKeyDown={keyDown}><span>{selected?.label ?? "Seleccionar"}{selected?.detail ? <small>{selected.detail}</small> : null}</span><ChevronDown size={16} /></button>{open ? <div role="listbox" className="custom-combobox__list">{options.map((option, index) => <button key={option.value} type="button" role="option" aria-selected={option.value === value} className={index === activeIndex ? "is-active" : ""} onMouseEnter={() => setActiveIndex(index)} onClick={() => choose(index)}><strong>{option.label}</strong>{option.detail ? <small>{option.detail}</small> : null}</button>)}</div> : null}</div>;
}

function CredentialRevealDialog({ classroomId, deviceId, deviceName, managed, initialRole, onActivity, onClose }: {
  classroomId: string; deviceId: string; deviceName: string; managed: ManagedAccountStatusResponse;
  initialRole: ManagedAccountRole | null; onActivity: () => void; onClose: () => void;
}) {
  const [masterPassword, setMasterPassword] = useState("");
  const [authorization, setAuthorization] = useState<{ token: string; expiresAt: number } | null>(null);
  const [secrets, setSecrets] = useState<Partial<Record<ManagedAccountRole, string>>>({});
  const [visible, setVisible] = useState<Set<ManagedAccountRole>>(() => new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rowPhases, setRowPhases] = useState<Partial<Record<ManagedAccountRole, RevealRowPhase>>>(() => initialRevealPhases(managed));
  const maskTimers = useRef<Partial<Record<ManagedAccountRole, number>>>({});
  const appToast = useAppToast();
  const close = () => {
    Object.values(maskTimers.current).forEach((timer) => timer && window.clearTimeout(timer));
    maskTimers.current = {}; setSecrets({}); setVisible(new Set()); setRowPhases(initialRevealPhases(managed)); setAuthorization(null); setMasterPassword(""); onClose();
  };
  useEffect(() => {
    if (!authorization) return;
    const remaining = Math.max(0, authorization.expiresAt - Date.now());
    const resetAuthorization = () => { setSecrets({}); setVisible(new Set()); setRowPhases(initialRevealPhases(managed)); setAuthorization(null); };
    const timer = window.setTimeout(resetAuthorization, remaining);
    const securityReset = () => { if (document.visibilityState !== "visible") resetAuthorization(); };
    document.addEventListener("visibilitychange", securityReset);
    return () => { window.clearTimeout(timer); document.removeEventListener("visibilitychange", securityReset); };
  }, [authorization]);
  const authorize = async () => {
    setBusy(true); setError(null);
    try {
      const response = await authorizeCredentialReveal(classroomId, deviceId, masterPassword);
      setAuthorization({ token: response.sensitiveAuthorizationToken, expiresAt: Date.parse(response.expiresAtUtc) });
      setMasterPassword("");
    } catch (requestError) { setError(humanSensitiveError(requestError)); }
    finally { setBusy(false); }
  };
  const reveal = async (role: ManagedAccountRole) => {
    if (!authorization || busy) return;
    if (!secrets[role]) {
      setBusy(true); setRowPhases((current) => ({ ...current, [role]: "loading" }));
      let password: string;
      try {
        password = await revealManagedCredential(classroomId, deviceId, role, authorization.token);
      } catch (requestError) {
        const phase = revealFailurePhase(requestError);
        const message = humanSensitiveError(requestError);
        setRowPhases((current) => ({ ...current, [role]: phase }));
        appToast.show({
          key: `${deviceId}:reveal`,
          severity: phase === "missing" ? "warn" : "error",
          summary: phase === "missing" ? `No hay contraseña guardada para ${managedRoleLabels[role]}` : `No se pudo consultar ${managedRoleLabels[role]}`,
          detail: message
        });
        if (requestError instanceof ApiError && (requestError.code === "SENSITIVE_AUTHORIZATION_EXPIRED" || requestError.code === "SENSITIVE_AUTHORIZATION_INVALID")) {
          setAuthorization(null);
        }
        setBusy(false);
        return;
      }
      setSecrets((current) => ({ ...current, [role]: password }));
      setRowPhases((current) => ({ ...current, [role]: "available" }));
      setBusy(false);
      appToast.show({ key: `${deviceId}:reveal`, severity: "success", summary: `${managedRoleLabels[role]} disponible`, detail: "La contraseña se ocultará automáticamente." });
      onActivity();
    }
    setVisible((current) => new Set(current).add(role));
    if (maskTimers.current[role]) window.clearTimeout(maskTimers.current[role]);
    maskTimers.current[role] = window.setTimeout(() => {
      setVisible((current) => { const next = new Set(current); next.delete(role); return next; });
    }, 30_000);
  };
  const hide = (role: ManagedAccountRole) => setVisible((current) => { const next = new Set(current); next.delete(role); return next; });
  return <Modal title={`Contraseñas administradas — ${deviceName}`} onClose={close} busy={busy}>
    {!authorization ? <>
      <p>Vuelve a introducir tu contraseña maestra para consultar únicamente las credenciales de este equipo.</p>
      <PasswordField label="Contraseña maestra" value={masterPassword} onChange={setMasterPassword} autoFocus help="Confirma tu contraseña maestra para consultar las contraseñas guardadas." />
      {error ? <div className="managed-dialog__result managed-dialog__result--error" role="alert">{error}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" disabled={busy || !masterPassword} onClick={() => void authorize()}>{busy ? "Verificando…" : "Continuar"}</button></div>
    </> : <>
      <div className="credential-reveal-list">{managed.accounts.map((account) => {
        const role = account.accountId; const password = secrets[role]; const shown = visible.has(role);
        const phase = rowPhases[role] ?? "idle";
        return <div className={`credential-reveal-row${initialRole === role ? " credential-reveal-row--focused" : ""}`} key={role}><div><strong>{managedRoleLabels[role]}</strong><span>{account.windowsAccountName ?? "Sin cuenta vinculada"}</span></div>
          {!account.configured ? <small>Sin cuenta vinculada</small>
            : phase === "missing" ? <div className="credential-reveal-missing"><small>No hay contraseña guardada.</small></div>
            : phase === "failed" ? <div className="credential-reveal-failed"><small className="credential-reveal-row__error">No se pudo consultar</small><Button type="button" size="small" outlined label="Reintentar" className="credential-reveal-retry" disabled={busy} onClick={() => void reveal(role)} /></div>
            : phase === "loading" ? <span className="credential-reveal-loading" role="status"><LoaderCircle className="spin" size={16} /> Consultando…</span>
            : <div className="credential-reveal-secret"><code>{shown && password ? password : "••••••••••"}</code><CredentialRevealButton visible={shown && Boolean(password)} role={role} disabled={busy} onClick={() => shown ? hide(role) : void reveal(role)} /></div>}
        </div>;
      })}</div>
      {error ? <div className="managed-dialog__result managed-dialog__result--error" role="alert">{error}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cerrar</button></div>
    </>}
  </Modal>;
}

function CredentialRevealButton({ visible, role, disabled, onClick }: {
  visible: boolean;
  role: ManagedAccountRole;
  disabled: boolean;
  onClick: () => void;
}) {
  const label = visible ? "Ocultar" : "Mostrar";
  return <Button type="button" size="small" outlined label={label}
    icon={visible ? <EyeOff size={15} aria-hidden="true" /> : <Eye size={15} aria-hidden="true" />}
    className="credential-reveal-action" disabled={disabled}
    aria-label={`${label} ${managedRoleLabels[role]}`} onClick={onClick} />;
}

function initialRevealPhases(managed: ManagedAccountStatusResponse) {
  return Object.fromEntries(managed.accounts.map((account) => [
    account.accountId,
    account.configured && account.vaultCredentialConfigured === false ? "missing" : "idle"
  ])) as Partial<Record<ManagedAccountRole, RevealRowPhase>>;
}

function humanSensitiveError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === "CREDENTIAL_VAULT_UNLOCK_FAILED") return "La contraseña maestra no es correcta.";
    if (error.code === "SENSITIVE_AUTHORIZATION_EXPIRED") return "La autorización caducó. Confirma nuevamente tu contraseña maestra.";
    if (error.code === "SENSITIVE_AUTHORIZATION_RATE_LIMITED") return "Demasiados intentos fallidos. Espera un momento y vuelve a intentarlo.";
    if (error.code === "CREDENTIAL_NOT_FOUND") return "La contraseña está disponible en el equipo para iniciar sesión, pero no existe una copia administrable en la bóveda. Actualízala para poder visualizarla.";
    if (error.code === "WINDOWS_ACCOUNT_ADMIN_REQUIRED") return "La cuenta ya no tiene permisos de administrador.";
    if (error.code === "WINDOWS_SESSION_CHANGED") return "La sesión cambió durante la operación.";
    if (error.code === "WINDOWS_SESSION_UNKNOWN") return "No se pudo confirmar el estado de la sesión.";
  }
  return error instanceof Error ? error.message : "No pudimos completar la acción.";
}

function PasswordField({ label, value, onChange, help, error, autoFocus = false, newPassword = false }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  help?: string;
  error?: string | null;
  autoFocus?: boolean;
  newPassword?: boolean;
}) {
  return <FormPassword label={label} value={value} onChange={onChange} help={help} error={error} autoFocus={autoFocus} newPassword={newPassword} />;
}

function Modal({ title, onClose, busy, children }: { title: string; onClose: () => void; busy: boolean; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  const busyRef = useRef(busy);
  closeRef.current = onClose;
  busyRef.current = busy;
  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const node = ref.current;
    node?.querySelector<HTMLElement>("button, input, select")?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busyRef.current) { event.preventDefault(); closeRef.current(); return; }
      if (event.key !== "Tab" || !node) return;
      const focusable = [...node.querySelectorAll<HTMLElement>("button:not([disabled]), input:not([disabled]), select:not([disabled])")];
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => { document.removeEventListener("keydown", onKeyDown); previous?.focus(); };
  }, []);
  return <div className="managed-dialog__backdrop" role="presentation"><div ref={ref} className="managed-dialog" role="dialog" aria-modal="true" aria-labelledby="managed-dialog-title"><div className="managed-dialog__header"><h4 id="managed-dialog-title">{title}</h4><GaltekCloseButton ariaLabel="Cerrar" disabled={busy} onClick={onClose} /></div><div className="managed-dialog__body">{children}</div></div></div>;
}

function CapabilitiesPanel({ capabilities }: { capabilities: string[] }) {
  const sections: { title: string; items: [DeviceFeature, string][] }[] = [
    { title: "Control del equipo", items: [["keyboardMouse", "Bloquear y desbloquear teclado/mouse"], ["power", "Reiniciar y apagar"], ["openLinks", "Abrir enlaces"]] },
    { title: "Sesiones", items: [["sessionState", "Detectar sesión"], ["profileLogon", "Iniciar perfiles"], ["profileSwitch", "Cambiar perfiles"], ["sessionLogoff", "Cerrar sesión"], ["adminSession", "Sesión remota de Administración"]] },
    { title: "Perfiles", items: [["accountInventory", "Detectar cuentas"], ["profileBinding", "Vincular perfiles"], ["credentialProvisioning", "Guardar contraseñas"], ["credentialRemoval", "Eliminar contraseñas"], ["credentialRevealMasterOnly", "Consultar contraseñas guardadas"]] },
    { title: "Navegación", items: [["navigationRules", "Reglas de navegación"], ["downloadControl", "Control de descargas"]] }
  ];
  return <div className="functions-view">{sections.map((section) => <section className="identity-section" key={section.title}><div className="identity-section__heading"><span>{section.title}</span></div><div className="function-list">{section.items.map(([feature, label]) => {
    const state = deviceFeatureCompatibility(capabilities, feature);
    return <div key={feature} className={`function-list__item function-list__item--${state.toLowerCase()}`}><span>{state === "AVAILABLE" ? "✓" : "—"}</span><strong>{label}</strong>{state === "REQUIRES_CLIENT_UPDATE" ? <small>Requiere actualizar este equipo.</small> : null}</div>;
  })}</div></section>)}</div>;
}

function AccountsSkeleton() { return <div className="accounts-skeleton" aria-label="Cargando cuentas"><span /><span /><span /><span /></div>; }
function InlineState({ icon: Icon, title, copy }: { icon: typeof WifiOff; title: string; copy: string }) { return <div className="accounts-inline-state"><Icon size={20} /><div><strong>{title}</strong><span>{copy}</span></div></div>; }
