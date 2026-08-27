package com.galtek.classroom.network;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.AclEntry;
import java.nio.file.attribute.AclEntryPermission;
import java.nio.file.attribute.AclEntryType;
import java.nio.file.attribute.AclFileAttributeView;
import java.nio.file.attribute.PosixFilePermission;
import java.nio.file.attribute.UserPrincipal;
import java.util.EnumSet;
import java.util.List;
import java.util.Set;

final class LocalFileSecurity {

    private LocalFileSecurity() {
    }

    static void restrictOwnerOnly(Path path) {
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
                UserPrincipal owner = Files.getOwner(path);
                AclEntry entry = AclEntry.newBuilder()
                        .setType(AclEntryType.ALLOW)
                        .setPrincipal(owner)
                        .setPermissions(EnumSet.allOf(AclEntryPermission.class))
                        .build();
                acl.setAcl(List.of(entry));
            }
        } catch (IOException | UnsupportedOperationException ignored) {
            // File ACL hardening is best-effort here; cryptographic validation still fails closed.
        }
    }
}
