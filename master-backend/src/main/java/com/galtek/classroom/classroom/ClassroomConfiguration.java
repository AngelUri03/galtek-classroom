package com.galtek.classroom.classroom;

import static com.galtek.classroom.domain.DomainChecks.copySet;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.util.Set;

public record ClassroomConfiguration(
        Set<String> authorizedApplicationIds,
        String defaultBrowserProfileId,
        boolean workspaceRecoveryPlanned,
        boolean batchConfirmationsRequired) {

    public ClassroomConfiguration {
        authorizedApplicationIds = copySet(authorizedApplicationIds, "authorizedApplicationIds");
        if (defaultBrowserProfileId != null && defaultBrowserProfileId.isBlank()) {
            throw new IllegalArgumentException("defaultBrowserProfileId cannot be blank.");
        }
        requireNonNull(workspaceRecoveryPlanned, "workspaceRecoveryPlanned");
        requireNonNull(batchConfirmationsRequired, "batchConfirmationsRequired");
    }

    public static ClassroomConfiguration defaults() {
        return new ClassroomConfiguration(Set.of(), null, true, true);
    }
}
