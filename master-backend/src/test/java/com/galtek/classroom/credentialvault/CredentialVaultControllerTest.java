package com.galtek.classroom.credentialvault;

import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import java.time.Instant;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.WebMvcTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

@WebMvcTest(CredentialVaultController.class)
class CredentialVaultControllerTest {

    @Autowired
    private MockMvc mockMvc;

    @MockitoBean
    private MasterAccessGuard masterAccessGuard;

    @MockitoBean
    private CredentialVaultService credentialVaultService;

    @Test
    void statusInitializeUnlockAndLockUseMasterAccessGuard() throws Exception {
        when(masterAccessGuard.requireAuthorized()).thenReturn(authorized());
        when(credentialVaultService.status())
                .thenReturn(new CredentialVaultService.CredentialVaultStatus(false, true))
                .thenReturn(new CredentialVaultService.CredentialVaultStatus(true, true))
                .thenReturn(new CredentialVaultService.CredentialVaultStatus(true, true));
        when(credentialVaultService.unlock(anyString())).thenReturn(new CredentialVaultSession(
                "vault-token",
                Instant.parse("2026-09-14T12:00:00Z"),
                Instant.parse("2026-09-14T12:05:00Z")));

        mockMvc.perform(get("/api/credential-vault/status"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.initialized").value(false))
                .andExpect(jsonPath("$.locked").value(true));
        mockMvc.perform(post("/api/credential-vault/initialize")
                        .contentType(MediaType.APPLICATION_OCTET_STREAM)
                        .content("master-password".getBytes(java.nio.charset.StandardCharsets.UTF_8)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.initialized").value(true));
        mockMvc.perform(post("/api/credential-vault/unlock")
                        .contentType(MediaType.APPLICATION_OCTET_STREAM)
                        .content("master-password".getBytes(java.nio.charset.StandardCharsets.UTF_8)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.vaultSessionToken").value("vault-token"))
                .andExpect(jsonPath("$.locked").value(false));
        mockMvc.perform(post("/api/credential-vault/lock")
                        .header(CredentialVaultController.VAULT_SESSION_HEADER, "vault-token"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.locked").value(true));

        verify(masterAccessGuard, org.mockito.Mockito.times(4)).requireAuthorized();
        verify(credentialVaultService).initialize("master-password");
        verify(credentialVaultService).unlock("master-password");
        verify(credentialVaultService).lock("vault-token");
    }

    @Test
    void initializeRejectsJsonPasswordBodies() throws Exception {
        when(masterAccessGuard.requireAuthorized()).thenReturn(authorized());

        mockMvc.perform(post("/api/credential-vault/initialize")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"password\":\"secret\"}"))
                .andExpect(status().isUnsupportedMediaType());
    }

    private static MasterAuthorizationResponse authorized() {
        return new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\Maestra",
                "AULA\\Maestra");
    }
}
