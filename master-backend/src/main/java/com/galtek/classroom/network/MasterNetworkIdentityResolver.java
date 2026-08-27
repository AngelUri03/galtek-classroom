package com.galtek.classroom.network;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.util.HexFormat;
import java.util.UUID;

public class MasterNetworkIdentityResolver {

    private final MasterNetworkIdentityStore store;
    private final MasterNetworkIdentityKeyStore keyStore;
    private final Clock clock;

    public MasterNetworkIdentityResolver(
            MasterNetworkIdentityStore store,
            MasterNetworkIdentityKeyStore keyStore,
            Clock clock) {
        this.store = store;
        this.keyStore = keyStore;
        this.clock = clock;
    }

    public MasterNetworkIdentityResolution resolve() {
        MasterNetworkIdentityStoreReadResult read = store.read();

        if (read.status() == MasterNetworkIdentityStoreReadStatus.LOADED) {
            return validateLoaded(read.metadata(), read.filePath());
        }

        if (read.status() == MasterNetworkIdentityStoreReadStatus.INVALID) {
            return MasterNetworkIdentityResolution.invalid(read.filePath(), read.errorMessage());
        }

        if (keyStore.hasAnyKeyMaterial()) {
            return MasterNetworkIdentityResolution.invalid(
                    read.filePath(),
                    "master-network-identity.json is missing but Master key material already exists; refusing to regenerate silently.");
        }

        return createNewIdentity(read.filePath());
    }

    private MasterNetworkIdentityResolution validateLoaded(
            MasterNetworkIdentityMetadata metadata,
            String filePath) {
        MasterNetworkKeyLookupResult lookup = keyStore.lookup(metadata.keyId());
        if (lookup.status() == MasterNetworkKeyLookupStatus.MISSING) {
            return MasterNetworkIdentityResolution.keyMissing(
                    filePath,
                    metadata,
                    lookup.errorMessage());
        }
        if (lookup.status() == MasterNetworkKeyLookupStatus.INVALID) {
            return MasterNetworkIdentityResolution.invalid(filePath, lookup.errorMessage());
        }
        if (!metadata.publicKeyFingerprint().equals(lookup.publicKeyFingerprint())
                || !metadata.publicKeySubjectPublicKeyInfoBase64().equals(lookup.subjectPublicKeyInfoBase64())) {
            return MasterNetworkIdentityResolution.invalid(
                    filePath,
                    "Master Network Identity public key fingerprint does not match key material.");
        }

        return MasterNetworkIdentityResolution.ready(metadata, filePath, false);
    }

    private MasterNetworkIdentityResolution createNewIdentity(String filePath) {
        UUID masterNetworkIdentityId = UUID.randomUUID();
        String keyId = deriveKeyId(masterNetworkIdentityId);
        MasterNetworkKeyCreationResult key = keyStore.create(keyId);
        if (!key.created()) {
            return MasterNetworkIdentityResolution.invalid(filePath, key.errorMessage());
        }

        MasterNetworkIdentityMetadata metadata = new MasterNetworkIdentityMetadata(
                PairingConstants.SCHEMA_VERSION,
                masterNetworkIdentityId,
                keyId,
                key.publicKeyFingerprint(),
                key.subjectPublicKeyInfoBase64(),
                clock.instant());

        try {
            store.writeNew(metadata);
        } catch (IOException | IllegalArgumentException exception) {
            return MasterNetworkIdentityResolution.invalid(
                    filePath,
                    "master-network-identity.json could not be written: " + exception.getMessage());
        }

        return MasterNetworkIdentityResolution.ready(metadata, filePath, true);
    }

    static String deriveKeyId(UUID masterNetworkIdentityId) {
        String source = PairingConstants.KEY_DERIVATION_NAMESPACE + ":" + masterNetworkIdentityId;
        return HexFormat.of().formatHex(NetworkIdentityCrypto.sha256(source.getBytes(StandardCharsets.UTF_8)));
    }
}
