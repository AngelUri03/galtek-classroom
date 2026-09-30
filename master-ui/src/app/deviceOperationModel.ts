export type DeviceOperationPhase = "IN_PROGRESS" | "RECONCILIATION_REQUIRED";
export type DeviceOperationSnapshot = {
  deviceId: string;
  state: DeviceOperationPhase;
  operationType: string;
  targetProfile?: string | null;
  startedAtUtc: string;
};

export function hasBusyDevice(
  operations: ReadonlyMap<string, DeviceOperationSnapshot>,
  deviceIds: Iterable<string>
) {
  for (const deviceId of deviceIds) if (operations.has(deviceId)) return true;
  return false;
}

export function claimDeviceOperations(
  current: ReadonlyMap<string, DeviceOperationSnapshot>,
  deviceIds: Iterable<string>,
  operationType: string,
  targetProfile: string | null | undefined,
  startedAtUtc: string
) {
  const ids = [...new Set(deviceIds)];
  if (ids.some((deviceId) => current.has(deviceId))) {
    return { accepted: false as const, operations: new Map(current) };
  }
  const operations = new Map(current);
  for (const deviceId of ids) {
    operations.set(deviceId, { deviceId, state: "IN_PROGRESS", operationType, targetProfile, startedAtUtc });
  }
  return { accepted: true as const, operations };
}

export function replaceDeviceOperations(
  current: ReadonlyMap<string, DeviceOperationSnapshot>,
  requestedDeviceIds: Iterable<string>,
  serverOperations: Iterable<DeviceOperationSnapshot>
) {
  const next = new Map(current);
  for (const deviceId of requestedDeviceIds) next.delete(deviceId);
  for (const operation of serverOperations) next.set(operation.deviceId, operation);
  return next;
}

export function deviceOperationLabel(operation: DeviceOperationSnapshot) {
  if (operation.state === "RECONCILIATION_REQUIRED") return "Estado por confirmar";
  if (operation.operationType === "LOGOFF_WINDOWS_SESSION") return "Cerrando sesión…";
  if (operation.targetProfile === "ADMIN") return "Cambiando a Administración…";
  if (operation.targetProfile === "PRIMARY") return "Cambiando a Primaria…";
  if (operation.targetProfile === "SECONDARY") return "Cambiando a Secundaria…";
  if (operation.operationType === "UNBIND_MANAGED_ACCOUNT") return "Desvinculando perfil…";
  if (operation.operationType === "BIND_MANAGED_ACCOUNT") return "Vinculando perfil…";
  if (operation.operationType === "CONFIGURE_MANAGED_CREDENTIAL") return "Guardando contraseña…";
  if (operation.operationType === "REMOVE_MANAGED_CREDENTIAL") return "Eliminando contraseña…";
  return "Operación en curso…";
}
