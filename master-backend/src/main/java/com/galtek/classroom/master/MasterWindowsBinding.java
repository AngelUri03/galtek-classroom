package com.galtek.classroom.master;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.time.OffsetDateTime;

public record MasterWindowsBinding(
        String installationId,
        String windowsSid,
        String accountDisplayName,
        OffsetDateTime boundAtUtc) {

    public MasterWindowsBinding {
        installationId = requireNonBlank(installationId, "installationId");
        windowsSid = requireNonBlank(windowsSid, "windowsSid");
        accountDisplayName = requireNonBlank(accountDisplayName, "accountDisplayName");
        requireNonNull(boundAtUtc, "boundAtUtc");
    }
}
