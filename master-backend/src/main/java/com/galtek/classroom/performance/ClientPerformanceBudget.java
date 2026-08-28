package com.galtek.classroom.performance;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record ClientPerformanceBudget(
        DevicePerformanceProfile profile,
        int maxHeavyConcurrentOperations,
        int preferredAgentServiceIdleMemoryMb,
        int preferredSessionAgentIdleMemoryMb,
        int preferredCombinedIdleMemoryMb,
        int combinedIdleMemoryReviewThresholdMb) {

    public ClientPerformanceBudget {
        requireNonNull(profile, "profile");
        requirePositive(maxHeavyConcurrentOperations, "maxHeavyConcurrentOperations");
        requirePositive(preferredAgentServiceIdleMemoryMb, "preferredAgentServiceIdleMemoryMb");
        requirePositive(preferredSessionAgentIdleMemoryMb, "preferredSessionAgentIdleMemoryMb");
        requirePositive(preferredCombinedIdleMemoryMb, "preferredCombinedIdleMemoryMb");
        requirePositive(combinedIdleMemoryReviewThresholdMb, "combinedIdleMemoryReviewThresholdMb");
    }

    public static ClientPerformanceBudget forProfile(DevicePerformanceProfile configuredProfile) {
        var profile = DevicePerformanceProfile.configuredOrDefault(configuredProfile);
        int heavyConcurrency = switch (profile) {
            case LEGACY -> 1;
            case STANDARD -> 2;
        };

        return new ClientPerformanceBudget(
                profile,
                heavyConcurrency,
                60,
                40,
                100,
                150);
    }

    public boolean permitsIdle(IdleClientActivity activity) {
        requireNonNull(activity, "activity");

        return switch (activity) {
            case CONTROL_CONNECTION, HEARTBEAT -> true;
            case CAPTURE, OVERLAY, UI, PROCESS_SCANNING, FILESYSTEM_SCANNING, WMI_QUERY, INVENTORY, PREFETCH,
                    PERIODIC_DISK_WRITE, HEALTHY_HEARTBEAT_LOG, DIAGNOSTIC_SAMPLE -> false;
        };
    }

    public boolean requiresReviewForCombinedIdleMemoryMb(int measuredCombinedIdleMemoryMb) {
        return measuredCombinedIdleMemoryMb > combinedIdleMemoryReviewThresholdMb;
    }

    public boolean profileGrantsAuthorization() {
        return false;
    }

    public boolean permitsPeriodicWmiProfileDetection() {
        return false;
    }

    private static void requirePositive(int value, String fieldName) {
        if (value <= 0) {
            throw new IllegalArgumentException(fieldName + " must be positive.");
        }
    }
}
