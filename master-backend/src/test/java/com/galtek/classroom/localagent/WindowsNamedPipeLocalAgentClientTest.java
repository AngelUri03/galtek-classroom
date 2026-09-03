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
    void getMasterAuthorizationSerializesRequestAndDeserializesAuthorizedResponse() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(
                REQUEST_ID,
                true,
                null,
                Map.of(
                        "status", "AUTHORIZED",
                        "authorized", true,
                        "configured", true,
                        "boundAccountDisplayName", "AULA\\MaestraPrimaria",
                        "currentAccountDisplayName", "AULA\\MaestraPrimaria")));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        MasterAuthorizationResponse response = client.getMasterAuthorization();
        JsonNode requestJson = objectMapper.readTree(transport.requests().getFirst());

        assertThat(requestJson.get("protocolVersion").asInt()).isEqualTo(LocalIpcProtocol.PROTOCOL_VERSION);
        assertThat(requestJson.get("requestId").asText()).isEqualTo(REQUEST_ID);
        assertThat(requestJson.get("operation").asText())
                .isEqualTo(LocalIpcProtocol.OPERATION_GET_MASTER_AUTHORIZATION);
        assertThat(response.status()).isEqualTo("AUTHORIZED");
        assertThat(response.authorized()).isTrue();
        assertThat(response.configured()).isTrue();
        assertThat(response.boundAccountDisplayName()).isEqualTo("AULA\\MaestraPrimaria");
    }

    @Test
    void getMasterAuthorizationDeserializesNotAuthorizedBusinessStates() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(
                REQUEST_ID,
                true,
                null,
                Map.of(
                        "status", "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                        "authorized", false,
                        "configured", true,
                        "boundAccountDisplayName", "AULA\\MaestraPrimaria",
                        "currentAccountDisplayName", "AULA\\Soporte")));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        MasterAuthorizationResponse response = client.getMasterAuthorization();

        assertThat(response.status()).isEqualTo("CURRENT_ACCOUNT_NOT_AUTHORIZED");
        assertThat(response.authorized()).isFalse();
        assertThat(response.currentAccountDisplayName()).isEqualTo("AULA\\Soporte");
    }

    @Test
    void getMasterAuthorizationDeserializesLicenseRequired() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(
                REQUEST_ID,
                true,
                null,
                Map.of(
                        "status", "MASTER_LICENSE_REQUIRED",
                        "authorized", false,
                        "configured", true,
                        "boundAccountDisplayName", "AULA\\MaestraPrimaria",
                        "currentAccountDisplayName", "AULA\\MaestraPrimaria")));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        MasterAuthorizationResponse response = client.getMasterAuthorization();

        assertThat(response.status()).isEqualTo("MASTER_LICENSE_REQUIRED");
        assertThat(response.authorized()).isFalse();
    }

    @Test
    void getMasterUnlockAuthorizationSerializesRequestAndDeserializesMinimalResponse() throws Exception {
        CapturingTransport transport = new CapturingTransport(responseJson(
                REQUEST_ID,
                true,
                null,
                Map.of(
                        "status", "AUTHORIZED",
                        "authorized", true,
                        "configured", true)));
        WindowsNamedPipeLocalAgentClient client = new WindowsNamedPipeLocalAgentClient(
                objectMapper,
                transport,
                () -> REQUEST_ID);

        MasterUnlockAuthorizationResponse response = client.getMasterUnlockAuthorization();
        JsonNode requestJson = objectMapper.readTree(transport.requests().getFirst());

        assertThat(requestJson.get("protocolVersion").asInt()).isEqualTo(LocalIpcProtocol.PROTOCOL_VERSION);
        assertThat(requestJson.get("requestId").asText()).isEqualTo(REQUEST_ID);
        assertThat(requestJson.get("operation").asText())
                .isEqualTo(LocalIpcProtocol.OPERATION_GET_MASTER_UNLOCK_AUTHORIZATION);
        assertThat(response.status()).isEqualTo("AUTHORIZED");
        assertThat(response.authorized()).isTrue();
        assertThat(response.configured()).isTrue();
        assertThat(response.toString()).doesNotContain("Sid", "License", "claims");
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
