import { postJson } from "./apiClient";

export type QuickActionType = "OPEN_URL" | "LOCK_INPUT" | "UNLOCK_INPUT" | "RESTART" | "SHUTDOWN";
export type WindowsSessionState =
  | "PRIMARY_ACTIVE"
  | "SECONDARY_ACTIVE"
  | "OTHER_SESSION_ACTIVE"
  | "NO_SESSION"
  | "UNKNOWN";
export type BatchOperationStatus =
  | "PLANNED" | "PREFLIGHT" | "RUNNING" | "SUCCESS" | "PARTIAL_SUCCESS"
  | "FAILED" | "CANCELLED" | "ROLLING_BACK" | "ROLLED_BACK";
export type TargetExecutionStatus =
  | "PENDING" | "NO_CHANGE" | "SUCCESS" | "FAILED" | "SKIPPED" | "CANCELLED" | "ROLLED_BACK";

export type BatchTargetResult = {
  deviceId: string;
  status: TargetExecutionStatus;
  errorCode: string | null;
  message: string | null;
  attempt: number;
};

export type BatchOperationResult = {
  operationId: string;
  type: QuickActionType;
  status: BatchOperationStatus;
  targetCount: number;
  successCount: number;
  failedCount: number;
  targets: BatchTargetResult[];
};

export type BatchOperationResponse = BatchOperationResult;

export type WindowsSessionStateTarget = {
  deviceId: string;
  state: WindowsSessionState;
  online: boolean;
  errorCode: string | null;
  message: string | null;
};

export type WindowsSessionStateBatchResponse = {
  classroomId: string;
  targetCount: number;
  targets: WindowsSessionStateTarget[];
};

function classroomPath(classroomId: string, suffix: string) {
  return `/api/classrooms/${encodeURIComponent(classroomId)}/${suffix}`;
}

export function openUrl(classroomId: string, targetDeviceIds: ReadonlySet<string>, url: string, signal?: AbortSignal) {
  return postJson<BatchOperationResponse>(classroomPath(classroomId, "open-url"),
    { url, targetDeviceIds: [...targetDeviceIds] }, signal);
}

export function setInputLocked(classroomId: string, targetDeviceIds: ReadonlySet<string>, locked: boolean, signal?: AbortSignal) {
  return postJson<BatchOperationResponse>(classroomPath(classroomId, `input-control/${locked ? "lock" : "unlock"}`),
    { targetDeviceIds: [...targetDeviceIds] }, signal);
}

export function powerControl(classroomId: string, targetDeviceIds: ReadonlySet<string>, type: "RESTART" | "SHUTDOWN", signal?: AbortSignal) {
  return postJson<BatchOperationResponse>(classroomPath(classroomId, "power-control"),
    { type, targetDeviceIds: [...targetDeviceIds] }, signal);
}

export function fetchWindowsSessionStates(
  classroomId: string,
  targetDeviceIds: ReadonlySet<string>,
  signal?: AbortSignal
) {
  return postJson<WindowsSessionStateBatchResponse>(classroomPath(classroomId, "windows-session-state"),
    { targetDeviceIds: [...targetDeviceIds] }, signal);
}
