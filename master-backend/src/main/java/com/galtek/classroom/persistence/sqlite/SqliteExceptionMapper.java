package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import org.springframework.dao.CannotAcquireLockException;
import org.springframework.dao.DataAccessException;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.dao.DuplicateKeyException;

public final class SqliteExceptionMapper {

    private SqliteExceptionMapper() {
    }

    public static MasterStorageException map(String message, RuntimeException exception) {
        if (exception instanceof MasterStorageException storageException) {
            return storageException;
        }

        if (exception instanceof DuplicateKeyException || exception instanceof DataIntegrityViolationException) {
            return new MasterStorageException(
                    ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION,
                    message,
                    exception);
        }

        if (exception instanceof CannotAcquireLockException) {
            return new MasterStorageException(ErrorCode.MASTER_DATABASE_BUSY, message, exception);
        }

        String technicalMessage = technicalMessage(exception).toLowerCase();
        if (technicalMessage.contains("database is locked") || technicalMessage.contains("busy")) {
            return new MasterStorageException(ErrorCode.MASTER_DATABASE_BUSY, message, exception);
        }
        if (technicalMessage.contains("sqlite_constraint")
                || technicalMessage.contains("constraint failed")
                || technicalMessage.contains("unique constraint failed")
                || technicalMessage.contains("foreign key constraint failed")
                || technicalMessage.contains("check constraint failed")) {
            return new MasterStorageException(
                    ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION,
                    message,
                    exception);
        }
        if (technicalMessage.contains("database or disk is full") || technicalMessage.contains("disk is full")) {
            return new MasterStorageException(ErrorCode.MASTER_STORAGE_FULL, message, exception);
        }
        if (technicalMessage.contains("sqlite_notadb")
                || technicalMessage.contains("not a database")
                || technicalMessage.contains("file is not a database")
                || technicalMessage.contains("database disk image is malformed")
                || technicalMessage.contains("invalid file format")
                || technicalMessage.contains("malformed")) {
            return new MasterStorageException(ErrorCode.MASTER_DATABASE_CORRUPT, message, exception);
        }

        return new MasterStorageException(ErrorCode.MASTER_DATABASE_UNAVAILABLE, message, exception);
    }

    private static String technicalMessage(Throwable exception) {
        StringBuilder messages = new StringBuilder();
        Throwable current = exception;
        while (current != null) {
            if (current.getMessage() != null) {
                messages.append(' ').append(current.getMessage());
            }
            if (current instanceof DataAccessException dataAccessException) {
                Throwable rootCause = dataAccessException.getRootCause();
                if (rootCause != null && rootCause != current && rootCause.getMessage() != null) {
                    messages.append(' ').append(rootCause.getMessage());
                }
            }
            current = current.getCause();
        }

        return messages.toString();
    }
}
