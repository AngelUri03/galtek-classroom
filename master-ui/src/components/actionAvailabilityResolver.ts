import type { QuickActionType, WindowsSessionState } from "../api/quickActionsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";

export type ActionPresentation = "PRIMARY" | "AVAILABLE" | "DISABLED" | "HIDDEN_FROM_PRIMARY" | "BLOCKED" | "LOADING";

export type ActionBlockReason = {
  deviceId: string;
  deviceName: string;
  reason: string;
};

export type ActionAvailability = {
  action: QuickActionType;
  presentation: ActionPresentation;
  enabled: boolean;
  eligibleCount: number;
  blockedCount: number;
  reasons: ActionBlockReason[];
};

export type DeviceSessionContext = {
  device: ClassroomDeviceCardData;
  sessionState: WindowsSessionState;
  sessionLoading: boolean;
};

const interactiveStates: WindowsSessionState[] = [
  "PRIMARY_ACTIVE",
  "SECONDARY_ACTIVE",
  "OTHER_SESSION_ACTIVE"
];

export const sessionStateLabels: Record<WindowsSessionState, string> = {
  PRIMARY_ACTIVE: "Primaria activa",
  SECONDARY_ACTIVE: "Secundaria activa",
  OTHER_SESSION_ACTIVE: "Otra sesión activa",
  NO_SESSION: "Sin sesión iniciada",
  UNKNOWN: "Sesión no disponible"
};

export function resolveActionAvailability(
  action: QuickActionType,
  contexts: DeviceSessionContext[]
): ActionAvailability {
  const reasons: ActionBlockReason[] = [];
  let loadingCount = 0;

  for (const context of contexts) {
    const reason = blockedReason(action, context);
    if (context.sessionLoading && requiresSession(action)) {
      loadingCount += 1;
    }
    if (reason) {
      reasons.push({
        deviceId: context.device.id,
        deviceName: context.device.label,
        reason
      });
    }
  }

  const blockedCount = reasons.length;
  const eligibleCount = Math.max(0, contexts.length - blockedCount);
  const loading = loadingCount > 0 && contexts.length > 0;
  const enabled = contexts.length > 0 && !loading && blockedCount === 0;

  return {
    action,
    presentation: presentationFor(action, contexts.length, loading, eligibleCount, blockedCount),
    enabled,
    eligibleCount,
    blockedCount,
    reasons
  };
}

function presentationFor(
  action: QuickActionType,
  targetCount: number,
  loading: boolean,
  eligibleCount: number,
  blockedCount: number
): ActionPresentation {
  if (targetCount === 0) return "HIDDEN_FROM_PRIMARY";
  if (loading) return "LOADING";
  if (blockedCount > 0 && eligibleCount > 0) return "BLOCKED";
  if (blockedCount > 0) return action === "OPEN_URL" ? "HIDDEN_FROM_PRIMARY" : "DISABLED";
  if (action === "OPEN_URL") return "PRIMARY";
  return "AVAILABLE";
}

function blockedReason(action: QuickActionType, context: DeviceSessionContext) {
  if (!isOnline(context.device)) return "Sin comunicación";
  if (!requiresSession(action)) return null;
  if (context.sessionLoading) return "Consultando sesión de Windows";
  if (interactiveStates.includes(context.sessionState)) return null;
  if (context.sessionState === "NO_SESSION") return "Sin sesión iniciada";
  return "Estado de sesión no disponible";
}

function requiresSession(action: QuickActionType) {
  return action === "OPEN_URL" || action === "LOCK_INPUT" || action === "UNLOCK_INPUT";
}

function isOnline(device: ClassroomDeviceCardData) {
  return device.rawStatus === "ONLINE" || device.status === "online";
}
