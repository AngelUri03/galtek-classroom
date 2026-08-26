package com.galtek.classroom.device;

import com.galtek.classroom.localagent.LocalAgentProtocolException;
import com.galtek.classroom.localagent.LocalAgentUnavailableException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

@RestControllerAdvice
public class DeviceExceptionHandler {

    @ExceptionHandler(LocalAgentUnavailableException.class)
    public ResponseEntity<DeviceErrorResponse> localAgentUnavailable() {
        return ResponseEntity
                .status(HttpStatus.SERVICE_UNAVAILABLE)
                .body(new DeviceErrorResponse("LOCAL_AGENT_UNAVAILABLE"));
    }

    @ExceptionHandler(LocalAgentProtocolException.class)
    public ResponseEntity<DeviceErrorResponse> localAgentProtocolError(
            LocalAgentProtocolException exception) {
        return ResponseEntity
                .status(HttpStatus.BAD_GATEWAY)
                .body(new DeviceErrorResponse(exception.code()));
    }
}
