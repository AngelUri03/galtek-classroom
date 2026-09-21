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
  statusLabel: string;
  secondaryStatus: string;
  note: string;
};

export type ClassroomDashboardData = {
  classroomName: string;
  summary: DashboardMetric[];
  devices: ClassroomDeviceCardData[];
};

export type ClassroomOption = {
  classroomId: string;
  displayName: string;
};
