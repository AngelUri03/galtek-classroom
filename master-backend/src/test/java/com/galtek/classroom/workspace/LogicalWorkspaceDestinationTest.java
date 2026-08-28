package com.galtek.classroom.workspace;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class LogicalWorkspaceDestinationTest {

    @Test
    void removableStorageIsLogicalAuthorizedDestinationNotArbitraryPath() {
        assertThat(LogicalWorkspaceDestination.REMOVABLE_STORAGE.name()).isEqualTo("REMOVABLE_STORAGE");
        assertThat(LogicalWorkspaceDestination.REMOVABLE_STORAGE.studentScoped()).isFalse();
        assertThat(LogicalWorkspaceDestination.REMOVABLE_STORAGE.authorizedStudentDocumentDestination()).isTrue();
        assertThat(LogicalWorkspaceDestination.REMOVABLE_STORAGE.requiresRemovableStorageAuthorization()).isTrue();
    }
}
