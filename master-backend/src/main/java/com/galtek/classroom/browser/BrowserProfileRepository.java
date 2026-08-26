package com.galtek.classroom.browser;

import java.time.OffsetDateTime;
import java.util.Optional;

public interface BrowserProfileRepository {

    void create(BrowserProfile profile, OffsetDateTime nowUtc);

    Optional<BrowserProfile> findById(String browserProfileId);

    Optional<BrowserProfile> findByStudentId(String studentId);
}
