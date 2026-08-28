package com.galtek.classroom.student;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import org.junit.jupiter.api.Test;

class StudentAssignmentStrategyTest {

    @Test
    void supportedAssignmentStrategiesAreExplicit() {
        assertThat(List.of(StudentAssignmentStrategy.values()))
                .containsExactly(
                        StudentAssignmentStrategy.LIST_ORDER,
                        StudentAssignmentStrategy.RANDOM,
                        StudentAssignmentStrategy.PREVIOUS,
                        StudentAssignmentStrategy.MANUAL);
    }
}
