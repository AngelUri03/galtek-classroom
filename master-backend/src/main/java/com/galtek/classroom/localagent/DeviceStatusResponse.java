package com.galtek.classroom.localagent;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Map;

public record DeviceStatusResponse(
        String product,
        String installationId,
        String hostname,
        String licenseStatus,
        boolean active,
        String licenseId,
        String organizationId,
        OffsetDateTime expiresAtUtc,
        OffsetDateTime lastValidatedAtUtc,
        List<String> roles,
        Map<String, Object> features) {
}
