package com.galtek.classroom.persistence;

import com.galtek.classroom.operations.ErrorCode;

public class MasterStorageException extends RuntimeException {

    private final ErrorCode errorCode;

    public MasterStorageException(ErrorCode errorCode, String message) {
        super(message);
        this.errorCode = errorCode;
    }

    public MasterStorageException(ErrorCode errorCode, String message, Throwable cause) {
        super(message, cause);
        this.errorCode = errorCode;
    }

    public ErrorCode errorCode() {
        return errorCode;
    }
}
