import type {
  BatchOperationResult,
  BatchTargetResult,
  QuickActionType,
  TargetExecutionStatus
} from "../api/quickActionsApi";
import type { ClassroomDeviceCardData } from "../types/classroom";

export type ResultTone = "success" | "warning" | "danger" | "unknown" | "neutral";
export type TargetResultTone = "success" | "problem" | "unknown" | "pending" | "neutral";

export type OperationResultTargetView = {
  deviceId: string;
  deviceName: string;
  status: TargetExecutionStatus;
  tone: TargetResultTone;
  resultText: string;
  errorCode: string | null;
};

export type OperationResultView = {
  actionType: QuickActionType;
  actionName: string;
  globalText: string;
  tone: ResultTone;
  successSummary: string;
  problemSummary: string;
  targets: OperationResultTargetView[];
};

type KnownOperationErrorCode =
  | "DEVICE_OFFLINE"
  | "DEVICE_NOT_REGISTERED"
  | "DEVICE_NOT_FOUND"
  | "MASTER_NOT_PAIRED"
  | "CLIENT_REVOKED"
  | "CAPABILITY_NOT_SUPPORTED"
  | "SESSION_AGENT_UNAVAILABLE"
  | "OPERATION_REJECTED"
  | "OPERATION_RESULT_UNKNOWN"
  | "POWER_CONTROL_UNAVAILABLE"
  | "POWER_CONTROL_FAILED"
  | "URL_BLOCKED_BY_POLICY"
  | "INVALID_URL"
  | "SESSION_COMMAND_RESULT_UNKNOWN"
  | "URL_LAUNCH_FAILED"
  | "INPUT_LOCK_FAILED"
  | "INPUT_UNLOCK_FAILED"
  | "AGENT_UNAVAILABLE"
  | "SESSION_NOT_AVAILABLE"
  | "DEVICE_BUSY";

const actionNames: Record<QuickActionType, string> = {
  OPEN_URL: "Abrir URL",
  LOCK_INPUT: "Bloquear teclado y mouse",
  UNLOCK_INPUT: "Desbloquear teclado y mouse",
  RESTART: "Reiniciar equipos",
  SHUTDOWN: "Apagar equipos"
};

const successCopy: Record<QuickActionType, string> = {
  OPEN_URL: "URL abierta.",
  LOCK_INPUT: "Bloqueo de teclado y mouse aplicado.",
  UNLOCK_INPUT: "Desbloqueo de teclado y mouse aplicado.",
  RESTART: "Solicitud de reinicio aceptada.",
  SHUTDOWN: "Solicitud de apagado aceptada."
};

const errorCopy: Record<KnownOperationErrorCode, string> = {
  DEVICE_OFFLINE: "Sin comunicacion con el equipo.",
  DEVICE_NOT_REGISTERED: "El equipo no esta registrado correctamente.",
  DEVICE_NOT_FOUND: "El equipo ya no pertenece a esta aula.",
  MASTER_NOT_PAIRED: "El equipo necesita volver a vincularse con este Master.",
  CLIENT_REVOKED: "La vinculacion de este equipo fue revocada.",
  CAPABILITY_NOT_SUPPORTED: "Esta version del equipo no admite esta accion.",
  SESSION_AGENT_UNAVAILABLE: "La sesion de Windows no esta disponible para esta accion.",
  OPERATION_REJECTED: "El equipo rechazo la accion.",
  OPERATION_RESULT_UNKNOWN: "No se pudo confirmar el resultado.",
  POWER_CONTROL_UNAVAILABLE: "Windows no permitio preparar esta accion.",
  POWER_CONTROL_FAILED: "Windows no pudo completar la solicitud.",
  URL_BLOCKED_BY_POLICY: "La politica de navegacion bloqueo esta URL.",
  INVALID_URL: "La URL no es valida para esta accion.",
  SESSION_COMMAND_RESULT_UNKNOWN: "No se pudo confirmar el resultado.",
  URL_LAUNCH_FAILED: "Windows no pudo abrir la URL.",
  INPUT_LOCK_FAILED: "Windows no pudo aplicar el bloqueo.",
  INPUT_UNLOCK_FAILED: "Windows no pudo aplicar el desbloqueo.",
  AGENT_UNAVAILABLE: "El agente del equipo no esta disponible.",
  SESSION_NOT_AVAILABLE: "La sesion de Windows no esta disponible.",
  DEVICE_BUSY: "El equipo esta ocupado."
};

