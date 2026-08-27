package com.galtek.classroom.api;

public record ApiErrorResponse(
        String code,
        String message,
        String detail) {

    public static ApiErrorResponse of(String code, String message) {
        return new ApiErrorResponse(code, message, null);
    }
}
