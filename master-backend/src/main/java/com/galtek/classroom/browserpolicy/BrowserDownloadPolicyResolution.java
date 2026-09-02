package com.galtek.classroom.browserpolicy;

public record BrowserDownloadPolicyResolution(
        BrowserDownloadPolicy policy,
        BrowserDownloadRestrictionMode restrictionMode,
        boolean implicit) {

    public static BrowserDownloadPolicyResolution implicitNoSpecialRestrictions() {
        return new BrowserDownloadPolicyResolution(
                null,
                BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS,
                true);
    }

    public static BrowserDownloadPolicyResolution effective(BrowserDownloadPolicy policy) {
        return new BrowserDownloadPolicyResolution(policy, policy.restrictionMode(), false);
    }
}
