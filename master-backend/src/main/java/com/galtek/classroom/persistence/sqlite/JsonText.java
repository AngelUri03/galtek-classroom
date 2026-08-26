package com.galtek.classroom.persistence.sqlite;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import java.util.Collection;
import java.util.Comparator;
import java.util.EnumSet;
import java.util.List;
import java.util.Set;

public final class JsonText {

    private static final ObjectMapper OBJECT_MAPPER = new ObjectMapper();
    private static final TypeReference<List<String>> STRING_LIST = new TypeReference<>() {
    };

    private JsonText() {
    }

    public static <E extends Enum<E>> String enumNames(Collection<E> values) {
        List<String> names = values.stream()
                .map(Enum::name)
                .sorted(Comparator.naturalOrder())
                .toList();

        try {
            return OBJECT_MAPPER.writeValueAsString(names);
        } catch (JsonProcessingException exception) {
            throw new MasterStorageException(
                    ErrorCode.MASTER_DATABASE_UNAVAILABLE,
                    "Enum list could not be serialized.",
                    exception);
        }
    }

    public static <E extends Enum<E>> Set<E> enumSet(String json, Class<E> enumType) {
        try {
            List<String> names = OBJECT_MAPPER.readValue(json, STRING_LIST);
            EnumSet<E> values = EnumSet.noneOf(enumType);
            for (String name : names) {
                values.add(Enum.valueOf(enumType, name));
            }
            return values;
        } catch (JsonProcessingException | IllegalArgumentException exception) {
            throw new MasterStorageException(
                    ErrorCode.MASTER_DATABASE_CORRUPT,
                    "Stored enum list could not be read.",
                    exception);
        }
    }
}
