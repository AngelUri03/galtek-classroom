export type DashboardMetric = {
  id: "devices" | "online" | "assigned" | "free";
  label: string;
  value: number;
  detail: string;
  tone: "neutral" | "info" | "success" | "warning" | "danger";
};

export type DeviceCardStatus = "online" | "offline" | "connecting" | "unavailable" | "busy" | "error";

export type ClassroomDeviceCardData = {
  id: string;
  label: string;
  studentName: string;
  status: DeviceCardStatus;
  rawStatus: string;
  statusLabel: string;
  secondaryStatus: string;
  note: string;
  capabilities: string[];
  assignedStudentId: string | null;
  assignedStudentGroupId: string | null;
  assignedStudentGroupName: string | null;
  hostname: string | null;
};

export type ClassroomDashboardData = {
  classroomName: string;
  groups: ClassroomGroupData[];
  summary: DashboardMetric[];
  devices: ClassroomDeviceCardData[];
};

export type ClassroomGroupData = {
  id: string;
  label: string;
  active: boolean;
};

export type ClassroomOption = {
  classroomId: string;
  displayName: string;
  deviceCount?: number;
};
