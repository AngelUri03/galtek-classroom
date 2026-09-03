package com.galtek.classroom.credentialvault;

import com.galtek.classroom.operations.ErrorCode;

public class CredentialVaultException extends RuntimeException {

    private final ErrorCode errorCode;

    public CredentialVaultException(ErrorCode errorCode, String message) {
        super(message);
        this.errorCode = errorCode;
    }

    public CredentialVaultException(ErrorCode errorCode, String message, Throwable cause) {
        super(message, cause);
        this.errorCode = errorCode;
    }

    public ErrorCode errorCode() {
        return errorCode;
    }
}
