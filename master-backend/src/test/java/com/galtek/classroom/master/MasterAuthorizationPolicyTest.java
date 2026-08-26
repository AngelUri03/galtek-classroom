package com.galtek.classroom.master;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.operations.ErrorCode;
import java.time.OffsetDateTime;
import java.util.Set;
import org.junit.jupiter.api.Test;

class MasterAuthorizationPolicyTest {

    private final MasterAuthorizationPolicy policy = new MasterAuthorizationPolicy();

    @Test
    void masterRoleAndMatchingSidAreAuthorized() {
        var decision = policy.evaluate(
                license(Set.of("CLIENT", "MASTER")),
                binding("S-1-5-21-1000"),
                identity("S-1-5-21-1000"));

        assertThat(decision.authorized()).isTrue();
        assertThat(decision.state()).isEqualTo(MasterAuthorizationState.AUTHORIZED);
        assertThat(decision.errorCode()).isNull();
    }

    @Test
    void masterRoleAndDifferentSidAreRejected() {
        var decision = policy.evaluate(
                license(Set.of("CLIENT", "MASTER")),
                binding("S-1-5-21-1000"),
                identity("S-1-5-21-2000"));

        assertThat(decision.authorized()).isFalse();
        assertThat(decision.state()).isEqualTo(MasterAuthorizationState.CURRENT_ACCOUNT_NOT_AUTHORIZED);
        assertThat(decision.errorCode()).isEqualTo(ErrorCode.MASTER_WINDOWS_ACCOUNT_NOT_AUTHORIZED);
    }

    @Test
    void clientOnlyLicenseIsRejectedForMaster() {
        var decision = policy.evaluate(
                license(Set.of("CLIENT")),
                binding("S-1-5-21-1000"),
                identity("S-1-5-21-1000"));

        assertThat(decision.authorized()).isFalse();
        assertThat(decision.state()).isEqualTo(MasterAuthorizationState.MASTER_LICENSE_REQUIRED);
        assertThat(decision.errorCode()).isEqualTo(ErrorCode.MASTER_NOT_LICENSED);
    }

    private static CommercialLicenseView license(Set<String> roles) {
        return new CommercialLicenseView("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", true, roles);
    }

    private static MasterWindowsBinding binding(String sid) {
        return new MasterWindowsBinding(
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                sid,
                "AULA\\MaestraPrimaria",
                OffsetDateTime.parse("2026-08-25T15:00:00Z"));
    }

    private static WindowsIdentity identity(String sid) {
        return new WindowsIdentity(sid, "AULA\\UsuarioActual");
    }
}
