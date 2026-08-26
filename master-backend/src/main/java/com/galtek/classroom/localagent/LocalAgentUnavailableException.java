package com.galtek.classroom.localagent;

public class LocalAgentUnavailableException extends RuntimeException {

    public LocalAgentUnavailableException(String message) {
        super(message);
    }

    public LocalAgentUnavailableException(String message, Throwable cause) {
        super(message, cause);
    }
}
