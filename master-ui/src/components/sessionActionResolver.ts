import type { WindowsSessionState } from "../api/quickActionsApi";
import type { ManagedAccountRole, ManagedAccountSlot } from "../api/windowsAccountsApi";
import { deviceFeatureCompatibility } from "./deviceFeatureCompatibility";

export type SessionActionId =
  | "LOGIN_PRIMARY"
  | "LOGIN_SECONDARY"
  | "LOGIN_ADMIN"
  | "SWITCH_PRIMARY"
  | "SWITCH_SECONDARY"
  | "SWITCH_ADMIN"
  | "LOGOFF_PRIMARY"
  | "LOGOFF_SECONDARY"
  | "LOGOFF_ADMIN";

export type SessionActionAvailability =
  | "AVAILABLE"
  | "PROFILE_NOT_BOUND"
  | "CREDENTIAL_MISSING"
  | "OFFLINE"
  | "CAPABILITY_MISSING"
  | "OTHER_SESSION_ACTIVE"
  | "UNKNOWN"
  | "REQUIRES_STEP_UP";

export type SessionActionAvailabilityInput = {
  online: boolean;
  sessionState: WindowsSessionState;
  targetRole: ManagedAccountRole;
  profile: ManagedAccountSlot | undefined;
  capabilities: readonly string[];
  sensitiveAuthorizationPresent?: boolean;
};

export function resolveSessionActionAvailability({
  online,
  sessionState,
  targetRole,
  profile,
  capabilities,
  sensitiveAuthorizationPresent = false
}: SessionActionAvailabilityInput): SessionActionAvailability {
  if (!online) return "OFFLINE";
  if (sessionState === "UNKNOWN") return "UNKNOWN";
  if (sessionState === "OTHER_SESSION_ACTIVE") return "OTHER_SESSION_ACTIVE";
  const feature = targetRole === "ADMIN"
    ? "adminSession"
    : sessionState === "NO_SESSION" ? "profileLogon" : "profileSwitch";
  if (deviceFeatureCompatibility(capabilities, feature) !== "AVAILABLE") return "CAPABILITY_MISSING";
  if (!profile?.configured) return "PROFILE_NOT_BOUND";
  if (!profile.credentialConfigured || profile.credentialStatus !== "READY") return "CREDENTIAL_MISSING";
  if (targetRole === "ADMIN" && !sensitiveAuthorizationPresent) return "REQUIRES_STEP_UP";
  return "AVAILABLE";
}

export function sessionAvailabilityMessage(availability: SessionActionAvailability): string | null {
  switch (availability) {
    case "AVAILABLE":
    case "REQUIRES_STEP_UP": return null;
    case "PROFILE_NOT_BOUND": return "Vincula primero una cuenta a este perfil.";
    case "CREDENTIAL_MISSING": return "Guarda primero la contraseña de esta cuenta.";
    case "OFFLINE": return "No hay comunicación con el equipo.";
    case "CAPABILITY_MISSING": return "Este equipo necesita actualizar Galtek Classroom.";
    case "OTHER_SESSION_ACTIVE": return "Hay otra sesión de Windows activa.";
    case "UNKNOWN": return "No se pudo confirmar el estado de la sesión.";
  }
}

export function resolveSessionActions(state: WindowsSessionState, offline: boolean): SessionActionId[] {
  if (offline) return [];
  switch (state) {
    case "NO_SESSION": return ["LOGIN_PRIMARY", "LOGIN_SECONDARY", "LOGIN_ADMIN"];
    case "PRIMARY_ACTIVE": return ["SWITCH_SECONDARY", "SWITCH_ADMIN", "LOGOFF_PRIMARY"];
    case "SECONDARY_ACTIVE": return ["SWITCH_PRIMARY", "SWITCH_ADMIN", "LOGOFF_SECONDARY"];
    case "ADMIN_ACTIVE": return ["SWITCH_PRIMARY", "SWITCH_SECONDARY", "LOGOFF_ADMIN"];
    case "OTHER_SESSION_ACTIVE":
    case "UNKNOWN":
      return [];
  }
}
