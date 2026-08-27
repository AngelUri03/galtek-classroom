package com.galtek.classroom.windows;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.device.Device;
import java.util.List;

public record ManagedAccountSwitchTarget(
        Device device,
        WindowsSessionState sessionState,
        List<ManagedWindowsAccount> accounts) {

    public ManagedAccountSwitchTarget {
        requireNonNull(device, "device");
        requireNonNull(sessionState, "sessionState");
        accounts = copyList(accounts, "accounts");
    }

    public ManagedWindowsAccount account(ManagedWindowsAccountType accountType) {
        requireNonNull(accountType, "accountType");

        return accounts.stream()
                .filter(account -> account.accountType() == accountType)
                .findFirst()
                .orElse(null);
    }
}
