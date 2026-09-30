import type {
  ManagedAccountRole,
  ManagedAccountSlot,
  ManagedAccountStatusResponse,
  WindowsAccount,
  WindowsAccountInventoryResponse
} from "../api/windowsAccountsApi";
import type { WindowsSessionState } from "../api/quickActionsApi";

export type ManagedAccountCardView = {
  role: ManagedAccountRole;
  roleLabel: string;
  accountName: string | null;
  configured: boolean;
  existsInWindows: boolean;
  statusLabel: string;
  credentialLabel: string;
  active: boolean;
  remoteLoginSupported: boolean;
  credentialConfigured: boolean;
  enabled: boolean | null;
  administrator: boolean | null;
};

export type WindowsAccountInventoryView = {
  managed: ManagedAccountCardView[];
  administrators: WindowsAccount[];
  others: WindowsAccount[];
  system: WindowsAccount[];
};

export const managedRoleOrder: ManagedAccountRole[] = ["PRIMARY", "SECONDARY", "ADMIN"];

export const managedRoleLabels: Record<ManagedAccountRole, string> = {
  PRIMARY: "Primaria",
  SECONDARY: "Secundaria",
  ADMIN: "Administración"
};

export function eligibleRolesForAccount(
  account: WindowsAccount,
  slots: ManagedAccountSlot[]
): ManagedAccountRole[] {
  if (!account.enabled || account.builtIn || account.managedRole !== "NONE") return [];
  const available = new Set(slots.filter((slot) => !slot.configured).map((slot) => slot.accountId));
  return managedRoleOrder.filter((role) => available.has(role)
    && (role === "ADMIN" ? account.administrator : !account.administrator));
}

export function eligibleAccountsForRole(
  role: ManagedAccountRole,
  accounts: WindowsAccount[]
): WindowsAccount[] {
  return accounts.filter((account) => account.enabled
    && !account.builtIn
    && account.managedRole === "NONE"
    && (role === "ADMIN" ? account.administrator : !account.administrator));
}

export function toWindowsAccountInventoryView(
  inventory: WindowsAccountInventoryResponse,
  managedStatus: ManagedAccountStatusResponse,
  sessionState: WindowsSessionState
): WindowsAccountInventoryView {
  const managedByRole = new Map(managedStatus.accounts.map((account) => [account.accountId, account]));
  const accountByRole = new Map(
    inventory.accounts
      .filter((account) => account.managedRole !== "NONE")
      .map((account) => [account.managedRole as ManagedAccountRole, account])
  );

  const managed = managedRoleOrder.map((role) => managedCard(
    role,
    managedByRole.get(role),
    accountByRole.get(role),
    sessionState
  ));
  const unmanaged = inventory.accounts.filter((account) => account.managedRole === "NONE");

  return {
    managed,
    administrators: unmanaged.filter((account) => account.administrator && !account.builtIn),
    others: unmanaged.filter((account) => !account.administrator && !account.builtIn),
    system: inventory.accounts.filter((account) => account.builtIn)
  };
}

function managedCard(
  role: ManagedAccountRole,
  slot: ManagedAccountSlot | undefined,
  windowsAccount: WindowsAccount | undefined,
  sessionState: WindowsSessionState
): ManagedAccountCardView {
  const configured = slot?.configured ?? false;
  const existsInWindows = Boolean(windowsAccount);
  const statusLabel = managedStatusLabel(slot, existsInWindows);

  return {
    role,
    roleLabel: managedRoleLabels[role],
    accountName: slot?.windowsAccountName ?? windowsAccount?.accountName ?? null,
    configured,
    existsInWindows,
    statusLabel,
    credentialLabel: credentialLabel(slot),
    active: sessionState === `${role}_ACTIVE`,
    remoteLoginSupported: true,
    credentialConfigured: slot?.credentialConfigured ?? false,
    enabled: windowsAccount?.enabled ?? null,
    administrator: windowsAccount?.administrator ?? null
  };
}

export function credentialsMatch(secret: string, confirmation: string) {
  return secret.length > 0 && secret === confirmation;
}

function managedStatusLabel(slot: ManagedAccountSlot | undefined, existsInWindows: boolean) {
  if (!slot || !slot.configured || slot.credentialStatus === "NOT_CONFIGURED") return "Sin perfil asignado";
  if (slot.credentialStatus === "ACCOUNT_NOT_FOUND" || !existsInWindows) {
    return "Cuenta no encontrada en Windows";
  }
  if (slot.credentialStatus === "CREDENTIAL_NOT_CONFIGURED") return "Falta contraseña";
  return "Lista para iniciar sesión";
}

function credentialLabel(slot: ManagedAccountSlot | undefined) {
  if (!slot || !slot.configured || slot.credentialStatus === "NOT_CONFIGURED") return "Sin administrar";
  if (slot.credentialStatus === "ACCOUNT_NOT_FOUND") return "Requiere atención";
  if (slot.combinedCredentialState === "READY") return "Guardada en bóveda y equipo";
  if (slot.combinedCredentialState === "PENDING_CLIENT_SYNC") return "Pendiente de sincronizar con el equipo";
  if (slot.combinedCredentialState === "CLIENT_ONLY_NOT_REVEALABLE") return "Disponible en equipo, no revelable";
  if (slot.combinedCredentialState === "NO_CREDENTIAL") return "Sin contraseña";
  return slot.credentialConfigured ? "Contraseña guardada de forma segura" : "Guardar contraseña para completar";
}
