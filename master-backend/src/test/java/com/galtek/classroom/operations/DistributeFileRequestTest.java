package com.galtek.classroom.operations;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.workspace.LogicalWorkspaceDestination;
import java.util.List;
import org.junit.jupiter.api.Test;

class DistributeFileRequestTest {

    @Test
    void distributionRequestModelsBatchTargetsAndOptionalOpenAfterDistribution() {
        var request = new DistributeFileRequest(
                "OP-DIST-1",
                "TEACHER-CONTENT-123",
                LogicalWorkspaceDestination.DOCUMENTS,
                ConflictPolicy.SKIP,
                true,
                List.of(target("STU-001"), target("STU-002")));

        assertThat(request.openAfterDistribution()).isTrue();
        assertThat(request.batch()).isTrue();
        assertThat(request.logicalDestination()).isEqualTo(LogicalWorkspaceDestination.DOCUMENTS);
    }

    private static OperationTarget target(String studentId) {
        return new OperationTarget(OperationTargetType.STUDENT, studentId, studentId);
    }
}
