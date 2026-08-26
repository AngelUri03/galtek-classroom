package com.galtek.classroom.localagent;

public class LocalAgentProtocolException extends RuntimeException {

    private final String code;

    public LocalAgentProtocolException(String code, String message) {
        super(message);
        this.code = code;
    }

    public LocalAgentProtocolException(String code, String message, Throwable cause) {
        super(message, cause);
        this.code = code;
    }

    public String code() {
        return code;
    }
}
