package com.galtek.classroom.windows;

import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;
import static org.mockito.Mockito.never;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.asyncDispatch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.request;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.galtek.classroom.api.ApiExceptionHandler;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.security.SensitiveActionAuthorizationService;
import com.galtek.classroom.operations.DeviceMutationHttpGuard;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.WebMvcTest;
import org.springframework.http.MediaType;
import org.springframework.http.HttpStatus;
import org.springframework.test.context.TestPropertySource;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;

@WebMvcTest({ManagedAccountAdminController.class, ApiExceptionHandler.class})
@TestPropertySource(properties = "galtek.classroom.master.storage.enabled=true")
class ManagedAccountAdminControllerTest {
    @Autowired MockMvc mockMvc;
    @MockitoBean ManagedAccountAdminService service;
    @MockitoBean DeviceMutationHttpGuard mutationGuard;

    @Test
    void revealIsPerAccountOctetStreamAndNeverCacheable() throws Exception {
        when(service.revealCredential("room-1", "device-1", "ADMIN", "fresh-token"))
                .thenReturn(new ManagedAccountAdminService.CredentialReveal(
                        "temporary-secret", "credential-1", "LOCAL_MASTER",
                        ManagedWindowsAccountType.ADMIN, "PC14\\ADMIN-14"));

        MvcResult started = mockMvc.perform(post("/api/classrooms/room-1/devices/device-1/managed-accounts/ADMIN/credential/reveal")
                        .header(SensitiveActionAuthorizationService.HEADER, "fresh-token"))
                .andExpect(status().isOk())
                .andExpect(request().asyncStarted())
                .andExpect(content().contentType(MediaType.APPLICATION_OCTET_STREAM))
                .andExpect(header().string("Cache-Control", org.hamcrest.Matchers.containsString("no-store")))
                .andExpect(header().string("Pragma", "no-cache"))
                .andReturn();

        mockMvc.perform(asyncDispatch(started))
                .andExpect(status().isOk())
                .andExpect(content().contentType(MediaType.APPLICATION_OCTET_STREAM))
                .andExpect(content().bytes("temporary-secret".getBytes(java.nio.charset.StandardCharsets.UTF_8)))
                .andExpect(header().string("Cache-Control", org.hamcrest.Matchers.containsString("no-store")))
                .andExpect(header().string("Pragma", "no-cache"));

        verify(service).revealCredential("room-1", "device-1", "ADMIN", "fresh-token");
        verify(service).recordCredentialRevealDelivered(
                org.mockito.ArgumentMatchers.eq("room-1"),
                org.mockito.ArgumentMatchers.eq("device-1"),
                org.mockito.ArgumentMatchers.any(ManagedAccountAdminService.CredentialReveal.class));
    }

    @Test
    void revealRequiresSensitiveAuthorizationHeader() throws Exception {
        mockMvc.perform(post("/api/classrooms/room-1/devices/device-1/managed-accounts/PRIMARY/credential/reveal"))
                .andExpect(status().isBadRequest());
    }

    @Test
    void missingRevealReturnsJsonErrorAndNeverRecordsDelivery() throws Exception {
        when(service.revealCredential("room-1", "device-1", "PRIMARY", "fresh-token"))
                .thenThrow(new ApiException(HttpStatus.NOT_FOUND, ErrorCode.CREDENTIAL_NOT_FOUND,
                        "Managed credential is not present in the Master vault."));

        mockMvc.perform(post("/api/classrooms/room-1/devices/device-1/managed-accounts/PRIMARY/credential/reveal")
                        .header(SensitiveActionAuthorizationService.HEADER, "fresh-token"))
                .andExpect(status().isNotFound())
                .andExpect(content().contentType(MediaType.APPLICATION_JSON));

        verify(service, never()).recordCredentialRevealDelivered(
                org.mockito.ArgumentMatchers.anyString(),
                org.mockito.ArgumentMatchers.anyString(),
                org.mockito.ArgumentMatchers.any(ManagedAccountAdminService.CredentialReveal.class));
    }
}
