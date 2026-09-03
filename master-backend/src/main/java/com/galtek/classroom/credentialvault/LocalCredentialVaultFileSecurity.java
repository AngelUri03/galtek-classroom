package com.galtek.classroom.credentialvault;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.FileSystems;
import java.nio.file.Path;
import java.nio.file.attribute.AclEntry;
import java.nio.file.attribute.AclEntryPermission;
import java.nio.file.attribute.AclEntryType;
import java.nio.file.attribute.AclFileAttributeView;
import java.nio.file.attribute.PosixFilePermission;
import java.nio.file.attribute.UserPrincipal;
import java.nio.file.attribute.UserPrincipalLookupService;
import java.util.ArrayList;
import java.util.EnumSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class LocalCredentialVaultFileSecurity implements CredentialVaultFileSecurity {

    @Override
    public void protect(Path path) {
        try {
            var posix = Files.getFileAttributeView(path, java.nio.file.attribute.PosixFileAttributeView.class);
            if (posix != null) {
                Files.setPosixFilePermissions(path, Set.of(
                        PosixFilePermission.OWNER_READ,
                        PosixFilePermission.OWNER_WRITE));
                return;
            }

            AclFileAttributeView acl = Files.getFileAttributeView(path, AclFileAttributeView.class);
            if (acl != null) {
                UserPrincipalLookupService lookupService = FileSystems.getDefault().getUserPrincipalLookupService();
                UserPrincipal owner = Files.getOwner(path);
                Map<String, UserPrincipal> principals = new LinkedHashMap<>();
                addIfResolved(principals, "SYSTEM", () -> lookupService.lookupPrincipalByName("SYSTEM"));
                addIfResolved(principals, "Administrators",
                        () -> lookupService.lookupPrincipalByName("BUILTIN\\Administrators"));
                principals.put(owner.getName(), owner);
                ArrayList<AclEntry> entries = new ArrayList<>();
                for (UserPrincipal principal : principals.values()) {
                    entries.add(AclEntry.newBuilder()
                            .setType(AclEntryType.ALLOW)
                            .setPrincipal(principal)
                            .setPermissions(EnumSet.allOf(AclEntryPermission.class))
                            .build());
                }
                acl.setAcl(List.copyOf(entries));
            }
        } catch (IOException | UnsupportedOperationException ignored) {
            // ACL is defense in depth; vault confidentiality is enforced by the master password and AES-GCM.
        }
    }

    private static void addIfResolved(
            Map<String, UserPrincipal> principals,
            String key,
            PrincipalResolver resolver) {
        try {
            UserPrincipal principal = resolver.resolve();
            principals.putIfAbsent(key, principal);
        } catch (IOException | UnsupportedOperationException ignored) {
            // Principal names can vary by Windows localization/domain. Owner-only fallback remains available.
        }
    }

    @FunctionalInterface
    private interface PrincipalResolver {
        UserPrincipal resolve() throws IOException;
    }
}
