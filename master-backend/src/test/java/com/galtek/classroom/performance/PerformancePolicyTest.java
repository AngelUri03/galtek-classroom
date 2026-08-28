package com.galtek.classroom.performance;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.operations.OperationPriority;
import org.junit.jupiter.api.Test;

class PerformancePolicyTest {

    @Test
    void unknownClientUsesLegacyBudget() {
        var budget = ClientPerformanceBudget.forProfile(null);

        assertThat(budget.profile()).isEqualTo(DevicePerformanceProfile.LEGACY);
    }

    @Test
    void legacyLimitsHeavyConcurrencyToOneClientOperation() {
        var legacy = ClientPerformanceBudget.forProfile(DevicePerformanceProfile.LEGACY);
        var standard = ClientPerformanceBudget.forProfile(DevicePerformanceProfile.STANDARD);

        assertThat(legacy.maxHeavyConcurrentOperations()).isEqualTo(1);
        assertThat(standard.maxHeavyConcurrentOperations()).isGreaterThan(legacy.maxHeavyConcurrentOperations());
    }

    @Test
    void resourceWorkClassesReuseOperationPriorityForPrecedence() {
        assertThat(ResourceWorkClass.CONTROL_CRITICAL.priorityFloor())
                .isEqualTo(OperationPriority.CRITICAL);
        assertThat(ResourceWorkClass.CONTROL_CRITICAL.priorityFloor()
                .outranks(ResourceWorkClass.BACKGROUND.priorityFloor()))
                .isTrue();
    }

    @Test
    void loadSheddingDropsNonEssentialWorkBeforeCriticalControl() {
        var policy = new LoadSheddingPolicy();

        assertThat(policy.sheddingOrder())
                .containsExactly(
                        SheddableWork.PREFETCH,
                        SheddableWork.NON_ESSENTIAL_INVENTORY,
                        SheddableWork.THUMBNAILS,
                        SheddableWork.PREVIEW_QUALITY_OR_FPS,
                        SheddableWork.NON_URGENT_TRANSFER,
                        SheddableWork.BACKGROUND_JOB);
        assertThat(policy.canShed(ResourceWorkClass.VISUAL)).isTrue();
        assertThat(policy.canShed(ResourceWorkClass.BACKGROUND)).isTrue();
        assertThat(policy.protects(ResourceWorkClass.CONTROL_CRITICAL)).isTrue();
        assertThat(policy.canShed(ResourceWorkClass.CONTROL_CRITICAL)).isFalse();
    }

    @Test
    void idlePolicyPermitsOnlyTinyControlTraffic() {
        var budget = ClientPerformanceBudget.forProfile(DevicePerformanceProfile.LEGACY);

        assertThat(budget.permitsIdle(IdleClientActivity.HEARTBEAT)).isTrue();
        assertThat(budget.permitsIdle(IdleClientActivity.CAPTURE)).isFalse();
        assertThat(budget.permitsIdle(IdleClientActivity.PROCESS_SCANNING)).isFalse();
        assertThat(budget.permitsIdle(IdleClientActivity.FILESYSTEM_SCANNING)).isFalse();
        assertThat(budget.permitsIdle(IdleClientActivity.WMI_QUERY)).isFalse();
        assertThat(budget.permitsIdle(IdleClientActivity.PERIODIC_DISK_WRITE)).isFalse();
        assertThat(budget.permitsIdle(IdleClientActivity.HEALTHY_HEARTBEAT_LOG)).isFalse();
    }

    @Test
    void diagnosticSamplingIsOnDemandOnly() {
        var policy = PerformanceDiagnosticPolicy.onDemandOnly();

        assertThat(policy.samplingMode()).isEqualTo(DiagnosticSamplingMode.ON_DEMAND);
        assertThat(policy.continuousCollectionAllowed()).isFalse();
        assertThat(policy.persistedTelemetryAllowed()).isFalse();
        assertThat(policy.sentWithHeartbeatAllowed()).isFalse();
    }

    @Test
    void performanceProfilesDoNotGrantAuthorization() {
        assertThat(ClientPerformanceBudget.forProfile(DevicePerformanceProfile.STANDARD).profileGrantsAuthorization())
                .isFalse();
        assertThat(MasterPerformanceBudget.balanced().profileGrantsAuthorization()).isFalse();
    }

    @Test
    void masterBudgetStaysSmallAndDoesNotAllowHeavyInfrastructure() {
        var budget = MasterPerformanceBudget.balanced();

        assertThat(budget.profile()).isEqualTo(MasterPerformanceProfile.MASTER_BALANCED);
        assertThat(budget.initialJvmHeapBudgetMb()).isEqualTo(512);
        assertThat(budget.sqliteMaximumPoolSize()).isEqualTo(4);
        assertThat(budget.distributedInfrastructureAllowed()).isFalse();
        assertThat(budget.terminalServerRoleAllowed()).isFalse();
    }

    @Test
    void degradedPerformanceDoesNotMeanOffline() {
        assertThat(ResourcePressureState.DEGRADED.impliesOffline()).isFalse();
    }
}
