import { postJson } from "./apiClient";

export type QuickActionType = "OPEN_URL" | "LOCK_INPUT" | "UNLOCK_INPUT" | "RESTART" | "SHUTDOWN";
export type BatchOperationStatus =
  | "PLANNED" | "PREFLIGHT" | "RUNNING" | "SUCCESS" | "PARTIAL_SUCCESS"
  | "FAILED" | "CANCELLED" | "ROLLING_BACK" | "ROLLED_BACK";
export type TargetExecutionStatus =
  | "PENDING" | "NO_CHANGE" | "SUCCESS" | "FAILED" | "SKIPPED" | "CANCELLED" | "ROLLED_BACK";

export type BatchOperationResponse = {
  operationId: string;
  type: QuickActionType;
  status: BatchOperationStatus;
  targetCount: number;
  successCount: number;
  failedCount: number;
  targets: {
    deviceId: string;
    status: TargetExecutionStatus;
    errorCode: string | null;
    message: string | null;
    attempt: number;
  }[];
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
