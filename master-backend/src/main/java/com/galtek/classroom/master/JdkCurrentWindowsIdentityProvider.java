package com.galtek.classroom.master;

public final class JdkCurrentWindowsIdentityProvider implements CurrentWindowsIdentityProvider {

    @Override
    public WindowsIdentity currentIdentity() {
        var ntSystem = createNtSystem();
        var sid = invokeString(ntSystem, "getUserSID");
        if (sid == null || sid.isBlank()) {
            throw new IllegalStateException("Current Windows SID is unavailable in this runtime.");
        }

        var accountName = invokeString(ntSystem, "getName");
        var domain = invokeString(ntSystem, "getDomain");
        var displayName = accountName == null || accountName.isBlank()
                ? sid
                : domain == null || domain.isBlank()
                ? accountName
                : domain + "\\" + accountName;

        return new WindowsIdentity(sid, displayName);
    }

    private static Object createNtSystem() {
        try {
            var type = Class.forName("com.sun.security.auth.module.NTSystem");
            return type.getDeclaredConstructor().newInstance();
        } catch (ReflectiveOperationException exception) {
            throw new IllegalStateException("Windows identity provider is unavailable in this runtime.", exception);
        }
    }

    private static String invokeString(Object instance, String methodName) {
        try {
            return (String) instance.getClass().getMethod(methodName).invoke(instance);
        } catch (ReflectiveOperationException exception) {
            throw new IllegalStateException("Cannot read current Windows identity.", exception);
        }
    }
}
