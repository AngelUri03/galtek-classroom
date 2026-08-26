package com.galtek.classroom.localagent;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.Map;
import java.util.UUID;
import java.util.function.Supplier;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

@Service
public class WindowsNamedPipeLocalAgentClient implements LocalAgentClient {

    private final ObjectMapper objectMapper;
    private final LocalIpcTransport transport;
    private final Supplier<String> requestIdSupplier;

    @Autowired
    public WindowsNamedPipeLocalAgentClient(
            ObjectMapper objectMapper,
            LocalIpcTransport transport) {
        this(objectMapper, transport, () -> UUID.randomUUID().toString());
    }

    WindowsNamedPipeLocalAgentClient(
            ObjectMapper objectMapper,
            LocalIpcTransport transport,
            Supplier<String> requestIdSupplier) {
        this.objectMapper = objectMapper;
        this.transport = transport;
        this.requestIdSupplier = requestIdSupplier;
    }

    @Override
    public DeviceStatusResponse getDeviceStatus() {
        return execute(LocalIpcProtocol.OPERATION_GET_DEVICE_STATUS, DeviceStatusResponse.class);
    }

    @Override
    public MachineCodeResponse getMachineCode() {
        return execute(LocalIpcProtocol.OPERATION_GET_MACHINE_CODE, MachineCodeResponse.class);
    }

    @Override
    public MasterAuthorizationResponse getMasterAuthorization() {
        return execute(
                LocalIpcProtocol.OPERATION_GET_MASTER_AUTHORIZATION,
                MasterAuthorizationResponse.class);
    }

    private <T> T execute(String operation, Class<T> payloadType) {
        String requestId = requestIdSupplier.get();
        LocalIpcRequest request = new LocalIpcRequest(
                LocalIpcProtocol.PROTOCOL_VERSION,
                requestId,
                operation,
                Map.of());

        try {
            String responseJson = transport.exchange(objectMapper.writeValueAsString(request));
            LocalIpcResponse response = objectMapper.readValue(responseJson, LocalIpcResponse.class);

            if (!requestId.equals(response.requestId())) {
                throw new LocalAgentProtocolException(
                        LocalIpcProtocol.ERROR_RESPONSE_MISMATCH,
                        "Local Agent IPC response requestId did not match the request.");
            }

            if (!response.success()) {
                throw new LocalAgentProtocolException(
                        response.errorCode(),
                        "Local Agent IPC operation failed.");
            }

            JsonNode payload = response.payload();

            if (payload == null || payload.isNull()) {
                throw new LocalAgentProtocolException(
                        LocalIpcProtocol.ERROR_MALFORMED_RESPONSE,
                        "Local Agent IPC response payload was missing.");
            }

            return objectMapper.treeToValue(payload, payloadType);
        } catch (JsonProcessingException exception) {
            throw new LocalAgentProtocolException(
                    LocalIpcProtocol.ERROR_MALFORMED_RESPONSE,
                    "Local Agent IPC JSON could not be processed.",
                    exception);
        }
    }
}
