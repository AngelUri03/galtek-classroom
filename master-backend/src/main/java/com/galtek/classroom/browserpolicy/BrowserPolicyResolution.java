package com.galtek.classroom.browserpolicy;

public record BrowserPolicyResolution(
        BrowserAccessPolicy policy,
        BrowserPolicyMode mode,
        boolean implicit) {

    public static BrowserPolicyResolution implicitUnrestricted() {
        return new BrowserPolicyResolution(null, BrowserPolicyMode.UNRESTRICTED, true);
    }

    public static BrowserPolicyResolution effective(BrowserAccessPolicy policy) {
        return new BrowserPolicyResolution(policy, policy.mode(), false);
    }
}
