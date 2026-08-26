package com.galtek.classroom.persistence.sqlite;

import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;

public final class UtcTimestamps {

    private UtcTimestamps() {
    }

    public static String toText(OffsetDateTime value) {
        if (value == null) {
            return null;
        }

        return value.toInstant().toString();
    }

    public static OffsetDateTime fromText(String value) {
        if (value == null) {
            return null;
        }

        return OffsetDateTime.ofInstant(Instant.parse(value), ZoneOffset.UTC);
    }
}
