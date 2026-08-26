package com.galtek.classroom.browser;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class BrowserProfileService {

    private final BrowserProfileRepository browserProfileRepository;
    private final MasterBrowserProfileRepository masterBrowserProfileRepository;
    private final Clock clock;

    public BrowserProfileService(
            BrowserProfileRepository browserProfileRepository,
            MasterBrowserProfileRepository masterBrowserProfileRepository,
            Clock clock) {
        this.browserProfileRepository = browserProfileRepository;
        this.masterBrowserProfileRepository = masterBrowserProfileRepository;
        this.clock = clock;
    }

    @Transactional
    public BrowserProfile createStudentProfile(BrowserProfile profile) {
        browserProfileRepository.create(profile, nowUtc());
        return profile;
    }

    @Transactional
    public MasterBrowserProfile createMasterProfile(
            MasterBrowserProfile profile,
            BrowserProfileStrategy strategy,
            String ownerWindowsSid,
            boolean defaultProfile) {
        masterBrowserProfileRepository.create(profile, strategy, ownerWindowsSid, defaultProfile, nowUtc());
        return profile;
    }

    public Optional<BrowserProfile> findStudentProfile(String browserProfileId) {
        return browserProfileRepository.findById(browserProfileId);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
