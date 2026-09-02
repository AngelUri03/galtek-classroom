package com.galtek.classroom.browserpolicy;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.OffsetDateTime;
import java.util.List;
import org.junit.jupiter.api.Test;

class BrowserPolicyPrecedenceResolverTest {

    private final BrowserPolicyPrecedenceResolver resolver = new BrowserPolicyPrecedenceResolver();

    @Test
    void deviceSpecificAccountWinsOverDeviceAny() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context("device-1", "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("device-any", BrowserPolicyScopeType.DEVICE, null, "device-1", BrowserPolicyAccountScope.ANY),
                        policy("device-primary", BrowserPolicyScopeType.DEVICE, null, "device-1",
                                BrowserPolicyAccountScope.PRIMARY)));

        assertThat(resolution.policy().policyId()).isEqualTo("device-primary");
    }

    @Test
    void deviceAnyWinsOverGroupSpecific() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context("device-1", "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("group-primary", BrowserPolicyScopeType.GROUP, "group-1", null,
                                BrowserPolicyAccountScope.PRIMARY),
                        policy("device-any", BrowserPolicyScopeType.DEVICE, null, "device-1", BrowserPolicyAccountScope.ANY)));

        assertThat(resolution.policy().policyId()).isEqualTo("device-any");
    }

    @Test
    void groupSpecificWinsOverGroupAny() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context(null, "group-1", BrowserPolicyAccountScope.SECONDARY),
                List.of(
                        policy("group-any", BrowserPolicyScopeType.GROUP, "group-1", null, BrowserPolicyAccountScope.ANY),
                        policy("group-secondary", BrowserPolicyScopeType.GROUP, "group-1", null,
                                BrowserPolicyAccountScope.SECONDARY)));

        assertThat(resolution.policy().policyId()).isEqualTo("group-secondary");
    }

    @Test
    void groupAnyWinsOverClassroomSpecific() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context(null, "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("classroom-primary", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.PRIMARY),
                        policy("group-any", BrowserPolicyScopeType.GROUP, "group-1", null, BrowserPolicyAccountScope.ANY)));

        assertThat(resolution.policy().policyId()).isEqualTo("group-any");
    }

    @Test
    void classroomSpecificWinsOverClassroomAny() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context(null, null, BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("classroom-any", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.ANY),
                        policy("classroom-primary", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.PRIMARY)));

        assertThat(resolution.policy().policyId()).isEqualTo("classroom-primary");
    }

    @Test
    void noPolicyReturnsImplicitUnrestricted() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context("device-1", "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of());

        assertThat(resolution.implicit()).isTrue();
        assertThat(resolution.mode()).isEqualTo(BrowserPolicyMode.UNRESTRICTED);
    }

    @Test
    void nullAccountTypeUsesOnlyAnyPolicies() {
        BrowserPolicyResolution resolution = resolver.resolve(
                context("device-1", null, null),
                List.of(
                        policy("device-primary", BrowserPolicyScopeType.DEVICE, null, "device-1",
                                BrowserPolicyAccountScope.PRIMARY),
                        policy("classroom-any", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.ANY)));

        assertThat(resolution.policy().policyId()).isEqualTo("classroom-any");
    }

    @Test
    void policyFromAnotherClassroomNeverApplies() {
        BrowserAccessPolicy otherClassroom = new BrowserAccessPolicy(
                "other-classroom",
                "classroom-2",
                "Other",
                BrowserPolicyMode.ALLOWLIST,
                BrowserPolicyScopeType.CLASSROOM,
                null,
                null,
                BrowserPolicyAccountScope.ANY,
                true,
                0,
                now(),
                now());
        BrowserAccessPolicy otherClassroomDevice = new BrowserAccessPolicy(
                "other-classroom-device",
                "classroom-2",
                "Other Device",
                BrowserPolicyMode.ALLOWLIST,
                BrowserPolicyScopeType.DEVICE,
                null,
                "device-1",
                BrowserPolicyAccountScope.PRIMARY,
                true,
                0,
                now(),
                now());

        BrowserPolicyResolution resolution = resolver.resolve(
                context("device-1", null, BrowserPolicyAccountScope.PRIMARY),
                List.of(otherClassroom, otherClassroomDevice));

        assertThat(resolution.implicit()).isTrue();
    }

    private BrowserPolicyContext context(
            String deviceId,
            String groupId,
            BrowserPolicyAccountScope accountScope) {
        return new BrowserPolicyContext("classroom-1", deviceId, groupId, accountScope);
    }

    private BrowserAccessPolicy policy(
            String policyId,
            BrowserPolicyScopeType scopeType,
            String groupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope) {
        return new BrowserAccessPolicy(
                policyId,
                "classroom-1",
                policyId,
                BrowserPolicyMode.BLOCKLIST,
                scopeType,
                groupId,
                deviceId,
                accountScope,
                true,
                0,
                now(),
                now());
    }

    private OffsetDateTime now() {
        return OffsetDateTime.parse("2026-09-01T12:00:00Z");
    }
}
