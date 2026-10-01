import type { WindowsSessionState } from "../api/quickActionsApi";

export type SessionSnapshot = {
  deviceId: string;
  state: WindowsSessionState;
  generation: number;
};

export type SessionRefreshTicket = ReadonlyMap<string, number>;

export type SessionObservation = {
  deviceId: string;
  state: WindowsSessionState;
};

export type SessionObservationCommit = {
  snapshots: Map<string, SessionSnapshot>;
  observed: Map<string, WindowsSessionState>;
  published: Map<string, WindowsSessionState>;
  rejectedDeviceIds: Set<string>;
};

export function beginSessionRefresh(
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

export function invalidateSessionRefreshes(
  currentGenerations: ReadonlyMap<string, number>,
  deviceIds: Iterable<string>
) {
  const generations = new Map(currentGenerations);
  for (const deviceId of new Set(deviceIds)) {
    generations.set(deviceId, (generations.get(deviceId) ?? 0) + 1);
  }
  return generations;
}

export function commitSessionObservations(
  currentSnapshots: ReadonlyMap<string, SessionSnapshot>,
  currentGenerations: ReadonlyMap<string, number>,
  ticket: SessionRefreshTicket,
  observations: Iterable<SessionObservation>,
  expectedStates: ReadonlyMap<string, WindowsSessionState> = new Map()
): SessionObservationCommit {
  const snapshots = new Map(currentSnapshots);
  const observed = new Map<string, WindowsSessionState>();
  const published = new Map<string, WindowsSessionState>();
  const rejectedDeviceIds = new Set<string>();

  for (const observation of observations) {
    const requestGeneration = ticket.get(observation.deviceId);
    if (requestGeneration === undefined || currentGenerations.get(observation.deviceId) !== requestGeneration) {
      continue;
    }
    observed.set(observation.deviceId, observation.state);
    const expectedState = expectedStates.get(observation.deviceId);
    if (expectedState !== undefined && observation.state !== expectedState) {
      rejectedDeviceIds.add(observation.deviceId);
      continue;
    }
    snapshots.set(observation.deviceId, {
      deviceId: observation.deviceId,
      state: observation.state,
      generation: requestGeneration
    });
    published.set(observation.deviceId, observation.state);
  }
  expectedStates.forEach((_expectedState, deviceId) => {
    const requestGeneration = ticket.get(deviceId);
    if (requestGeneration !== undefined
      && currentGenerations.get(deviceId) === requestGeneration
      && !published.has(deviceId)) {
      rejectedDeviceIds.add(deviceId);
    }
  });

  return { snapshots, observed, published, rejectedDeviceIds };
}
