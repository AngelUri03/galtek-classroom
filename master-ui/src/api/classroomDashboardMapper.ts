import type {
  ClassroomDeviceResponse,
  ClassroomDeviceStatus,
  ClassroomSnapshotResponse
} from "./masterApi";
import type {
  ClassroomDashboardData,
  ClassroomDeviceCardData,
  DeviceCardStatus
} from "../types/classroom";

const statusLabels: Record<ClassroomDeviceStatus, string> = {
  ONLINE: "En linea",
  OFFLINE: "Sin conexion",
  CONNECTING: "Conectando",
  UNLICENSED: "Sin licencia",
  LICENSE_BLOCKED: "Licencia bloqueada",
  AGENT_UNAVAILABLE: "Agent no disponible",
  SESSION_UNAVAILABLE: "Sesion no disponible",
  BUSY: "Ocupado",
  ERROR: "Error"
};

export function toClassroomDashboardData(snapshot: ClassroomSnapshotResponse): ClassroomDashboardData {
  const onlineDevices = snapshot.devices.filter((device) => device.status === "ONLINE").length;

  return {
    classroomName: snapshot.classroom.displayName,
    summary: [
      {
        id: "devices",
        label: "Equipos",
        value: snapshot.summary.deviceCount,
        detail: "Registrados en el aula",
        tone: "neutral"
      },
      {
        id: "online",
        label: "En linea",
        value: onlineDevices,
        detail: "Presencia actual",
        tone: "success"
      },
      {
        id: "assigned",
        label: "Asignados",
        value: snapshot.summary.assignedDeviceCount,
        detail: "Con alumno actual",
        tone: "info"
      },
      {
        id: "free",
        label: "Libres",
        value: snapshot.summary.freeDeviceCount,
        detail: "Sin alumno asignado",
        tone: "neutral"
      }
    ],
    devices: snapshot.devices.map(toDeviceCardData)
  };
}

function toDeviceCardData(device: ClassroomDeviceResponse): ClassroomDeviceCardData {
  const label = readableDeviceName(device);
  const hostname = clean(device.hostname);
  const hostnameDetail = hostname && hostname !== clean(device.displayName) ? `Host ${hostname}` : null;

  return {
    id: device.deviceId,
    label,
    studentName: clean(device.assignedStudentDisplayName) ?? "Sin alumno asignado",
    status: toCardStatus(device.status),
    statusLabel: statusLabels[device.status],
    secondaryStatus: formatLastSeen(device.lastSeenUtc),
    note: hostnameDetail ?? `ID ${shortDeviceId(device.deviceId)}`
  };
}

function toCardStatus(status: ClassroomDeviceStatus): DeviceCardStatus {
  switch (status) {
    case "ONLINE":
      return "online";
    case "OFFLINE":
      return "offline";
    case "CONNECTING":
      return "connecting";
    case "BUSY":
      return "busy";
    case "ERROR":
    case "AGENT_UNAVAILABLE":
    case "SESSION_UNAVAILABLE":
      return "error";
    case "UNLICENSED":
    case "LICENSE_BLOCKED":
      return "unavailable";
  }
}

function readableDeviceName(device: ClassroomDeviceResponse): string {
  return clean(device.displayName) ?? `Equipo ${shortDeviceId(device.deviceId)}`;
}

function shortDeviceId(deviceId: string): string {
  return deviceId.length <= 8 ? deviceId : deviceId.slice(-8);
}

function formatLastSeen(value: string | null): string {
  if (!value) {
    return "Sin senal registrada";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Ultima senal registrada";
  }

  return `Ultima senal ${date.toLocaleString("es-MX", {
    dateStyle: "short",
    timeStyle: "short"
  })}`;
}

function clean(value: string | null): string | null {
  if (!value || !value.trim()) {
    return null;
  }

  return value.trim();
}
