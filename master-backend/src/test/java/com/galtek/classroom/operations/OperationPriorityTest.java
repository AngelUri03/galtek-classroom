package com.galtek.classroom.operations;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class OperationPriorityTest {

    @Test
    void priorityOrderKeepsCriticalAheadOfHighNormalAndLow() {
        assertThat(OperationPriority.CRITICAL.outranks(OperationPriority.HIGH)).isTrue();
        assertThat(OperationPriority.HIGH.outranks(OperationPriority.NORMAL)).isTrue();
        assertThat(OperationPriority.NORMAL.outranks(OperationPriority.LOW)).isTrue();

        var priorities = new ArrayList<>(List.of(
                OperationPriority.NORMAL,
                OperationPriority.CRITICAL,
                OperationPriority.LOW,
                OperationPriority.HIGH));
        priorities.sort(OperationPriority.mostImportantFirst());

        assertThat(priorities)
                .containsExactly(
                        OperationPriority.CRITICAL,
                        OperationPriority.HIGH,
                        OperationPriority.NORMAL,
                        OperationPriority.LOW);
    }

    @Test
    void defaultPriorityKeepsRecoveryCommandsAheadOfLargeTransfers() {
        var recovery = OperationPriority.defaultFor(OperationType.UNLOCK_INPUT);
        var distribution = OperationPriority.defaultFor(OperationType.DISTRIBUTE_FILE);

        assertThat(recovery).isEqualTo(OperationPriority.CRITICAL);
        assertThat(distribution).isEqualTo(OperationPriority.NORMAL);
        assertThat(recovery.outranks(distribution)).isTrue();
    }
}
