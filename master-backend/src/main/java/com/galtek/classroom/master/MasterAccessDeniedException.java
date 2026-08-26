package com.galtek.classroom.master;

public class MasterAccessDeniedException extends RuntimeException {

    private final String status;

    public MasterAccessDeniedException(String status) {
        super("Master access is not authorized: " + status);
        this.status = status;
    }

    public String status() {
        return status;
    }
}
