package com.galtek.classroom.persistence;

import com.galtek.classroom.operations.ErrorCode;

public class PersistenceVersionConflictException extends MasterStorageException {

    public PersistenceVersionConflictException(String message) {
        super(ErrorCode.CONCURRENT_MODIFICATION, message);
    }
}
