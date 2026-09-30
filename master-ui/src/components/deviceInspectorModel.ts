import { ApiError } from "../api/apiClient";
import type { DeviceActivityEvent, ManagedAccountRole } from "../api/windowsAccountsApi";
import { managedRoleLabels } from "./windowsAccountViewModel";

export type RevealRowPhase = "idle" | "loading" | "available" | "failed" | "missing";

export function revealFailurePhase(error: unknown): RevealRowPhase {
  return error instanceof ApiError && error.code === "CREDENTIAL_NOT_FOUND" ? "missing" : "failed";
}

export function showInitialSessionLoading(fetchPending: boolean, hasSnapshot: boolean) {
  return fetchPending && !hasSnapshot;
}

export type ManagedMutationFeedback = {
  summary: string;
  detail: string;
  severity: "warn" | "error";
};

export function managedMutationFeedback(
  action: "bind" | "unbind" | "credential" | "removeCredential",
  role: ManagedAccountRole,
  deviceName: string,
  errorCode?: string | null,
  fallback?: string | null
): ManagedMutationFeedback {
  const roleLabel = managedRoleLabels[role];
  if (errorCode === "MANAGED_ACCOUNT_SESSION_ACTIVE") {
    return {
      summary: `No se puede desvincular ${roleLabel}`,
      detail: `La cuenta de ${roleLabel} está activa en ${deviceName}. Cambia a otro perfil o cierra la sesión antes de desvincularla.`,
      severity: "warn"
    };
  }
  if (errorCode === "DEVICE_OPERATION_IN_PROGRESS") {
    return {
      summary: `${deviceName} tiene una operación en curso`,
      detail: "Espera a que termine antes de enviar otra acción.",
      severity: "warn"
    };
  }
  const summary = action === "unbind" ? `No se pudo desvincular ${roleLabel}`
    : action === "bind" ? `No se pudo vincular ${roleLabel}`
    : action === "removeCredential" ? `No se pudo eliminar la contraseña de ${roleLabel}`
    : `No se pudo guardar la contraseña de ${roleLabel}`;
  return {
    summary,
    detail: fallback && !looksTechnical(fallback) ? fallback : "No pudimos completar la acción. Vuelve a intentarlo.",
    severity: "error"
  };
}

export function isDeterministicHttpRejection(error: unknown) {
  return error instanceof ApiError && error.status >= 400 && error.status < 500;
}

export function deviceActivityText(event: DeviceActivityEvent) {
  const role = event.role ? managedRoleLabels[event.role] : "perfil";
  const success = event.result === "SUCCESS" || event.result === "NO_CHANGE";
  const successLabels: Record<string, string> = {
    MANAGED_PROFILE_BOUND: `Cuenta ${event.accountReference ?? "Windows"} vinculada a ${role}`,
    MANAGED_PROFILE_UNBOUND: `Perfil ${role} desvinculado`,
    MANAGED_CREDENTIAL_REGISTERED: `Contraseña de ${role} registrada`,
    MANAGED_CREDENTIAL_UPDATED: `Contraseña de ${role} actualizada`,
    MANAGED_CREDENTIAL_REMOVED: `Contraseña de ${role} eliminada`,
    MANAGED_CREDENTIAL_REVEALED: `Contraseña de ${role} visualizada`,
    WINDOWS_SESSION_LOGON_REQUESTED: `Inicio de ${role} solicitado`,
    WINDOWS_SESSION_LOGON_SUCCEEDED: `Sesión de ${role} iniciada`,
    WINDOWS_SESSION_SWITCH_REQUESTED: `Cambio a ${role} solicitado`,
    WINDOWS_SESSION_SWITCH_SUCCEEDED: `Sesión cambiada a ${role}`,
    WINDOWS_SESSION_LOGOFF_REQUESTED: `Cierre de ${role} solicitado`,
    WINDOWS_SESSION_LOGOFF_SUCCEEDED: `Sesión de ${role} cerrada`,
    ADMIN_SESSION_AUTHORIZED: "Acceso a Administración autorizado"
  };
  const failureLabels: Record<string, string> = {
    MANAGED_PROFILE_BOUND: `No se pudo vincular ${role}`,
    MANAGED_PROFILE_UNBOUND: `No se pudo desvincular ${role}`,
    MANAGED_CREDENTIAL_REGISTERED: `No se pudo registrar la contraseña de ${role}`,
    MANAGED_CREDENTIAL_UPDATED: `No se pudo actualizar la contraseña de ${role}`,
    MANAGED_CREDENTIAL_REMOVED: `No se pudo eliminar la contraseña de ${role}`,
    MANAGED_CREDENTIAL_REVEALED: `No se pudo visualizar la contraseña de ${role}`,
    WINDOWS_SESSION_LOGON_FAILED: `No se pudo iniciar ${role}`,
    WINDOWS_SESSION_SWITCH_FAILED: `No se pudo cambiar a ${role}`,
    WINDOWS_SESSION_LOGOFF_FAILED: `No se pudo cerrar ${role}`,
    ADMIN_SESSION_AUTHORIZATION_FAILED: "Autorización de Administración rechazada"
  };
  return (success ? successLabels[event.eventType] : failureLabels[event.eventType])
    ?? `${success ? "Acción completada" : "Acción no completada"}: ${role}`;
}

function looksTechnical(message: string) {
  return /\b[A-Z][A-Z0-9_]{3,}\b/.test(message) || /^Managed |^Device |^Windows account /i.test(message);
}
