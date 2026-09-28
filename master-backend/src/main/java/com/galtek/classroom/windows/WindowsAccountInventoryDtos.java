package com.galtek.classroom.windows;

import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.network.v1.WindowsAccountInventoryEntry;
import com.galtek.classroom.network.v1.WindowsAccountInventoryResult;
import java.util.List;

public final class WindowsAccountInventoryDtos {

    private WindowsAccountInventoryDtos() {
    }

    public record WindowsAccountInventoryResponse(
            String deviceId,
            List<WindowsAccountResponse> accounts) {

        static WindowsAccountInventoryResponse from(
                String deviceId,
                WindowsAccountInventoryResult result) {
            return new WindowsAccountInventoryResponse(
                    deviceId,
                    result.getAccountsList().stream()
                            .map(WindowsAccountResponse::from)
                            .toList());
        }
    }

    public record WindowsAccountResponse(
            String accountName,
            String displayName,
            boolean enabled,
            boolean administrator,
            boolean builtIn,
            String managedRole) {

        static WindowsAccountResponse from(WindowsAccountInventoryEntry account) {
            return new WindowsAccountResponse(
                    account.getAccountName(),
                    account.getDisplayName(),
                    account.getEnabled(),
                    account.getAdministrator(),
                    account.getBuiltIn(),
                    WindowsAccountInventoryDtos.managedRole(account.getManagedRole()));
        }
    }

    private static String managedRole(ManagedWindowsAccountId role) {
        return switch (role) {
            case MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY -> "PRIMARY";
            case MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY -> "SECONDARY";
            case MANAGED_WINDOWS_ACCOUNT_ID_ADMIN -> "ADMIN";
            case MANAGED_WINDOWS_ACCOUNT_ID_UNSPECIFIED, UNRECOGNIZED -> "NONE";
        };
    }
}
