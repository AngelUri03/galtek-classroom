package com.galtek.classroom.browser;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record BrowserProfile(
        String browserProfileId,
        String studentId,
        BrowserType browserType,
        String displayName,
        BrowserProfileStrategy profileStrategy,
        String profileReference,
        BrowserProfileStatus status,
        BrowserProfilePortability portability) {

    public BrowserProfile {
        browserProfileId = requireNonBlank(browserProfileId, "browserProfileId");
        studentId = requireNonBlank(studentId, "studentId");
        requireNonNull(browserType, "browserType");
        displayName = requireNonBlank(displayName, "displayName");
        requireNonNull(profileStrategy, "profileStrategy");
        profileReference = requireNonBlank(profileReference, "profileReference");
        requireNonNull(status, "status");
        requireNonNull(portability, "portability");
    }

    public boolean usableWithoutSecretCopy() {
        return status == BrowserProfileStatus.READY
                && (portability == BrowserProfilePortability.PORTABLE
                || portability == BrowserProfilePortability.REAUTH_REQUIRED);
    }
}
