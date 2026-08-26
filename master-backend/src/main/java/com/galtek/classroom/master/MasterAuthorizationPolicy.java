package com.galtek.classroom.master;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;

public final class MasterAuthorizationPolicy {

    public MasterAuthorizationDecision evaluate(
            CommercialLicenseView license,
            MasterWindowsBinding binding,
            WindowsIdentity currentIdentity) {
        requireNonNull(currentIdentity, "currentIdentity");

        if (binding == null) {
            return new MasterAuthorizationDecision(
                    MasterAuthorizationState.NOT_CONFIGURED,
                    ErrorCode.MASTER_WINDOWS_ACCOUNT_NOT_AUTHORIZED,
                    currentIdentity.windowsSid(),
                    null);
        }

        if (license == null || !license.allowsMaster()) {
            return new MasterAuthorizationDecision(
                    MasterAuthorizationState.MASTER_LICENSE_REQUIRED,
                    ErrorCode.MASTER_NOT_LICENSED,
                    currentIdentity.windowsSid(),
                    binding.windowsSid());
        }

        if (!binding.installationId().equals(license.installationId())) {
            return new MasterAuthorizationDecision(
                    MasterAuthorizationState.INSTALLATION_MISMATCH,
                    ErrorCode.MASTER_INSTALLATION_MISMATCH,
                    currentIdentity.windowsSid(),
                    binding.windowsSid());
        }

        if (!binding.windowsSid().equals(currentIdentity.windowsSid())) {
            return new MasterAuthorizationDecision(
                    MasterAuthorizationState.CURRENT_ACCOUNT_NOT_AUTHORIZED,
                    ErrorCode.MASTER_WINDOWS_ACCOUNT_NOT_AUTHORIZED,
                    currentIdentity.windowsSid(),
                    binding.windowsSid());
        }

        return new MasterAuthorizationDecision(
                MasterAuthorizationState.AUTHORIZED,
                null,
                currentIdentity.windowsSid(),
                binding.windowsSid());
    }
}
