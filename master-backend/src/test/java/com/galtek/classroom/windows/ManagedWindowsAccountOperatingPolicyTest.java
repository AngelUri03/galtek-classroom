package com.galtek.classroom.windows;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class ManagedWindowsAccountOperatingPolicyTest {

    @Test
    void secondaryDefaultIsNormalWindowsNotRestrictedMode() {
        var policy = ManagedWindowsAccountOperatingPolicy.secondaryDefault();

        assertThat(policy.accountType()).isEqualTo(ManagedWindowsAccountType.SECONDARY);
        assertThat(policy.restrictedMode()).isFalse();
        assertThat(policy.blockInputOnClassStart()).isFalse();
        assertThat(policy.forceSessionSwitchOnAssignment()).isFalse();
    }

    @Test
    void primaryDefaultKeepsInputAvailableAtClassStart() {
        var policy = ManagedWindowsAccountOperatingPolicy.primaryDefault();

        assertThat(policy.accountType()).isEqualTo(ManagedWindowsAccountType.PRIMARY);
        assertThat(policy.restrictedMode()).isFalse();
        assertThat(policy.blockInputOnClassStart()).isFalse();
        assertThat(policy.forceSessionSwitchOnAssignment()).isFalse();
    }
}
