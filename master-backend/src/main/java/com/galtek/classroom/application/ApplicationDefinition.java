package com.galtek.classroom.application;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record ApplicationDefinition(
        String applicationId,
        String displayName,
        ApplicationType type,
        ApplicationAvailability availability,
        LaunchPolicy launchPolicy) {

    public ApplicationDefinition {
        applicationId = requireNonBlank(applicationId, "applicationId");
        displayName = requireNonBlank(displayName, "displayName");
        requireNonNull(type, "type");
        requireNonNull(availability, "availability");
        requireNonNull(launchPolicy, "launchPolicy");
    }
}
