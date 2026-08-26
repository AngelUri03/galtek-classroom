package com.galtek.classroom.master;

import static com.galtek.classroom.domain.DomainChecks.copySet;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

import java.util.Set;

public record CommercialLicenseView(
        String installationId,
        boolean active,
        Set<String> roles) {

    public static final String MASTER_ROLE = "MASTER";

    public CommercialLicenseView {
        installationId = requireNonBlank(installationId, "installationId");
        roles = copySet(roles, "roles");
    }

    public boolean allowsMaster() {
        return active && roles.contains(MASTER_ROLE);
    }
}
