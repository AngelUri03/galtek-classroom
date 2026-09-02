package com.galtek.classroom.browserpolicy;

import static org.assertj.core.api.Assertions.assertThat;

import java.lang.reflect.RecordComponent;
import java.time.OffsetDateTime;
import java.util.Arrays;
import java.util.List;
import org.junit.jupiter.api.Test;

class BrowserDownloadPolicyPrecedenceResolverTest {

    private final BrowserDownloadPolicyPrecedenceResolver resolver = new BrowserDownloadPolicyPrecedenceResolver();

    @Test
    void noPolicyReturnsImplicitNoSpecialRestrictions() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context("device-1", "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of());

        assertThat(resolution.implicit()).isTrue();
        assertThat(resolution.policy()).isNull();
        assertThat(resolution.restrictionMode())
                .isEqualTo(BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS);
    }

    @Test
    void deviceSpecificAccountWinsOverDeviceAny() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context("device-1", "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("device-any", BrowserPolicyScopeType.DEVICE, null, "device-1",
                                BrowserPolicyAccountScope.ANY, BrowserDownloadRestrictionMode.BLOCK_ALL),
                        policy("device-primary", BrowserPolicyScopeType.DEVICE, null, "device-1",
                                BrowserPolicyAccountScope.PRIMARY,
                                BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS)));

        assertThat(resolution.policy().policyId()).isEqualTo("device-primary");
        assertThat(resolution.restrictionMode())
                .isEqualTo(BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS);
    }

    @Test
    void deviceAnyWinsOverGroupSpecific() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context("device-1", "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("group-primary", BrowserPolicyScopeType.GROUP, "group-1", null,
                                BrowserPolicyAccountScope.PRIMARY,
                                BrowserDownloadRestrictionMode.BLOCK_MALICIOUS),
                        policy("device-any", BrowserPolicyScopeType.DEVICE, null, "device-1",
                                BrowserPolicyAccountScope.ANY,
                                BrowserDownloadRestrictionMode.BLOCK_POTENTIALLY_DANGEROUS)));

        assertThat(resolution.policy().policyId()).isEqualTo("device-any");
    }

    @Test
    void groupSpecificWinsOverGroupAny() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context(null, "group-1", BrowserPolicyAccountScope.SECONDARY),
                List.of(
                        policy("group-any", BrowserPolicyScopeType.GROUP, "group-1", null,
                                BrowserPolicyAccountScope.ANY, BrowserDownloadRestrictionMode.BLOCK_ALL),
                        policy("group-secondary", BrowserPolicyScopeType.GROUP, "group-1", null,
                                BrowserPolicyAccountScope.SECONDARY,
                                BrowserDownloadRestrictionMode.BLOCK_DANGEROUS)));

        assertThat(resolution.policy().policyId()).isEqualTo("group-secondary");
    }

    @Test
    void groupAnyWinsOverClassroomSpecific() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context(null, "group-1", BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("classroom-primary", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.PRIMARY,
                                BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS),
                        policy("group-any", BrowserPolicyScopeType.GROUP, "group-1", null,
                                BrowserPolicyAccountScope.ANY, BrowserDownloadRestrictionMode.BLOCK_ALL)));

        assertThat(resolution.policy().policyId()).isEqualTo("group-any");
    }

    @Test
    void classroomSpecificWinsOverClassroomAny() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context(null, null, BrowserPolicyAccountScope.PRIMARY),
                List.of(
                        policy("classroom-any", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.ANY, BrowserDownloadRestrictionMode.BLOCK_DANGEROUS),
                        policy("classroom-primary", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.PRIMARY,
                                BrowserDownloadRestrictionMode.BLOCK_MALICIOUS)));

        assertThat(resolution.policy().policyId()).isEqualTo("classroom-primary");
    }

    @Test
    void nullAccountTypeUsesOnlyAnyPolicies() {
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context("device-1", null, null),
                List.of(
                        policy("device-primary", BrowserPolicyScopeType.DEVICE, null, "device-1",
                                BrowserPolicyAccountScope.PRIMARY, BrowserDownloadRestrictionMode.BLOCK_ALL),
                        policy("classroom-any", BrowserPolicyScopeType.CLASSROOM, null, null,
                                BrowserPolicyAccountScope.ANY,
                                BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS)));

        assertThat(resolution.policy().policyId()).isEqualTo("classroom-any");
        assertThat(resolution.restrictionMode())
                .isEqualTo(BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS);
    }

    @Test
    void policyFromAnotherClassroomNeverApplies() {
        BrowserDownloadPolicy otherClassroom = new BrowserDownloadPolicy(
                "other-classroom",
                "classroom-2",
                "Other",
                BrowserDownloadRestrictionMode.BLOCK_ALL,
                BrowserPolicyScopeType.CLASSROOM,
                null,
                null,
                BrowserPolicyAccountScope.ANY,
                true,
                0,
                now(),
                now());
        BrowserDownloadPolicy otherClassroomDevice = new BrowserDownloadPolicy(
                "other-classroom-device",
                "classroom-2",
                "Other Device",
                BrowserDownloadRestrictionMode.BLOCK_ALL,
                BrowserPolicyScopeType.DEVICE,
                null,
                "device-1",
                BrowserPolicyAccountScope.PRIMARY,
                true,
                0,
                now(),
                now());

        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                context("device-1", null, BrowserPolicyAccountScope.PRIMARY),
                List.of(otherClassroom, otherClassroomDevice));

        assertThat(resolution.implicit()).isTrue();
    }

    @Test
    void allFiveRestrictionModesAreValid() {
        assertThat(BrowserDownloadRestrictionMode.values())
                .containsExactly(
                        BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS,
                        BrowserDownloadRestrictionMode.BLOCK_DANGEROUS,
                        BrowserDownloadRestrictionMode.BLOCK_POTENTIALLY_DANGEROUS,
                        BrowserDownloadRestrictionMode.BLOCK_ALL,
                        BrowserDownloadRestrictionMode.BLOCK_MALICIOUS);
    }

    @Test
    void policyDoesNotModelArbitraryExtensionOrMimeLists() {
        assertThat(Arrays.stream(BrowserDownloadPolicy.class.getRecordComponents())
                        .map(RecordComponent::getName))
                .doesNotContain(
                        "blockedExtensions",
                        "allowedExtensions",
                        "blockedMimeTypes",
                        "allowedMimeTypes");
    }

    private BrowserDownloadPolicyContext context(
            String deviceId,
            String groupId,
            BrowserPolicyAccountScope accountScope) {
        return new BrowserDownloadPolicyContext("classroom-1", deviceId, groupId, accountScope);
    }

    private BrowserDownloadPolicy policy(
            String policyId,
            BrowserPolicyScopeType scopeType,
            String groupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope,
            BrowserDownloadRestrictionMode restrictionMode) {
        return new BrowserDownloadPolicy(
                policyId,
                "classroom-1",
                policyId,
                restrictionMode,
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
