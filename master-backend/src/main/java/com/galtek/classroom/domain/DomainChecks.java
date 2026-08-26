package com.galtek.classroom.domain;

import java.util.Collection;
import java.util.List;
import java.util.Objects;
import java.util.Set;

public final class DomainChecks {

    private DomainChecks() {
    }

    public static String requireNonBlank(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(fieldName + " is required.");
        }

        return value;
    }

    public static <T> T requireNonNull(T value, String fieldName) {
        return Objects.requireNonNull(value, fieldName + " is required.");
    }

    public static <T> List<T> copyList(Collection<T> values, String fieldName) {
        requireNonNull(values, fieldName);

        return List.copyOf(values);
    }

    public static <T> Set<T> copySet(Collection<T> values, String fieldName) {
        requireNonNull(values, fieldName);

        return Set.copyOf(values);
    }
}
