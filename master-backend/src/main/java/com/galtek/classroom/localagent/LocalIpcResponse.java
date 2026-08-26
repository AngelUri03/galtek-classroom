package com.galtek.classroom.localagent;

import com.fasterxml.jackson.databind.JsonNode;

public record LocalIpcResponse(
        int protocolVersion,
        String requestId,
        boolean success,
        String errorCode,
        JsonNode payload) {
}
