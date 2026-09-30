package com.galtek.classroom.security;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.credentialvault.CredentialType;
import com.galtek.classroom.credentialvault.CredentialVaultException;
import com.galtek.classroom.credentialvault.CredentialVaultService;
import com.galtek.classroom.credentialvault.CredentialVaultService.FreshCredentialSnapshot;
import com.galtek.classroom.operations.ErrorCode;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.List;
import org.junit.jupiter.api.Test;

class SensitiveActionAuthorizationServiceTest {

    private static final String ACTOR = "AULA\\Maestra";
    private static final String CLASSROOM = "classroom-1";
    private final CredentialVaultService vault = mock(CredentialVaultService.class);
    private final MutableClock clock = new MutableClock(Instant.parse("2026-09-28T18:00:00Z"));
    private final SensitiveActionAuthorizationService service =
            new SensitiveActionAuthorizationService(vault, clock, new java.security.SecureRandom());

    @Test
    void correctMasterPasswordIssuesOneTimeExactlyScopedAdminAuthorization() {
        var issued = service.authorize("master", SensitiveActionScope.ADMIN_SESSION, ACTOR,
                CLASSROOM, List.of("device-2", "device-1"), "SWITCH_MANAGED_ACCOUNT_ADMIN");

        service.consumeAdminSession(issued.token(), ACTOR, CLASSROOM, List.of("device-1", "device-2"));

        verify(vault).verifyMasterPassword("master");
        assertThatThrownBy(() -> service.consumeAdminSession(
                issued.token(), ACTOR, CLASSROOM, List.of("device-1", "device-2")))
                .isInstanceOfSatisfying(ApiException.class,
                        exception -> assertThat(exception.code()).isEqualTo(ErrorCode.SENSITIVE_AUTHORIZATION_USED.name()));
    }

    @Test
    void expiredAuthorizationIsRejected() {
        var issued = adminAuthorization(List.of("device-1"));
        clock.advanceSeconds(61);
        assertCode(() -> service.consumeAdminSession(issued.token(), ACTOR, CLASSROOM, List.of("device-1")),
                ErrorCode.SENSITIVE_AUTHORIZATION_EXPIRED);
    }

    @Test
    void scopeActorClassroomDeviceAndTargetListMustMatchExactly() {
        var issued = adminAuthorization(List.of("device-1", "device-2"));
        assertCode(() -> service.requireCredentialReveal(issued.token(), ACTOR, CLASSROOM, "device-1"),
                ErrorCode.SENSITIVE_AUTHORIZATION_INVALID);
        assertCode(() -> service.consumeAdminSession(issued.token(), "OTHER", CLASSROOM, List.of("device-1", "device-2")),
                ErrorCode.SENSITIVE_AUTHORIZATION_INVALID);
        assertCode(() -> service.consumeAdminSession(issued.token(), ACTOR, "other-classroom", List.of("device-1", "device-2")),
                ErrorCode.SENSITIVE_AUTHORIZATION_INVALID);
        assertCode(() -> service.consumeAdminSession(issued.token(), ACTOR, CLASSROOM, List.of("device-1")),
                ErrorCode.SENSITIVE_AUTHORIZATION_INVALID);
        assertCode(() -> service.consumeAdminSession(issued.token(), ACTOR, CLASSROOM, List.of("device-1", "device-3")),
                ErrorCode.SENSITIVE_AUTHORIZATION_INVALID);
    }

