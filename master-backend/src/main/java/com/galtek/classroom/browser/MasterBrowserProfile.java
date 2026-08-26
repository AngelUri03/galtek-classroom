package com.galtek.classroom.browser;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record MasterBrowserProfile(
        String browserProfileId,
        BrowserType browserType,
        String displayName,
        String profileReference,
        BrowserProfileStatus status) {

    public MasterBrowserProfile {
        browserProfileId = requireNonBlank(browserProfileId, "browserProfileId");
        requireNonNull(browserType, "browserType");
        displayName = requireNonBlank(displayName, "displayName");
        profileReference = requireNonBlank(profileReference, "profileReference");
        requireNonNull(status, "status");
    }
}
