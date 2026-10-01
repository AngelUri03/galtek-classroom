package com.galtek.classroom.operations;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.operations.DeviceMutationCoordinator.Lease;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.function.Supplier;
import org.springframework.stereotype.Component;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;

/** Keeps controller boundaries small while every mutation follows one lease lifecycle. */
@Component
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class DeviceMutationHttpGuard {
    private final DeviceMutationCoordinator coordinator;

    public DeviceMutationHttpGuard(DeviceMutationCoordinator coordinator) {
        this.coordinator = coordinator;
    }

    public <T> T run(
            List<String> deviceIds,
            String operationType,
            String targetProfile,
            Supplier<T> action,
            Function<T, List<String>> targetStatuses) {
        if (deviceIds == null || deviceIds.isEmpty()) return action.get();
        Lease lease = coordinator.acquire(deviceIds, operationType, targetProfile);
        try {
            T response = action.get();
            List<String> statuses = targetStatuses.apply(response);
            if (statuses.stream().anyMatch(DeviceMutationHttpGuard::requiresReconciliation)) {
                coordinator.requireReconciliation(lease);
            } else {
                coordinator.complete(lease);
            }
            return response;
        } catch (ApiException exception) {
            coordinator.complete(lease);
            throw exception;
        } catch (RuntimeException exception) {
            coordinator.requireReconciliation(lease);
            throw exception;
        }
    }

    /**
     * SESSION mutations retain backend authority until an explicit session
     * read confirms the frozen target. Deterministic failures release their
     * lease because they did not produce an uncertain SESSION result.
     */
    public <T> T runSession(
            List<String> deviceIds,
            String operationType,
            String targetProfile,
            Supplier<T> action,
            Function<T, Map<String, String>> targetStatuses) {
        if (deviceIds == null || deviceIds.isEmpty()) return action.get();
        Lease lease = coordinator.acquire(deviceIds, operationType, targetProfile);
        try {
            T response = action.get();
            Map<String, String> statuses = targetStatuses.apply(response);
            List<String> reconciliation = new ArrayList<>();
            List<String> completed = new ArrayList<>();
            for (String deviceId : lease.deviceIds()) {
                String status = statuses == null ? null : statuses.get(deviceId);
                if (status == null || requiresSessionObservation(status)) {
                    reconciliation.add(deviceId);
                } else {
                    completed.add(deviceId);
                }
            }
            coordinator.requireReconciliation(new Lease(reconciliation));
            coordinator.complete(new Lease(completed));
            return response;
        } catch (ApiException exception) {
            coordinator.complete(lease);
            throw exception;
        } catch (RuntimeException exception) {
            coordinator.requireReconciliation(lease);
            throw exception;
        }
    }

    public void confirmStable(List<String> deviceIds, List<String> operationTypes) {
        coordinator.confirmStable(deviceIds, operationTypes);
    }

    public List<String> reconcileSessionState(Map<String, String> observedStates) {
        return coordinator.reconcileSessionState(observedStates);
    }

    public static List<String> targetDeviceIds(Map<String, Object> request) {
        if (request == null || !(request.get("targetDeviceIds") instanceof List<?> raw)) return List.of();
        List<String> result = new ArrayList<>();
        for (Object item : raw) {
            if (!(item instanceof String value) || value.isBlank()) return List.of();
            result.add(value.trim());
        }
        return List.copyOf(result);
    }

    private static boolean requiresReconciliation(String status) {
        return "PARTIAL".equals(status) || "UNKNOWN".equals(status) || "PENDING".equals(status);
    }

    private static boolean requiresSessionObservation(String status) {
        return "SUCCESS".equals(status)
                || "NO_CHANGE".equals(status)
                || requiresReconciliation(status);
    }
}
