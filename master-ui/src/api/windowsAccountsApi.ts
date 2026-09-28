import { deleteJson, getJson, putJson, sendSecret } from "./apiClient";

export type ManagedAccountRole = "PRIMARY" | "SECONDARY" | "ADMIN";
export type WindowsAccountManagedRole = ManagedAccountRole | "NONE";
export type ManagedAccountCredentialStatus =
  | "READY"
  | "CREDENTIAL_NOT_CONFIGURED"
  | "ACCOUNT_NOT_FOUND"
  | "NOT_CONFIGURED";

export type WindowsAccount = {
  accountName: string;
  displayName: string;
  enabled: boolean;
  administrator: boolean;
  builtIn: boolean;
  managedRole: WindowsAccountManagedRole;
};

export type WindowsAccountInventoryResponse = {
  deviceId: string;
  accounts: WindowsAccount[];
};

export type ManagedAccountSlot = {
  accountId: ManagedAccountRole;
  configured: boolean;
  credentialConfigured: boolean;
  credentialStatus: ManagedAccountCredentialStatus;
  windowsAccountName: string | null;
};

export type ManagedAccountStatusResponse = {
  accounts: ManagedAccountSlot[];
};

export type ManagedAccountMutationResponse = {
  accountId: ManagedAccountRole;
  status: string;
  operationId: string;
  errorCode: string | null;
  message: string;
  account: ManagedAccountSlot | null;
};

export type ManagedAccountCredentialResponse = {
  accountId: ManagedAccountRole;
  provisioningStatus: string;
  operationId: string;
  errorCode: string | null;
  message: string;
  account: ManagedAccountSlot;
};

export type VaultStatus = { initialized: boolean; locked: boolean };
export type VaultUnlockResponse = VaultStatus & { vaultSessionToken: string; expiresAtUtc: string };

function devicePath(classroomId: string, deviceId: string, suffix: string) {
  return `/api/classrooms/${encodeURIComponent(classroomId)}/devices/${encodeURIComponent(deviceId)}/${suffix}`;
}

export function fetchWindowsAccounts(classroomId: string, deviceId: string, signal?: AbortSignal) {
  return getJson<WindowsAccountInventoryResponse>(
    devicePath(classroomId, deviceId, "windows-accounts"), signal);
}

export function fetchManagedAccounts(classroomId: string, deviceId: string, signal?: AbortSignal) {
  return getJson<ManagedAccountStatusResponse>(
    devicePath(classroomId, deviceId, "managed-accounts"), signal);
}

export function bindManagedAccount(
  classroomId: string,
  deviceId: string,
  role: ManagedAccountRole,
  windowsAccountName: string,
  signal?: AbortSignal
) {
  return putJson<ManagedAccountMutationResponse>(
    devicePath(classroomId, deviceId, `managed-accounts/${role}`),
    { windowsAccountName },
    signal);
}

export function unbindManagedAccount(
  classroomId: string,
  deviceId: string,
  role: ManagedAccountRole,
  signal?: AbortSignal
) {
  return deleteJson<ManagedAccountMutationResponse>(
    devicePath(classroomId, deviceId, `managed-accounts/${role}`), signal);
}

export function provisionManagedCredential(
  classroomId: string,
  deviceId: string,
  role: ManagedAccountRole,
  password: string,
  vaultSessionToken: string,
  signal?: AbortSignal
) {
  return sendSecret<ManagedAccountCredentialResponse>(
    devicePath(classroomId, deviceId, `managed-accounts/${role}/credential`),
    "PUT", password, vaultSessionToken, signal);
}

export function fetchVaultStatus(signal?: AbortSignal) {
  return getJson<VaultStatus>("/api/credential-vault/status", signal);
}

export function unlockVault(masterPassword: string, signal?: AbortSignal) {
  return sendSecret<VaultUnlockResponse>("/api/credential-vault/unlock", "POST", masterPassword, undefined, signal);
}

export function initializeVault(masterPassword: string, signal?: AbortSignal) {
  return sendSecret<VaultStatus>("/api/credential-vault/initialize", "POST", masterPassword, undefined, signal);
}
