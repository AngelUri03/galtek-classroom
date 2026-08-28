package com.galtek.classroom.performance;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record MasterPerformanceBudget(
        MasterPerformanceProfile profile,
        int initialJvmHeapBudgetMb,
        int sqliteMaximumPoolSize,
        boolean distributedInfrastructureAllowed,
        boolean terminalServerRoleAllowed) {

    public MasterPerformanceBudget {
        requireNonNull(profile, "profile");
        requirePositive(initialJvmHeapBudgetMb, "initialJvmHeapBudgetMb");
        requirePositive(sqliteMaximumPoolSize, "sqliteMaximumPoolSize");
    }

    public static MasterPerformanceBudget balanced() {
        return new MasterPerformanceBudget(
                MasterPerformanceProfile.MASTER_BALANCED,
                512,
                4,
                false,
                false);
    }

    public boolean profileGrantsAuthorization() {
        return false;
    }

    private static void requirePositive(int value, String fieldName) {
        if (value <= 0) {
            throw new IllegalArgumentException(fieldName + " must be positive.");
        }
    }
}
