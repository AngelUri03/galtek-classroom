import { getJson } from "./apiClient";

export type MasterAuthorization = {
  status: string;
  authorized: boolean;
  configured: boolean;
  boundAccountDisplayName: string | null;
  currentAccountDisplayName: string | null;
};

export type MasterStorage = {
  status: string;
  errorCode: string | null;
};

export type ClassroomCounts = {
  groupCount: number;
  activeStudentCount: number;
  archivedStudentCount: number;
  deviceCount: number;
  currentAssignmentCount: number;
  applicationCount: number;
};

export type ClassroomSummaryResponse = {
  classroomId: string;
  displayName: string;
  active: boolean;
  version: number;
  counts: ClassroomCounts;
};

export type MasterBootstrapResponse = {
  authorization: MasterAuthorization;
  storage: MasterStorage;
  classrooms: ClassroomSummaryResponse[];
};

export type ClassroomResponse = ClassroomSummaryResponse & {
  authorizedApplicationIds: string[];
  defaultBrowserProfileId: string | null;
  workspaceRecoveryPlanned: boolean;
  batchConfirmationsRequired: boolean;
};

export type ClassroomDeviceStatus =
  | "ONLINE"
  | "OFFLINE"
  | "CONNECTING"
  | "UNLICENSED"
  | "LICENSE_BLOCKED"
  | "AGENT_UNAVAILABLE"
  | "SESSION_UNAVAILABLE"
  | "BUSY"
  | "ERROR";

export type ClassroomDeviceResponse = {
  deviceId: string;
  classroomId: string;
  installationId: string;
  displayName: string | null;
  hostname: string | null;
  status: ClassroomDeviceStatus;
  lastSeenUtc: string | null;
  capabilities: string[];
  assignedStudentId: string | null;
  assignedStudentDisplayName: string | null;
  active: boolean;
  version: number;
};

export type ClassroomGroupResponse = {
  groupId: string;
  classroomId: string;
  grade: string | null;
  section: string | null;
  displayName: string;
  active: boolean;
  activeStudentCount: number;
  version: number;
};

export type ClassroomStudentResponse = {
  studentId: string;
  classroomId: string;
  groupId: string | null;
  groupDisplayName: string | null;
  displayName: string;
  active: boolean;
};

export type ClassroomAssignmentResponse = {
  assignmentId: string;
  studentId: string;
  studentDisplayName: string;
  deviceId: string;
  deviceDisplayName: string | null;
  status: string;
  current: boolean;
};

export type ClassroomApplicationResponse = {
  applicationId: string;
  displayName: string;
  type: string;
  availability: string;
  launchPolicy: string;
  active: boolean;
  version: number;
};

export type ClassroomSnapshotSummary = {
  groupCount: number;
  studentCount: number;
  activeStudentCount: number;
  deviceCount: number;
  freeDeviceCount: number;
  assignedDeviceCount: number;
  currentAssignmentCount: number;
  applicationCount: number;
};

export type ClassroomSnapshotResponse = {
  classroom: ClassroomResponse;
  groups: ClassroomGroupResponse[];
  students: ClassroomStudentResponse[];
  devices: ClassroomDeviceResponse[];
  currentAssignments: ClassroomAssignmentResponse[];
  applications: ClassroomApplicationResponse[];
  summary: ClassroomSnapshotSummary;
};

// Data contract sources:
// - Bootstrap: GET /api/master/bootstrap selects the initial active classroom.
// - Dashboard read model: GET /api/classrooms/{id}/snapshot provides classroom, Devices, assignments and live presence.
export function fetchMasterBootstrap(signal?: AbortSignal) {
  return getJson<MasterBootstrapResponse>("/api/master/bootstrap", signal);
}

export function fetchClassroomSnapshot(classroomId: string, signal?: AbortSignal) {
  return getJson<ClassroomSnapshotResponse>(
    `/api/classrooms/${encodeURIComponent(classroomId)}/snapshot`,
    signal
  );
}
