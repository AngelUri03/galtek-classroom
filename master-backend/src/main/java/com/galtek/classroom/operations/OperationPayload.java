package com.galtek.classroom.operations;

public record OperationPayload(
        int schemaVersion,
        String json) {

    public OperationPayload {
        if (schemaVersion < 1) {
            throw new IllegalArgumentException("schemaVersion must be at least 1.");
        }
        if (json != null && json.isBlank()) {
            throw new IllegalArgumentException("json cannot be blank.");
        }
    }

    public static OperationPayload none() {
        return new OperationPayload(1, null);
    }
}
