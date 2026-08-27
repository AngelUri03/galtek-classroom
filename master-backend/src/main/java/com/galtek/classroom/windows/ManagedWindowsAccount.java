package com.galtek.classroom.windows;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record ManagedWindowsAccount(
        String accountId,
        ManagedWindowsAccountType accountType,
        String accountReference,
        boolean configured,
        boolean credentialConfigured,
        ManagedWindowsAccountStatus status) {

    public ManagedWindowsAccount {
        accountId = requireNonBlank(accountId, "accountId");
        requireNonNull(accountType, "accountType");
        if (!accountId.equals(accountType.name())) {
            throw new IllegalArgumentException("accountId must match accountType.");
        }
        if (accountReference != null && accountReference.isBlank()) {
            throw new IllegalArgumentException("accountReference cannot be blank.");
        }
        requireNonNull(status, "status");
        if (status == ManagedWindowsAccountStatus.READY && (!configured || !credentialConfigured)) {
            throw new IllegalArgumentException("READY managed accounts must be configured with credentials.");
        }
    }

    public static ManagedWindowsAccount ready(
            ManagedWindowsAccountType accountType,
            String accountReference) {
        return new ManagedWindowsAccount(
                accountType.name(),
                accountType,
                requireNonBlank(accountReference, "accountReference"),
                true,
                true,
                ManagedWindowsAccountStatus.READY);
    }

    public static ManagedWindowsAccount notConfigured(ManagedWindowsAccountType accountType) {
        return new ManagedWindowsAccount(
                accountType.name(),
                accountType,
                null,
                false,
                false,
                ManagedWindowsAccountStatus.NOT_CONFIGURED);
    }

    public static ManagedWindowsAccount credentialMissing(
            ManagedWindowsAccountType accountType,
            String accountReference) {
        return new ManagedWindowsAccount(
                accountType.name(),
                accountType,
                requireNonBlank(accountReference, "accountReference"),
                true,
                false,
                ManagedWindowsAccountStatus.CREDENTIAL_NOT_CONFIGURED);
    }

    public boolean readyForManagedLogon() {
        return configured && credentialConfigured && status == ManagedWindowsAccountStatus.READY;
    }
}
