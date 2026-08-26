package com.galtek.classroom.device;

import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.galtek.classroom.localagent.DeviceStatusResponse;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.LocalAgentUnavailableException;
import com.galtek.classroom.localagent.MachineCodeResponse;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest(properties = {
        "debug=false",
        "galtek.classroom.master.storage.enabled=false",
        "spring.autoconfigure.exclude=org.springframework.boot.autoconfigure.jdbc.DataSourceAutoConfiguration",
        "logging.level.root=INFO",
        "logging.level.org.springframework=INFO"
})
@AutoConfigureMockMvc
class DeviceControllerTest {

    @Autowired
    private MockMvc mockMvc;

    @MockitoBean
    private LocalAgentClient localAgentClient;

    @Test
    void statusEndpointReturnsDeviceStatusFromLocalAgent() throws Exception {
        when(localAgentClient.getDeviceStatus()).thenReturn(new DeviceStatusResponse(
                "GALTEK_CLASSROOM",
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                "PC-AULA-07",
                "ACTIVATION_REQUIRED",
                false,
                null,
                null,
                null,
                OffsetDateTime.parse("2026-08-25T15:00:00Z"),
                List.of(),
                Map.of()));

        mockMvc.perform(get("/api/device/status"))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.product").value("GALTEK_CLASSROOM"))
                .andExpect(jsonPath("$.installationId").value("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))
                .andExpect(jsonPath("$.licenseStatus").value("ACTIVATION_REQUIRED"))
                .andExpect(jsonPath("$.active").value(false));
    }

    @Test
    void machineCodeEndpointReturnsMachineCodeFromLocalAgent() throws Exception {
        when(localAgentClient.getMachineCode()).thenReturn(new MachineCodeResponse("machine-code-1"));

        mockMvc.perform(get("/api/device/machine-code"))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.machineCode").value("machine-code-1"));
    }

    @Test
    void statusEndpointMapsUnavailableAgentTo503() throws Exception {
        when(localAgentClient.getDeviceStatus()).thenThrow(new LocalAgentUnavailableException("down"));

        mockMvc.perform(get("/api/device/status"))
                .andExpect(status().isServiceUnavailable())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.code").value("LOCAL_AGENT_UNAVAILABLE"));
    }

    @Test
    void systemHealthDoesNotDependOnLocalAgent() throws Exception {
        mockMvc.perform(get("/api/system/health"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("UP"));

        verifyNoInteractions(localAgentClient);
    }
}