    @Test
    void wrongMasterPasswordAndRateLimitNeverIssueAuthorization() {
        doThrow(new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_UNLOCK_FAILED, "wrong"))
                .when(vault).verifyMasterPassword("wrong");
        for (int attempt = 0; attempt < 5; attempt++) {
            assertThatThrownBy(() -> service.authorize("wrong", SensitiveActionScope.ADMIN_SESSION,
                    ACTOR, CLASSROOM, List.of("device-1"), "SWITCH_MANAGED_ACCOUNT_ADMIN"))
                    .isInstanceOf(CredentialVaultException.class);
        }
        assertCode(() -> service.authorize("wrong", SensitiveActionScope.ADMIN_SESSION,
                ACTOR, CLASSROOM, List.of("device-1"), "SWITCH_MANAGED_ACCOUNT_ADMIN"),
                ErrorCode.SENSITIVE_AUTHORIZATION_RATE_LIMITED);
    }

    @Test
    void revealAuthorizationIsReusableOnlyForItsDeviceAndSnapshot() {
        when(vault.freshCredentialSnapshot("master")).thenReturn(List.of(new FreshCredentialSnapshot(
                "cred-1", CredentialType.WINDOWS_ACCOUNT,
                "Managed Windows account device-1 / PC\\Primary", "PC\\Primary", "secret")));
        var issued = service.authorize("master", SensitiveActionScope.CREDENTIAL_REVEAL,
                ACTOR, CLASSROOM, List.of("device-1"), "REVEAL_MANAGED_CREDENTIAL");

        var first = service.revealManagedCredential(issued.token(), ACTOR, CLASSROOM, "device-1",
                "Managed Windows account device-1 / ", "PC\\Primary");
        var second = service.revealManagedCredential(issued.token(), ACTOR, CLASSROOM, "device-1",
                "Managed Windows account device-1 / ", "PC\\Primary");
        assertThat(first.password()).isEqualTo("secret");
        assertThat(second.password()).isEqualTo("secret");
        verify(vault, org.mockito.Mockito.never()).auditFreshReveal("cred-1");
        service.auditDeliveredCredentialReveal(first.credentialId());
        service.auditDeliveredCredentialReveal(second.credentialId());
        verify(vault, org.mockito.Mockito.times(2)).auditFreshReveal("cred-1");
        assertCode(() -> service.requireCredentialReveal(issued.token(), ACTOR, CLASSROOM, "device-2"),
                ErrorCode.SENSITIVE_AUTHORIZATION_INVALID);
    }

    @Test
    void revealSurvivesHostnameRenameWithinTheSameStableDevice() {
        when(vault.freshCredentialSnapshot("master")).thenReturn(List.of(new FreshCredentialSnapshot(
                "cred-rename", CredentialType.WINDOWS_ACCOUNT,
                "Managed Windows account device-1 / ICH11\\ADMIN-14", "ICH11\\ADMIN-14", "secret")));
        var issued = service.authorize("master", SensitiveActionScope.CREDENTIAL_REVEAL,
                ACTOR, CLASSROOM, List.of("device-1"), "REVEAL_MANAGED_CREDENTIAL");

        var revealed = service.revealManagedCredential(issued.token(), ACTOR, CLASSROOM, "device-1",
                "Managed Windows account device-1 / ", "PC14\\ADMIN-14");

        assertThat(revealed.credentialId()).isEqualTo("cred-rename");
        assertThat(revealed.password()).isEqualTo("secret");
        verify(vault, org.mockito.Mockito.never()).auditFreshReveal("cred-rename");
    }

    private SensitiveActionAuthorizationService.IssuedAuthorization adminAuthorization(List<String> targets) {
        return service.authorize("master", SensitiveActionScope.ADMIN_SESSION, ACTOR,
                CLASSROOM, targets, "SWITCH_MANAGED_ACCOUNT_ADMIN");
    }

    private void assertCode(org.assertj.core.api.ThrowableAssert.ThrowingCallable callable, ErrorCode expected) {
        assertThatThrownBy(callable).isInstanceOfSatisfying(ApiException.class,
                exception -> assertThat(exception.code()).isEqualTo(expected.name()));
    }

    private static final class MutableClock extends Clock {
        private Instant instant;
        private MutableClock(Instant instant) { this.instant = instant; }
        void advanceSeconds(long seconds) { instant = instant.plusSeconds(seconds); }
        @Override public ZoneId getZone() { return ZoneOffset.UTC; }
        @Override public Clock withZone(ZoneId zone) { return this; }
        @Override public Instant instant() { return instant; }
    }
}
