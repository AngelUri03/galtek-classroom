package com.galtek.classroom.workspace;

import static com.galtek.classroom.domain.DomainChecks.copySet;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.util.Set;

public record StudentWorkspace(
        String workspaceId,
        String studentId,
        WorkspaceStatus status,
        Set<LogicalWorkspaceDestination> allowedDestinations,
        String browserProfileId,
        WorkspaceRecoveryPolicy recoveryPolicy) {

    public StudentWorkspace {
        workspaceId = requireNonBlank(workspaceId, "workspaceId");
        studentId = requireNonBlank(studentId, "studentId");
        requireNonNull(status, "status");
        allowedDestinations = copySet(allowedDestinations, "allowedDestinations");
        if (allowedDestinations.isEmpty()) {
            throw new IllegalArgumentException("allowedDestinations cannot be empty.");
        }
        browserProfileId = requireNonBlank(browserProfileId, "browserProfileId");
        requireNonNull(recoveryPolicy, "recoveryPolicy");
    }

    public boolean readyForFileOperation() {
        return status.ready();
    }
}
