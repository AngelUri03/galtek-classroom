package com.galtek.classroom.master;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import org.junit.jupiter.api.Test;

class MasterAccessGuardTest {

    @Test
    void requireAuthorized_WhenAgentAuthorizes_ReturnsAuthorization() {
        LocalAgentClient localAgentClient = mock(LocalAgentClient.class);
        MasterAuthorizationResponse authorization = new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria");
        when(localAgentClient.getMasterAuthorization()).thenReturn(authorization);
        MasterAccessGuard guard = new MasterAccessGuard(new MasterAuthorizationService(localAgentClient));

        MasterAuthorizationResponse result = guard.requireAuthorized();

        assertThat(result).isSameAs(authorization);
    }

    @Test
    void requireAuthorized_WhenAgentDoesNotAuthorize_Throws() {
        LocalAgentClient localAgentClient = mock(LocalAgentClient.class);
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        MasterAccessGuard guard = new MasterAccessGuard(new MasterAuthorizationService(localAgentClient));

        assertThatThrownBy(guard::requireAuthorized)
                .isInstanceOf(MasterAccessDeniedException.class)
                .satisfies(exception -> assertThat(((MasterAccessDeniedException) exception).status())
                        .isEqualTo("CURRENT_ACCOUNT_NOT_AUTHORIZED"));
    }
}
