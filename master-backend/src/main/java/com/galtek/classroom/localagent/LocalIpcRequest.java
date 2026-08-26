package com.galtek.classroom.localagent;

import java.util.Map;

public record LocalIpcRequest(
        int protocolVersion,
        String requestId,
        String operation,
        Map<String, Object> payload) {
}
