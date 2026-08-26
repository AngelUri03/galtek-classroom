package com.galtek.classroom.browser;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface MasterBrowserProfileRepository {

    void create(MasterBrowserProfile profile, BrowserProfileStrategy strategy, String ownerWindowsSid,
            boolean defaultProfile, OffsetDateTime nowUtc);

    Optional<MasterBrowserProfile> findById(String browserProfileId);

    List<MasterBrowserProfile> findActiveByBrowserType(BrowserType browserType);
}
