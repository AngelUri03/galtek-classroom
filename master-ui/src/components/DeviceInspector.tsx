import {
  ChevronDown,
  CircleAlert,
  Eye,
  EyeOff,
  Info,
  LoaderCircle,
  Monitor,
  Shield,
  UserRound,
  WifiOff,
  X
} from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { ApiError } from "../api/apiClient";
import { fetchWindowsSessionStates, type WindowsSessionState } from "../api/quickActionsApi";
import {
  bindManagedAccount,
  fetchManagedAccounts,
  fetchVaultStatus,
  fetchWindowsAccounts,
  initializeVault,
  provisionManagedCredential,
  unbindManagedAccount,
  unlockVault,
  type ManagedAccountRole,
  type ManagedAccountStatusResponse,
  type WindowsAccount,
  type WindowsAccountInventoryResponse
} from "../api/windowsAccountsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";
import { sessionStateLabels } from "./actionAvailabilityResolver";
import {
  eligibleAccountsForRole,
  eligibleRolesForAccount,
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
};

export function DeviceInspector({ classroomId, device, initialTab = "summary", onClose, onBack }: Props) {
  const [tab, setTab] = useState<DeviceInspectorTab>(initialTab);
  const [sessionState, setSessionState] = useState<WindowsSessionState>("UNKNOWN");
  const [sessionLoading, setSessionLoading] = useState(device.rawStatus === "ONLINE");
  const [inventory, setInventory] = useState<WindowsAccountInventoryResponse | null>(null);
  const [managed, setManaged] = useState<ManagedAccountStatusResponse | null>(null);
  const [accountsLoading, setAccountsLoading] = useState(false);
  const [accountsError, setAccountsError] = useState<string | null>(null);
  const [loadVersion, setLoadVersion] = useState(0);
  const accountsControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (device.rawStatus !== "ONLINE") {
      setSessionState("UNKNOWN");
      setSessionLoading(false);
      return;
    }
    const controller = new AbortController();
    setSessionLoading(true);
    void fetchWindowsSessionStates(classroomId, new Set([device.id]), controller.signal)
      .then((response) => {
        const target = response.targets.find((item) => item.deviceId === device.id);
        if (!controller.signal.aborted) setSessionState(target?.state ?? "UNKNOWN");
      })
      .catch(() => { if (!controller.signal.aborted) setSessionState("UNKNOWN"); })
      .finally(() => { if (!controller.signal.aborted) setSessionLoading(false); });
    return () => controller.abort();
  }, [classroomId, device.id, device.rawStatus]);

  useEffect(() => {
    if (tab !== "accounts" || device.rawStatus !== "ONLINE") return;
    accountsControllerRef.current?.abort();
    const controller = new AbortController();
    accountsControllerRef.current = controller;
    setAccountsLoading(true);
    setAccountsError(null);
    void Promise.all([
      fetchWindowsAccounts(classroomId, device.id, controller.signal),
      fetchManagedAccounts(classroomId, device.id, controller.signal)
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
  }, [classroomId, device.id, device.rawStatus, loadVersion, tab]);

  const retryAccounts = useCallback(() => setLoadVersion((version) => version + 1), []);
  const sessionLabel = device.rawStatus !== "ONLINE"
    ? "Sin comunicación"
    : sessionLoading ? "Consultando sesión" : sessionStateLabels[sessionState];

  return <div className="device-inspector">
    <div className="device-inspector__toolbar">
      {onBack ? <button type="button" className="device-inspector__back" onClick={onBack}>Volver al resultado</button> : <span />}
      {onClose ? <button type="button" className="device-inspector__close" aria-label="Cerrar inspector" onClick={onClose}><X size={17} /></button> : null}
    </div>
    <div className="device-inspector__title">
      <Monitor size={18} aria-hidden="true" />
      <div><h3>{device.label}</h3><p>{device.studentName} · {device.rawStatus === "ONLINE" ? "En línea" : "Sin comunicación"}</p></div>
    </div>
    <div className="device-inspector__session-banner">
      {device.rawStatus !== "ONLINE" ? <WifiOff size={16} /> : sessionLoading ? <LoaderCircle className="spin" size={16} /> : <UserRound size={16} />}
      <div><span>Sesión actual</span><strong>{sessionLabel}</strong>{sessionState === "NO_SESSION" && device.rawStatus === "ONLINE" ? <small>Este equipo no tiene una sesión iniciada.</small> : null}</div>
    </div>
    <div className="device-inspector__tabs" role="tablist" aria-label="Inspector de equipo">
      <InspectorTab id="summary" current={tab} label="Resumen" onSelect={setTab} />
      <InspectorTab id="session" current={tab} label="Sesión" onSelect={setTab} />
      <InspectorTab id="accounts" current={tab} label="Cuentas" onSelect={setTab} />
      <InspectorTab id="capabilities" current={tab} label="Capacidades" onSelect={setTab} />
    </div>
    <div className="device-inspector__content" key={tab} role="tabpanel">
      {tab === "summary" ? <SummaryPanel device={device} /> : null}
      {tab === "session" ? <SessionPanel state={sessionState} offline={device.rawStatus !== "ONLINE"} /> : null}
      {tab === "accounts" ? <AccountsPanel classroomId={classroomId} deviceId={device.id}
        inventory={inventory} managed={managed} state={sessionState} offline={device.rawStatus !== "ONLINE"}
        loading={accountsLoading} error={accountsError} onRetry={retryAccounts} /> : null}
      {tab === "capabilities" ? <CapabilitiesPanel capabilities={device.capabilities} /> : null}
    </div>
  </div>;
}

function InspectorTab({ id, current, label, onSelect }: { id: DeviceInspectorTab; current: DeviceInspectorTab; label: string; onSelect: (tab: DeviceInspectorTab) => void }) {
  return <button type="button" role="tab" aria-selected={current === id} onClick={() => onSelect(id)}>{label}</button>;
}

function SummaryPanel({ device }: { device: ClassroomDeviceCardData }) {
  return <div className="device-inspector__panel"><Info size={16} /><div><strong>{device.statusLabel}</strong><span>{device.secondaryStatus}</span><span>{device.assignedStudentGroupName ?? "Sin grupo asignado"}</span><span>{device.capabilities.length} capacidades anunciadas</span></div></div>;
}

function SessionPanel({ state, offline }: { state: WindowsSessionState; offline: boolean }) {
  return <section className="identity-section"><div className="identity-section__heading"><span>Detalle de la sesión</span></div><div className="session-identity"><UserRound size={20} /><div><span>{offline ? "No hay comunicación con el Agent." : state === "OTHER_SESSION_ACTIVE" ? "Windows reporta otra sesión; no se asocia por nombre." : state === "UNKNOWN" ? "No fue posible confirmar el estado actual." : state === "NO_SESSION" ? "Este equipo no tiene una sesión iniciada." : "Windows confirmó una cuenta Galtek activa."}</span></div></div></section>;
}

type AccountAction =
  | { kind: "bind"; role?: ManagedAccountRole; account?: WindowsAccount }
  | { kind: "credential"; role: ManagedAccountRole; accountName: string }
  | { kind: "unbind"; role: ManagedAccountRole; accountName: string };

function AccountsPanel({ classroomId, deviceId, inventory, managed, state, offline, loading, error, onRetry }: {
  classroomId: string; deviceId: string; inventory: WindowsAccountInventoryResponse | null;
  managed: ManagedAccountStatusResponse | null; state: WindowsSessionState; offline: boolean;
  loading: boolean; error: string | null; onRetry: () => void;
}) {
  const [detail, setDetail] = useState<WindowsAccount | null>(null);
  const [systemOpen, setSystemOpen] = useState(false);
  const [action, setAction] = useState<AccountAction | null>(null);
  const [vaultToken, setVaultToken] = useState<string | null>(null);
  const view = useMemo(() => inventory && managed ? toWindowsAccountInventoryView(inventory, managed, state) : null, [inventory, managed, state]);
  if (offline) return <InlineState icon={WifiOff} title="Sin comunicación" copy="Conecta el equipo para consultar sus cuentas locales." />;
  if (loading && !view) return <AccountsSkeleton />;
  if (error) return <div className="accounts-inline-error" role="alert"><CircleAlert size={18} /><div><strong>No pudimos cargar las cuentas</strong><span>{error}</span></div><button type="button" onClick={onRetry}>Reintentar</button></div>;
  if (!view || !inventory || !managed) return <AccountsSkeleton />;

  return <div className="accounts-view">
    <section className="identity-section"><div className="identity-section__heading"><span>Cuentas Galtek</span></div>
      <div className="managed-account-grid">{view.managed.map((slot) => <article key={slot.role} className={`managed-account-card${slot.active ? " managed-account-card--active" : ""}`}>
        <span className="managed-account-card__role">{slot.roleLabel}</span>
        <strong>{slot.accountName ?? "Sin perfil asignado"}</strong>
        <span className={slot.statusLabel === "Credencial lista" ? "account-state account-state--ready" : "account-state account-state--attention"}>{slot.statusLabel}</span>
        <small>{slot.credentialLabel}</small>
        {slot.role === "ADMIN" && slot.configured ? <small className="managed-account-card__notice">Inicio remoto no disponible</small> : null}
        <div className="managed-account-card__actions">{slot.configured && slot.accountName ? <>
          <button type="button" onClick={() => setAction({ kind: "credential", role: slot.role, accountName: slot.accountName! })}>Actualizar contraseña guardada</button>
          <button type="button" className="text-danger" onClick={() => setAction({ kind: "unbind", role: slot.role, accountName: slot.accountName! })}>Quitar de Galtek</button>
        </> : <button type="button" onClick={() => setAction({ kind: "bind", role: slot.role })}>Asignar perfil existente</button>}</div>
      </article>)}</div>
    </section>
    {view.administrators.length > 0 ? <AccountSection title="Administración" accounts={view.administrators} icon="admin" managed={managed} onDetail={setDetail} onManage={(account) => setAction({ kind: "bind", account })} /> : null}
    {view.others.length > 0 ? <AccountSection title="Otras cuentas" accounts={view.others} icon="user" managed={managed} onDetail={setDetail} onManage={(account) => setAction({ kind: "bind", account })} /> : null}
    {view.system.length > 0 ? <section className="identity-section identity-section--system"><button className="system-accounts-toggle" type="button" aria-expanded={systemOpen} onClick={() => setSystemOpen(!systemOpen)}><span>Cuentas del sistema · {view.system.length}</span><ChevronDown size={16} /></button>{systemOpen ? <div className="account-list">{view.system.map((account) => <AccountRow key={account.accountName} account={account} managed={managed} onDetail={setDetail} onManage={() => undefined} />)}</div> : null}</section> : null}
    {inventory.accounts.length === 0 ? <p className="accounts-empty">No encontramos cuentas locales disponibles.</p> : null}
    {detail ? <AccountDetail account={detail} onClose={() => setDetail(null)} /> : null}
    {action ? <ManagedAccountDialog action={action} inventory={inventory} managed={managed}
      classroomId={classroomId} deviceId={deviceId} vaultToken={vaultToken} onVaultToken={setVaultToken}
      onClose={() => setAction(null)} onRefresh={onRetry}
      onChanged={() => { setAction(null); setDetail(null); onRetry(); }} /> : null}
  </div>;
}

function AccountSection({ title, accounts, icon, managed, onDetail, onManage }: {
  title: string; accounts: WindowsAccount[]; icon: "admin" | "user"; managed: ManagedAccountStatusResponse;
  onDetail: (account: WindowsAccount) => void; onManage: (account: WindowsAccount) => void;
}) {
  return <section className="identity-section"><div className="identity-section__heading"><span>{title}</span></div><div className="account-list">{accounts.map((account) => <AccountRow key={account.accountName} account={account} icon={icon} managed={managed} onDetail={onDetail} onManage={onManage} />)}</div></section>;
}

function AccountRow({ account, managed, icon = "user", onDetail, onManage }: {
  account: WindowsAccount; managed: ManagedAccountStatusResponse; icon?: "admin" | "user";
  onDetail: (account: WindowsAccount) => void; onManage: (account: WindowsAccount) => void;
}) {
  const Icon = icon === "admin" ? Shield : UserRound;
  const eligible = eligibleRolesForAccount(account, managed.accounts).length > 0;
  return <div className="account-row">
    <button type="button" className="account-row__detail" onClick={() => onDetail(account)}><span className="account-row__icon"><Icon size={17} /></span><span><strong>{account.displayName}</strong><small>{account.administrator ? "Administrador local" : account.builtIn ? "Cuenta del sistema" : "Usuario local"}</small></span><em>{account.enabled ? "Habilitada" : "Deshabilitada"}</em></button>
    {eligible ? <button type="button" className="account-row__manage" onClick={() => onManage(account)}>Administrar con Galtek</button> : null}
  </div>;
}

function AccountDetail({ account, onClose }: { account: WindowsAccount; onClose: () => void }) {
  return <aside className="account-detail" aria-label="Detalle de cuenta"><div className="account-detail__header"><strong>Detalle de cuenta</strong><button type="button" aria-label="Cerrar detalle" onClick={onClose}><X size={15} /></button></div><dl><dt>Nombre</dt><dd>{account.accountName}</dd><dt>Tipo</dt><dd>{account.builtIn ? "Cuenta del sistema" : "Usuario local"}</dd><dt>Estado</dt><dd>{account.enabled ? "Habilitada" : "Deshabilitada"}</dd><dt>Rol Galtek</dt><dd>{account.managedRole === "NONE" ? "Ninguno" : managedRoleLabels[account.managedRole]}</dd><dt>Administrador</dt><dd>{account.administrator ? "Sí" : "No"}</dd></dl></aside>;
}

function ManagedAccountDialog({ action, inventory, managed, classroomId, deviceId, vaultToken, onVaultToken, onClose, onRefresh, onChanged }: {
  action: AccountAction; inventory: WindowsAccountInventoryResponse; managed: ManagedAccountStatusResponse;
  classroomId: string; deviceId: string; vaultToken: string | null; onVaultToken: (token: string | null) => void;
  onClose: () => void; onRefresh: () => void; onChanged: () => void;
}) {
  const roleOptions = action.kind === "bind" && action.account ? eligibleRolesForAccount(action.account, managed.accounts) : [];
  const initialRole = action.kind === "bind" ? action.role ?? roleOptions[0] : action.role;
  const [role, setRole] = useState<ManagedAccountRole | undefined>(initialRole);
  const accountOptions = role ? eligibleAccountsForRole(role, inventory.accounts) : [];
  const initialAccount = action.kind === "bind" ? action.account?.accountName ?? accountOptions[0]?.accountName ?? "" : action.accountName;
  const [accountName, setAccountName] = useState(initialAccount);
  const [password, setPassword] = useState("");
  const [vaultPassword, setVaultPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [vaultInitialized, setVaultInitialized] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ kind: "error" | "partial"; message: string } | null>(null);

  useEffect(() => {
    if (action.kind === "unbind" || vaultToken) return;
    const controller = new AbortController();
    void fetchVaultStatus(controller.signal).then((status) => setVaultInitialized(status.initialized)).catch(() => setVaultInitialized(null));
    return () => controller.abort();
  }, [action.kind, vaultToken]);

  const selectedAccount = inventory.accounts.find((account) => account.accountName === accountName);
  const close = () => { setPassword(""); setVaultPassword(""); onClose(); };
  const ensureVault = async () => {
    if (vaultToken) return vaultToken;
    if (!vaultPassword) throw new Error("Ingresa la contraseña maestra de la Vault.");
    if (vaultInitialized === false) await initializeVault(vaultPassword);
    const unlocked = await unlockVault(vaultPassword);
    onVaultToken(unlocked.vaultSessionToken);
    setVaultPassword("");
    return unlocked.vaultSessionToken;
  };

  const submit = async () => {
    if (!role) return;
    let bindingCompleted = false;
    setBusy(true);
    setResult(null);
    try {
      if (action.kind === "unbind") {
        const response = await unbindManagedAccount(classroomId, deviceId, role);
        if (response.status !== "SUCCESS") throw new Error(response.message || response.errorCode || "No se pudo quitar el perfil.");
        onChanged();
        return;
      }
      if (!accountName || !password) throw new Error("Selecciona una cuenta e ingresa su contraseña de Windows.");
      const token = await ensureVault();
      if (action.kind === "bind") {
        const binding = await bindManagedAccount(classroomId, deviceId, role, accountName);
        if (binding.status !== "SUCCESS") throw new Error(binding.message || binding.errorCode || "No se pudo asignar el perfil.");
        bindingCompleted = true;
      }
      const credential = await provisionManagedCredential(classroomId, deviceId, role, password, token);
      if (credential.provisioningStatus !== "SUCCESS") {
        setResult({
          kind: action.kind === "bind" ? "partial" : "error",
          message: action.kind === "bind"
            ? "Perfil asignado. Falta guardar la contraseña; puedes reintentarlo desde el slot."
            : credential.message || "No se pudo actualizar la contraseña guardada."
        });
        if (bindingCompleted) onRefresh();
        return;
      }
      onChanged();
    } catch (requestError) {
      if (requestError instanceof ApiError && requestError.code === "CREDENTIAL_VAULT_LOCKED") onVaultToken(null);
      setResult(bindingCompleted
        ? { kind: "partial", message: "Perfil asignado. No se pudo guardar la contraseña; puedes reintentarlo desde el slot." }
        : { kind: "error", message: requestError instanceof Error ? requestError.message : "No pudimos completar la acción." });
      if (bindingCompleted) onRefresh();
    } finally {
      setPassword("");
      setVaultPassword("");
      setShowPassword(false);
      setBusy(false);
    }
  };

  const title = action.kind === "unbind" ? "Quitar perfil de Galtek" : action.kind === "credential" ? "Actualizar contraseña guardada" : "Administrar con Galtek";
  return <Modal title={title} onClose={close} busy={busy}>
    {action.kind === "unbind" ? <>
      <p>Galtek dejará de administrar esta cuenta. La cuenta de Windows y sus archivos no se eliminarán.</p>
      <dl className="managed-dialog__summary"><dt>Cuenta</dt><dd>{accountName}</dd><dt>Rol</dt><dd>{managedRoleLabels[role!]}</dd></dl>
      {role === "ADMIN" ? <p className="managed-dialog__warning">Este perfil tiene privilegios administrativos en Windows.</p> : null}
      {result ? <div className="managed-dialog__result managed-dialog__result--error" role="alert">{result.message}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" className="button-danger" disabled={busy} onClick={() => void submit()}>Quitar de Galtek</button></div>
    </> : <>
      {action.kind === "bind" ? <>
        {action.role ? <label>Cuenta de Windows<select value={accountName} onChange={(event) => setAccountName(event.target.value)}>{accountOptions.map((account) => <option key={account.accountName} value={account.accountName}>{account.displayName} ({account.accountName})</option>)}</select></label> : null}
        {action.account ? <label>Rol Galtek<select value={role} onChange={(event) => setRole(event.target.value as ManagedAccountRole)}>{roleOptions.map((option) => <option key={option} value={option}>{managedRoleLabels[option]}</option>)}</select></label> : null}
        <p className="managed-dialog__account">Cuenta seleccionada: <strong>{selectedAccount?.accountName ?? accountName}</strong></p>
      </> : <dl className="managed-dialog__summary"><dt>Cuenta</dt><dd>{accountName}</dd><dt>Rol</dt><dd>{managedRoleLabels[role!]}</dd></dl>}
      {role === "ADMIN" ? <><p className="managed-dialog__warning">Este perfil tiene privilegios administrativos en Windows.</p><small>El inicio y cambio remoto a ADMIN no están disponibles.</small></> : null}
      {!vaultToken ? <label>Contraseña maestra de la Vault<input type="password" autoComplete="current-password" value={vaultPassword} onChange={(event) => setVaultPassword(event.target.value)} /><small>{vaultInitialized === false ? "La Vault se inicializará antes de guardar la credencial." : "La Vault debe estar desbloqueada para continuar."}</small></label> : null}
      <label>Contraseña de Windows<div className="password-field"><input type={showPassword ? "text" : "password"} autoComplete="off" value={password} onChange={(event) => setPassword(event.target.value)} /><button type="button" aria-label={showPassword ? "Ocultar contraseña" : "Mostrar contraseña"} onPointerDown={() => setShowPassword(true)} onPointerUp={() => setShowPassword(false)} onPointerLeave={() => setShowPassword(false)} onKeyDown={(event) => { if (event.key === " " || event.key === "Enter") setShowPassword(true); }} onKeyUp={() => setShowPassword(false)}>{showPassword ? <EyeOff size={16} /> : <Eye size={16} />}</button></div><small>Se guarda protegida; no se comprueba mediante un inicio de sesión.</small></label>
      {result ? <div className={`managed-dialog__result managed-dialog__result--${result.kind}`} role="alert">{result.message}</div> : null}
      <div className="managed-dialog__buttons"><button type="button" onClick={close}>Cancelar</button><button type="button" disabled={busy || !role || !accountName || !password || (!vaultToken && !vaultPassword)} onClick={() => void submit()}>{busy ? "Guardando…" : action.kind === "bind" ? "Asignar y guardar" : "Actualizar contraseña"}</button></div>
    </>}
  </Modal>;
}

function Modal({ title, onClose, busy, children }: { title: string; onClose: () => void; busy: boolean; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const node = ref.current;
    node?.querySelector<HTMLElement>("button, input, select")?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) { event.preventDefault(); closeRef.current(); return; }
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
  }, [busy]);
  return <div className="managed-dialog__backdrop" role="presentation"><div ref={ref} className="managed-dialog" role="dialog" aria-modal="true" aria-labelledby="managed-dialog-title"><div className="managed-dialog__header"><h4 id="managed-dialog-title">{title}</h4><button type="button" aria-label="Cerrar" disabled={busy} onClick={onClose}><X size={16} /></button></div><div className="managed-dialog__body">{children}</div></div></div>;
}

function CapabilitiesPanel({ capabilities }: { capabilities: string[] }) {
  return <section className="identity-section"><div className="identity-section__heading"><span>Capacidades anunciadas</span></div><div className="capability-list">{capabilities.map((capability) => <span key={capability}>{capability}</span>)}</div></section>;
}

function AccountsSkeleton() { return <div className="accounts-skeleton" aria-label="Cargando cuentas"><span /><span /><span /><span /></div>; }
function InlineState({ icon: Icon, title, copy }: { icon: typeof WifiOff; title: string; copy: string }) { return <div className="accounts-inline-state"><Icon size={20} /><div><strong>{title}</strong><span>{copy}</span></div></div>; }
