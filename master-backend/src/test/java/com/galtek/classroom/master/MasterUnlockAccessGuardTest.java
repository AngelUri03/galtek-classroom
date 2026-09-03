package com.galtek.classroom.master;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.LocalAgentUnavailableException;
import com.galtek.classroom.localagent.MasterUnlockAuthorizationResponse;
import org.junit.jupiter.api.Test;

class MasterUnlockAccessGuardTest {

    @Test
    void requireUnlockAuthorized_WhenAgentAuthorizes_ReturnsAuthorization() {
        LocalAgentClient localAgentClient = mock(LocalAgentClient.class);
        MasterUnlockAuthorizationResponse authorization = new MasterUnlockAuthorizationResponse(
                "AUTHORIZED",
                true,
                true);
        when(localAgentClient.getMasterUnlockAuthorization()).thenReturn(authorization);
        MasterUnlockAccessGuard guard = new MasterUnlockAccessGuard(localAgentClient);

        MasterUnlockAuthorizationResponse result = guard.requireUnlockAuthorized();

        assertThat(result).isSameAs(authorization);
        verify(localAgentClient, never()).getMasterAuthorization();
    }

    @Test
    void requireUnlockAuthorized_WhenAgentDoesNotAuthorize_Throws() {
        LocalAgentClient localAgentClient = mock(LocalAgentClient.class);
        when(localAgentClient.getMasterUnlockAuthorization()).thenReturn(new MasterUnlockAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true));
        MasterUnlockAccessGuard guard = new MasterUnlockAccessGuard(localAgentClient);

        assertThatThrownBy(guard::requireUnlockAuthorized)
                .isInstanceOf(MasterAccessDeniedException.class)
                .satisfies(exception -> assertThat(((MasterAccessDeniedException) exception).status())
                        .isEqualTo("CURRENT_ACCOUNT_NOT_AUTHORIZED"));
        verify(localAgentClient, never()).getMasterAuthorization();
    }

    @Test
    void requireUnlockAuthorized_WhenLocalAgentIsUnavailable_FailsClosedWithoutFallback() {
        LocalAgentClient localAgentClient = mock(LocalAgentClient.class);
        when(localAgentClient.getMasterUnlockAuthorization()).thenThrow(new LocalAgentUnavailableException("down"));
        MasterUnlockAccessGuard guard = new MasterUnlockAccessGuard(localAgentClient);

        assertThatThrownBy(guard::requireUnlockAuthorized)
                .isInstanceOf(LocalAgentUnavailableException.class);
        verify(localAgentClient, never()).getMasterAuthorization();
    }

    @Test
    void requireUnlockAuthorized_DoesNotDependOnSqliteBindingFilesLicenseStateOrMasterAccessGuard() {
        assertThat(MasterUnlockAccessGuard.class.getDeclaredFields())
                .extracting(field -> field.getType().getName())
                .containsExactly(LocalAgentClient.class.getName());
    }
}
