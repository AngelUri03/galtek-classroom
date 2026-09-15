package com.galtek.classroom.api;

import com.galtek.classroom.localagent.LocalAgentProtocolException;
import com.galtek.classroom.localagent.LocalAgentUnavailableException;
import com.galtek.classroom.master.MasterAccessDeniedException;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.PersistenceVersionConflictException;
import com.galtek.classroom.credentialvault.CredentialVaultException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

@RestControllerAdvice
public class ApiExceptionHandler {

    @ExceptionHandler(ApiException.class)
    public ResponseEntity<ApiErrorResponse> api(ApiException exception) {
        return ResponseEntity
                .status(exception.status())
                .body(ApiErrorResponse.of(exception.code(), exception.getMessage()));
    }

    @ExceptionHandler(LocalAgentUnavailableException.class)
    public ResponseEntity<ApiErrorResponse> localAgentUnavailable() {
        return ResponseEntity
                .status(HttpStatus.SERVICE_UNAVAILABLE)
                .body(ApiErrorResponse.of("LOCAL_AGENT_UNAVAILABLE", "Local Agent Service is unavailable."));
    }

    @ExceptionHandler(LocalAgentProtocolException.class)
    public ResponseEntity<ApiErrorResponse> localAgentProtocolError(
            LocalAgentProtocolException exception) {
        return ResponseEntity
                .status(HttpStatus.BAD_GATEWAY)
                .body(ApiErrorResponse.of(exception.code(), "Local Agent Service returned an invalid response."));
    }

    @ExceptionHandler(MasterAccessDeniedException.class)
    public ResponseEntity<ApiErrorResponse> masterAccessDenied(MasterAccessDeniedException exception) {
        return ResponseEntity
                .status(HttpStatus.FORBIDDEN)
                .body(new ApiErrorResponse(
                        exception.status(),
                        "Master account is not authorized.",
                        exception.status()));
    }

    @ExceptionHandler(PersistenceVersionConflictException.class)
    public ResponseEntity<ApiErrorResponse> optimisticConflict(PersistenceVersionConflictException exception) {
        return storage(HttpStatus.CONFLICT, exception);
    }

    @ExceptionHandler(CredentialVaultException.class)
    public ResponseEntity<ApiErrorResponse> credentialVault(CredentialVaultException exception) {
        return ResponseEntity
                .status(statusForCredentialVault(exception.errorCode()))
                .body(ApiErrorResponse.of(exception.errorCode().name(), exception.getMessage()));
    }

    @ExceptionHandler(MasterStorageException.class)
    public ResponseEntity<ApiErrorResponse> storage(MasterStorageException exception) {
        return storage(statusForStorage(exception.errorCode()), exception);
    }

    @ExceptionHandler(IllegalArgumentException.class)
    public ResponseEntity<ApiErrorResponse> illegalArgument(IllegalArgumentException exception) {
        return ResponseEntity
                .status(HttpStatus.BAD_REQUEST)
                .body(ApiErrorResponse.of(ErrorCode.INVALID_REQUEST.name(), exception.getMessage()));
    }

    private ResponseEntity<ApiErrorResponse> storage(HttpStatus status, MasterStorageException exception) {
        return ResponseEntity
                .status(status)
                .body(ApiErrorResponse.of(exception.errorCode().name(), messageForStorage(exception.errorCode())));
    }

    private HttpStatus statusForStorage(ErrorCode errorCode) {
        return switch (errorCode) {
            case CONCURRENT_MODIFICATION, PERSISTENCE_CONSTRAINT_VIOLATION -> HttpStatus.CONFLICT;
            case MASTER_DATABASE_UNAVAILABLE, MASTER_DATABASE_CORRUPT, MASTER_DATABASE_MIGRATION_FAILED,
                    MASTER_DATABASE_BUSY, MASTER_STORAGE_FULL -> HttpStatus.SERVICE_UNAVAILABLE;
            default -> HttpStatus.INTERNAL_SERVER_ERROR;
        };
    }

    private String messageForStorage(ErrorCode errorCode) {
        return switch (errorCode) {
            case CONCURRENT_MODIFICATION -> "The record was modified concurrently.";
            case PERSISTENCE_CONSTRAINT_VIOLATION -> "The requested change violates stored data constraints.";
            case MASTER_DATABASE_BUSY -> "Master storage is busy.";
            case MASTER_DATABASE_CORRUPT -> "Master storage is corrupt.";
            case MASTER_DATABASE_MIGRATION_FAILED -> "Master storage migration failed.";
            case MASTER_STORAGE_FULL -> "Master storage is full.";
            default -> "Master storage is unavailable.";
        };
    }

    private HttpStatus statusForCredentialVault(ErrorCode errorCode) {
        return switch (errorCode) {
            case CREDENTIAL_NOT_FOUND -> HttpStatus.NOT_FOUND;
            case CREDENTIAL_VAULT_ALREADY_INITIALIZED, CREDENTIAL_VAULT_NOT_INITIALIZED,
                    CREDENTIAL_VAULT_INVALID -> HttpStatus.CONFLICT;
            case CREDENTIAL_VAULT_LOCKED, CREDENTIAL_VAULT_UNLOCK_FAILED -> HttpStatus.FORBIDDEN;
            case INVALID_REQUEST, CREDENTIAL_NOT_PROVISIONABLE -> HttpStatus.BAD_REQUEST;
            case MASTER_STORAGE_FULL -> HttpStatus.SERVICE_UNAVAILABLE;
            default -> HttpStatus.BAD_REQUEST;
        };
    }
}
