package com.galtek.classroom.master;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

public record WindowsIdentity(
        String windowsSid,
        String accountDisplayName) {

    public WindowsIdentity {
        windowsSid = requireNonBlank(windowsSid, "windowsSid");
        accountDisplayName = requireNonBlank(accountDisplayName, "accountDisplayName");
    }
}
