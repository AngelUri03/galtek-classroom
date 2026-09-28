package com.galtek.classroom.windows;

import com.galtek.classroom.network.v1.ManagedAccountCredentialStatus;
import com.galtek.classroom.network.v1.ManagedAccountStatus;
import com.galtek.classroom.network.v1.ManagedAccountStatusResult;
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.operations.ErrorCode;
import java.util.List;

public final class ManagedAccountAdminDtos {

    private ManagedAccountAdminDtos() {
    }

    public record ManagedAccountStatusResponse(
            List<ManagedAccountSlotResponse> accounts) {

        static ManagedAccountStatusResponse from(ManagedAccountStatusResult status) {
            return new ManagedAccountStatusResponse(status.getAccountsList().stream()
                    .map(ManagedAccountSlotResponse::from)
                    .toList());
        }
    }

    public record ManagedAccountCredentialProvisionResponse(
            String accountId,
            String provisioningStatus,
            String operationId,
            String errorCode,
            String message,
            ManagedAccountSlotResponse account) {
    }

    public record ManagedAccountBindingRequest(String windowsAccountName) {
    }

    public record ManagedAccountMutationResponse(
            String accountId,
            String status,
            String operationId,
            String errorCode,
            String message,
            ManagedAccountSlotResponse account) {
    }

    public record ManagedAccountSlotResponse(
            String accountId,
            boolean configured,
            boolean credentialConfigured,
            String credentialStatus,
            String windowsAccountName) {

        static ManagedAccountSlotResponse from(ManagedAccountStatus account) {
            return new ManagedAccountSlotResponse(
                    ManagedAccountAdminDtos.accountId(account.getAccountId()),
                    account.getConfigured(),
                    account.getCredentialConfigured(),
                    ManagedAccountAdminDtos.credentialStatus(account.getCredentialStatus()),
                    emptyToNull(account.getWindowsAccountName()));
        }
    }

    static String accountId(ManagedWindowsAccountId accountId) {
        return switch (accountId) {
            case MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY -> ManagedWindowsAccountType.PRIMARY.name();
            case MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY -> ManagedWindowsAccountType.SECONDARY.name();
            case MANAGED_WINDOWS_ACCOUNT_ID_ADMIN -> ManagedWindowsAccountType.ADMIN.name();
            case MANAGED_WINDOWS_ACCOUNT_ID_UNSPECIFIED, UNRECOGNIZED -> null;
        };
    }

    static String credentialStatus(ManagedAccountCredentialStatus status) {
        return switch (status) {
            case MANAGED_ACCOUNT_CREDENTIAL_STATUS_NOT_CONFIGURED -> "NOT_CONFIGURED";
            case MANAGED_ACCOUNT_CREDENTIAL_STATUS_CREDENTIAL_NOT_CONFIGURED -> "CREDENTIAL_NOT_CONFIGURED";
            case MANAGED_ACCOUNT_CREDENTIAL_STATUS_ACCOUNT_NOT_FOUND -> "ACCOUNT_NOT_FOUND";
            case MANAGED_ACCOUNT_CREDENTIAL_STATUS_READY -> "READY";
            case MANAGED_ACCOUNT_CREDENTIAL_STATUS_UNSPECIFIED, UNRECOGNIZED -> "CREDENTIAL_NOT_CONFIGURED";
        };
    }

    static ManagedAccountSlotResponse requireAccount(
            ManagedAccountStatusResult status,
            ManagedWindowsAccountType accountId) {
        return status.getAccountsList().stream()
                .filter(account -> accountId.name().equals(accountId(account.getAccountId())))
                .findFirst()
                .map(ManagedAccountSlotResponse::from)
                .orElseThrow(() -> new IllegalStateException("Managed account status result did not contain "
                        + accountId.name() + "."));
    }

    static ErrorCode statusError(ManagedAccountSlotResponse account) {
        if (!account.configured()) {
            return ErrorCode.ACCOUNT_NOT_CONFIGURED;
        }
        if ("ACCOUNT_NOT_FOUND".equals(account.credentialStatus())) {
            return ErrorCode.ACCOUNT_NOT_FOUND;
        }
        if (!account.credentialConfigured()) {
            return ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED;
        }
        return null;
    }

    private static String emptyToNull(String value) {
        return value == null || value.isBlank() ? null : value;
    }
}
