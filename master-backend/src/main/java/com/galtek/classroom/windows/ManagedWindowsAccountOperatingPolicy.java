package com.galtek.classroom.windows;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record ManagedWindowsAccountOperatingPolicy(
        ManagedWindowsAccountType accountType,
        boolean restrictedMode,
        boolean blockInputOnClassStart,
        boolean forceSessionSwitchOnAssignment) {

    public ManagedWindowsAccountOperatingPolicy {
        requireNonNull(accountType, "accountType");
    }

    public static ManagedWindowsAccountOperatingPolicy normalWindows(ManagedWindowsAccountType accountType) {
        return new ManagedWindowsAccountOperatingPolicy(accountType, false, false, false);
    }

    public static ManagedWindowsAccountOperatingPolicy primaryDefault() {
        return normalWindows(ManagedWindowsAccountType.PRIMARY);
    }

    public static ManagedWindowsAccountOperatingPolicy secondaryDefault() {
        return normalWindows(ManagedWindowsAccountType.SECONDARY);
    }
}
