export type DashboardMetric = {
  id: string;
  label: string;
  value: number;
  detail: string;
  tone: "neutral" | "info" | "success" | "warning" | "danger";
};

export type MockDeviceStatus = "online" | "offline" | "inUse" | "attention" | "degraded";

export type MockClassroomDevice = {
  id: string;
  label: string;
  studentName: string;
  status: MockDeviceStatus;
  secondaryStatus: string;
  note: string;
  selected?: boolean;
};

export type ClassroomDashboardData = {
  classroomName: string;
  teacherName: string;
  masterStatus: string;
  summary: DashboardMetric[];
  devices: MockClassroomDevice[];
};
