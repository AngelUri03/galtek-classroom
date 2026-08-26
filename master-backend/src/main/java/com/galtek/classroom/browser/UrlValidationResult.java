package com.galtek.classroom.browser;

import com.galtek.classroom.operations.ErrorCode;

public record UrlValidationResult(
        boolean valid,
        ErrorCode errorCode) {

    public static UrlValidationResult allowed() {
        return new UrlValidationResult(true, null);
    }

    public static UrlValidationResult invalid() {
        return new UrlValidationResult(false, ErrorCode.INVALID_URL);
    }
}
