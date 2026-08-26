package com.galtek.classroom.master;

import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.LocalAgentUnavailableException;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
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
class MasterAuthorizationControllerTest {

    @Autowired
    private MockMvc mockMvc;

    @MockitoBean
    private LocalAgentClient localAgentClient;

    @Test
    void authorizationEndpointReturnsStateFromLocalAgent() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));

        mockMvc.perform(get("/api/master/authorization"))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.status").value("AUTHORIZED"))
                .andExpect(jsonPath("$.authorized").value(true))
                .andExpect(jsonPath("$.configured").value(true))
                .andExpect(jsonPath("$.boundAccountDisplayName").value("AULA\\MaestraPrimaria"))
                .andExpect(jsonPath("$.currentAccountDisplayName").value("AULA\\MaestraPrimaria"));
    }

    @Test
    void authorizationEndpointReturnsBusinessStateWithHttp200WhenLicenseIsMissing() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "MASTER_LICENSE_REQUIRED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));

        mockMvc.perform(get("/api/master/authorization"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("MASTER_LICENSE_REQUIRED"))
                .andExpect(jsonPath("$.authorized").value(false));
    }

    @Test
    void authorizationEndpointMapsUnavailableAgentTo503() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenThrow(new LocalAgentUnavailableException("down"));

        mockMvc.perform(get("/api/master/authorization"))
                .andExpect(status().isServiceUnavailable())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.code").value("LOCAL_AGENT_UNAVAILABLE"));
    }
}