export function toOperationResultView(
  result: BatchOperationResult,
  devices: ClassroomDeviceCardData[]
): OperationResultView {
  const devicesById = new Map(devices.map((device) => [device.id, device]));
  const targets = result.targets.map((target) => toTargetView(result.type, target, devicesById));
  const hasUncertain = targets.some((target) => target.tone === "unknown");
  const hasPending = targets.some((target) => target.tone === "pending");
  const allSuccess = targets.length > 0 && targets.every((target) => target.tone === "success");
  const allProblem = targets.length > 0 && targets.every((target) => target.tone === "problem" || target.tone === "unknown");
  const problemCount = Math.max(0, result.targetCount - result.successCount);

  return {
    actionType: result.type,
    actionName: actionNames[result.type],
    globalText: globalResultText({ hasPending, hasUncertain, allSuccess, allProblem }),
    tone: globalResultTone({ hasPending, hasUncertain, allSuccess, allProblem }),
    successSummary: `${result.successCount} ${result.successCount === 1 ? "correcto" : "correctos"}`,
    problemSummary: `${problemCount} ${problemCount === 1 ? "con problema" : "con problema"}`,
    targets
  };
}

function toTargetView(
  actionType: QuickActionType,
  target: BatchTargetResult,
  devicesById: Map<string, ClassroomDeviceCardData>
): OperationResultTargetView {
  const tone = targetTone(target);
  return {
    deviceId: target.deviceId,
    deviceName: devicesById.get(target.deviceId)?.label ?? `Equipo ${shortId(target.deviceId)}`,
    status: target.status,
    tone,
    resultText: targetResultText(actionType, target),
    errorCode: target.errorCode
  };
}

function globalResultText({
  hasPending,
  hasUncertain,
  allSuccess,
  allProblem
}: {
  hasPending: boolean;
  hasUncertain: boolean;
  allSuccess: boolean;
  allProblem: boolean;
}) {
  if (hasPending) return "Resultado pendiente";
  if (hasUncertain) return "No se pudo confirmar el resultado";
  if (allSuccess) return "Completado";
  if (allProblem) return "No se pudo completar";
  return "Completado con incidencias";
}

function globalResultTone({
  hasPending,
  hasUncertain,
  allSuccess,
  allProblem
}: {
  hasPending: boolean;
  hasUncertain: boolean;
  allSuccess: boolean;
  allProblem: boolean;
}): ResultTone {
  if (hasPending) return "neutral";
  if (hasUncertain) return "unknown";
  if (allSuccess) return "success";
  if (allProblem) return "danger";
  return "warning";
}

function targetTone(target: BatchTargetResult): TargetResultTone {
  if (target.status === "SUCCESS" || target.status === "NO_CHANGE") return "success";
  if (target.status === "PENDING") return "pending";
  if (target.errorCode === "OPERATION_RESULT_UNKNOWN" || target.errorCode === "SESSION_COMMAND_RESULT_UNKNOWN") {
    return "unknown";
  }
  if (target.status === "FAILED" || target.status === "CANCELLED" || target.status === "ROLLED_BACK") {
    return "problem";
  }
  return "neutral";
}

function targetResultText(actionType: QuickActionType, target: BatchTargetResult) {
  if (target.status === "SUCCESS") return successCopy[actionType];
  if (target.status === "NO_CHANGE") return "Sin cambios.";
  if (target.status === "PENDING") return "Pendiente.";
  if (target.status === "SKIPPED") return "No se ejecuto en este equipo.";
  if (target.status === "CANCELLED") return "Accion cancelada.";
  if (target.status === "ROLLED_BACK") return "Accion revertida.";
  return humanError(target.errorCode);
}

export function humanError(errorCode: string | null) {
  if (isKnownErrorCode(errorCode)) return errorCopy[errorCode];
  return "No se pudo completar la accion.";
}

function isKnownErrorCode(errorCode: string | null): errorCode is KnownOperationErrorCode {
  return errorCode !== null && Object.prototype.hasOwnProperty.call(errorCopy, errorCode);
}

function shortId(deviceId: string) {
  return deviceId.trim().slice(0, 8) || "desconocido";
}
