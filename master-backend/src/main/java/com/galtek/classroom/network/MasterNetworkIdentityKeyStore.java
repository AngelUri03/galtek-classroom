package com.galtek.classroom.network;

public interface MasterNetworkIdentityKeyStore {

    boolean hasAnyKeyMaterial();

    MasterNetworkKeyCreationResult create(String keyId);

    MasterNetworkKeyLookupResult lookup(String keyId);

    MasterNetworkSignatureResult sign(String keyId, byte[] data);
}
