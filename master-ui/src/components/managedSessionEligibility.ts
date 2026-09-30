import type { ManagedSessionRole, WindowsSessionState } from "../api/quickActionsApi";
import type { ManagedAccountStatusResponse } from "../api/windowsAccountsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";
import { deviceFeatureCompatibility } from "./deviceFeatureCompatibility";

export type ManagedSessionEligibilityContext = {
  device: ClassroomDeviceCardData;
  sessionState: WindowsSessionState;
};

export type ManagedSessionEligibility = { enabled: boolean; ready: number; blocked: number; reasons: string[] };

export function resolveManagedSessionEligibility(
  role: ManagedSessionRole,
  contexts: ManagedSessionEligibilityContext[],
  managedByDeviceId: Map<string, ManagedAccountStatusResponse>,
  loading: boolean
): ManagedSessionEligibility {
  if (loading) return { enabled: false, ready: 0, blocked: contexts.length, reasons: ["Consultando perfiles…"] };
  let ready = 0;
  const reasons: string[] = [];
  for (const context of contexts) {
    let reason: string | null = null;
    if (context.device.rawStatus !== "ONLINE") reason = "Sin comunicación";
    else if (context.sessionState === "OTHER_SESSION_ACTIVE") reason = "Hay otra sesión de Windows activa";
    else if (context.sessionState === "UNKNOWN") reason = "No se pudo confirmar el estado de la sesión";
    else if (deviceFeatureCompatibility(context.device.capabilities,
      role === "ADMIN" ? "adminSession" : context.sessionState === "NO_SESSION" ? "profileLogon" : "profileSwitch") !== "AVAILABLE") {
      reason = "Este equipo necesita actualizar Galtek Classroom";
    } else if (context.sessionState !== `${role}_ACTIVE`) {
      const slot = managedByDeviceId.get(context.device.id)?.accounts.find((account) => account.accountId === role);
      if (!slot?.configured) reason = `Falta vincular ${role === "PRIMARY" ? "Primaria" : role === "SECONDARY" ? "Secundaria" : "Administración"}`;
      else if (slot.credentialStatus !== "READY") reason = "Falta la contraseña administrada";
    }
    if (reason) reasons.push(`${context.device.label}: ${reason}`);
    else ready++;
  }
  return { enabled: contexts.length > 0 && reasons.length === 0, ready, blocked: reasons.length, reasons };
}
