export type DeviceOperationPhase = "IN_PROGRESS" | "RECONCILIATION_REQUIRED";
export type DeviceReconciliationStatus = "ACTIVE" | "TIMED_OUT";
export type DeviceOperationSnapshot = {
  deviceId: string;
  state: DeviceOperationPhase;
  operationType: string;
  targetProfile?: string | null;
  startedAtUtc: string;
  reconciliationStatus?: DeviceReconciliationStatus;
};

export type DeviceOperationRefreshTicket = ReadonlyMap<string, number>;

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

export function beginDeviceOperationRefresh(
  currentGenerations: ReadonlyMap<string, number>,
  deviceIds: Iterable<string>
) {
  const generations = new Map(currentGenerations);
  const ticket = new Map<string, number>();
  for (const deviceId of new Set(deviceIds)) {
    const generation = (generations.get(deviceId) ?? 0) + 1;
    generations.set(deviceId, generation);
    ticket.set(deviceId, generation);
  }
  return { generations, ticket };
}

export function invalidateDeviceOperationRefreshes(
  currentGenerations: ReadonlyMap<string, number>,
  deviceIds: Iterable<string>
) {
  const generations = new Map(currentGenerations);
  for (const deviceId of new Set(deviceIds)) {
    generations.set(deviceId, (generations.get(deviceId) ?? 0) + 1);
  }
  return generations;
}

export function commitDeviceOperationRefresh(
  current: ReadonlyMap<string, DeviceOperationSnapshot>,
  currentGenerations: ReadonlyMap<string, number>,
  ticket: DeviceOperationRefreshTicket,
  serverOperations: Iterable<DeviceOperationSnapshot>
) {
  const next = new Map(current);
  const server = new Map([...serverOperations].map((operation) => [operation.deviceId, operation]));
  ticket.forEach((generation, deviceId) => {
    if (currentGenerations.get(deviceId) !== generation) return;
    next.delete(deviceId);
    const operation = server.get(deviceId);
    if (operation) {
      const currentOperation = current.get(deviceId);
      const reconciliationStatus = currentOperation?.state === "RECONCILIATION_REQUIRED"
        && operation.state === "RECONCILIATION_REQUIRED"
        && currentOperation.operationType === operation.operationType
        && currentOperation.targetProfile === operation.targetProfile
        ? currentOperation.reconciliationStatus
        : undefined;
      next.set(deviceId, reconciliationStatus ? { ...operation, reconciliationStatus } : operation);
    }
  });
  return next;
}

export function deviceOperationLabel(operation: DeviceOperationSnapshot, sessionState?: string) {
  if (operation.state === "RECONCILIATION_REQUIRED") {
    return operation.reconciliationStatus === "ACTIVE"
      ? "Confirmando estado del equipo…"
      : "Estado por confirmar";
  }
  if (sessionState === "NO_SESSION" && operation.targetProfile === "ADMIN") return "Iniciando Administración…";
  if (sessionState === "NO_SESSION" && operation.targetProfile === "PRIMARY") return "Iniciando Primaria…";
  if (sessionState === "NO_SESSION" && operation.targetProfile === "SECONDARY") return "Iniciando Secundaria…";
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

export function sessionMutationControl(
  operation: DeviceOperationSnapshot | undefined,
  sessionState: string,
  operationType: "SWITCH_MANAGED_ACCOUNT" | "LOGOFF_WINDOWS_SESSION",
  targetProfile?: string
) {
  const processing = operation?.state === "IN_PROGRESS"
    && operation.operationType === operationType
    && (operationType === "LOGOFF_WINDOWS_SESSION" || operation.targetProfile === targetProfile);
  return {
    disabled: operation !== undefined,
    processing,
    processingLabel: processing ? deviceOperationLabel(operation, sessionState) : null
  };
}
