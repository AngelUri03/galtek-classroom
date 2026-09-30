import { authorizeSensitiveAction, deleteWithVaultSession, getJson, getJsonWithVaultSession, putJson, revealSensitiveSecret, sendSecret } from "./apiClient";

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
  vaultCredentialConfigured?: boolean | null;
  combinedCredentialState?: "READY" | "PENDING_CLIENT_SYNC" | "CLIENT_ONLY_NOT_REVEALABLE" | "NO_CREDENTIAL" | "UNKNOWN";
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
export type DeviceActivityEvent = {
  eventId: string;
  classroomId: string;
  deviceId: string;
  eventType: string;
  actor: string;
  occurredAtUtc: string;
  role: ManagedAccountRole | null;
  accountReference: string | null;
  result: string;
  message: string | null;
};

function devicePath(classroomId: string, deviceId: string, suffix: string) {
  return `/api/classrooms/${encodeURIComponent(classroomId)}/devices/${encodeURIComponent(deviceId)}/${suffix}`;
}

export function fetchWindowsAccounts(classroomId: string, deviceId: string, signal?: AbortSignal) {
  return getJson<WindowsAccountInventoryResponse>(
    devicePath(classroomId, deviceId, "windows-accounts"), signal);
}

export function fetchManagedAccounts(
  classroomId: string,
  deviceId: string,
  vaultSessionToken?: string | null,
  signal?: AbortSignal
) {
  const path = devicePath(classroomId, deviceId, "managed-accounts");
  return vaultSessionToken
    ? getJsonWithVaultSession<ManagedAccountStatusResponse>(path, vaultSessionToken, signal)
    : getJson<ManagedAccountStatusResponse>(path, signal);
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
  vaultSessionToken: string,
  signal?: AbortSignal
) {
  return deleteWithVaultSession<ManagedAccountMutationResponse>(
    devicePath(classroomId, deviceId, `managed-accounts/${role}`), vaultSessionToken, signal);
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

export function removeManagedCredential(
  classroomId: string,
  deviceId: string,
  role: ManagedAccountRole,
  vaultSessionToken: string,
  signal?: AbortSignal
) {
  return deleteWithVaultSession<ManagedAccountMutationResponse>(
    devicePath(classroomId, deviceId, `managed-accounts/${role}/credential`), vaultSessionToken, signal);
}

export function fetchVaultStatus(signal?: AbortSignal) {
  return getJson<VaultStatus>("/api/credential-vault/status", signal);
}

export function fetchDeviceActivity(classroomId: string, deviceId: string, signal?: AbortSignal) {
  return getJson<{ events: DeviceActivityEvent[] }>(
    `${devicePath(classroomId, deviceId, "activity")}?date=today&limit=50`, signal);
}

export function unlockVault(masterPassword: string, signal?: AbortSignal) {
  return sendSecret<VaultUnlockResponse>("/api/credential-vault/unlock", "POST", masterPassword, undefined, signal);
}

export function initializeVault(masterPassword: string, signal?: AbortSignal) {
  return sendSecret<VaultStatus>("/api/credential-vault/initialize", "POST", masterPassword, undefined, signal);
}

export type SensitiveAuthorizationResponse = {
  sensitiveAuthorizationToken: string;
  expiresAtUtc: string;
};

export function authorizeCredentialReveal(
  classroomId: string,
  deviceId: string,
  masterPassword: string,
  signal?: AbortSignal
) {
  return authorizeSensitiveAction<SensitiveAuthorizationResponse>(
    devicePath(classroomId, deviceId, "sensitive-authorizations/credential-reveal"),
    masterPassword,
    {},
    signal);
}

export function revealManagedCredential(
  classroomId: string,
  deviceId: string,
  role: ManagedAccountRole,
  sensitiveAuthorization: string,
  signal?: AbortSignal
) {
  return revealSensitiveSecret(
    devicePath(classroomId, deviceId, `managed-accounts/${role}/credential/reveal`),
    sensitiveAuthorization,
    signal);
}
