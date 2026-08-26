package com.galtek.classroom.localagent;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static java.util.Map.entry;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.json.JsonMapper;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class WindowsNamedPipeLocalAgentClientTest {

    private static final String REQUEST_ID = "11111111-1111-1111-1111-111111111111";

    private final ObjectMapper objectMapper = JsonMapper.builder()
            .findAndAddModules()
            .build();

    @Test
    void getDeviceStatusSerializesRequestAndDeserializesResponse() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(REQUEST_ID, true, null, Map.ofEntries(
                entry("product", "GALTEK_CLASSROOM"),
                entry("installationId", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                entry("hostname", "PC-AULA-07"),
                entry("licenseStatus", "ACTIVE"),
                entry("active", true),
                entry("licenseId", "license-1"),
                entry("organizationId", "ORG-1"),
                entry("expiresAtUtc", "2027-08-25T15:00:00Z"),
                entry("lastValidatedAtUtc", "2026-08-25T15:00:00Z"),
                entry("roles", List.of("CLIENT", "MASTER")),
                entry("features", Map.of("screenMonitoring", true, "maxManagedClients", 30)))));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        DeviceStatusResponse status = client.getDeviceStatus();
        JsonNode requestJson = objectMapper.readTree(transport.requests().getFirst());

        assertThat(requestJson.get("protocolVersion").asInt()).isEqualTo(LocalIpcProtocol.PROTOCOL_VERSION);
        assertThat(requestJson.get("requestId").asText()).isEqualTo(REQUEST_ID);
        assertThat(requestJson.get("operation").asText()).isEqualTo(LocalIpcProtocol.OPERATION_GET_DEVICE_STATUS);
        assertThat(status.licenseStatus()).isEqualTo("ACTIVE");
        assertThat(status.roles()).containsExactly("CLIENT", "MASTER");
        assertThat(status.features()).containsEntry("screenMonitoring", true);
    }

    @Test
    void getMachineCodeDeserializesResponse() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(
                REQUEST_ID,
                true,
                null,
                Map.of("machineCode", "machine-code-1")));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        MachineCodeResponse response = client.getMachineCode();

        assertThat(response.machineCode()).isEqualTo("machine-code-1");
    }

    @Test
    void responseRequestIdMismatchIsRejected() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(
                "22222222-2222-2222-2222-222222222222",
                true,
                null,
                Map.of("machineCode", "machine-code-1")));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        assertThatThrownBy(client::getMachineCode)
                .isInstanceOf(LocalAgentProtocolException.class)
                .satisfies(exception -> assertThat(((LocalAgentProtocolException) exception).code())
                        .isEqualTo(LocalIpcProtocol.ERROR_RESPONSE_MISMATCH));
    }

    private String responseJson(
            String requestId,
            boolean success,
            String errorCode,
            Object payload) throws Exception {
        return objectMapper.writeValueAsString(new LocalIpcResponse(
                LocalIpcProtocol.PROTOCOL_VERSION,
                requestId,
                success,
                errorCode,
                objectMapper.valueToTree(payload)));
    }

    private record CapturingTransport(String responseJson, List<String> requests) implements LocalIpcTransport {

        CapturingTransport(String responseJson) {
            this(responseJson, new ArrayList<>());
        }

        @Override
        public String exchange(String requestJson) {
            requests.add(requestJson);
            return responseJson;
        }
    }
}
