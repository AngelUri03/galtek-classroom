package com.galtek.classroom.network;

import java.time.Duration;

public final class PairingConstants {

    public static final int SCHEMA_VERSION = 1;
    public static final String PURPOSE = "GALTEK_CLASSROOM_MASTER_CLIENT_PAIRING_V1";
    public static final String MASTER_NETWORK_IDENTITY_FILE_NAME = "master-network-identity.json";
    public static final String MASTER_NETWORK_PRIVATE_KEY_FILE_NAME = "master-network-identity.key";
    public static final String MASTER_NETWORK_PROTECTOR_FILE_NAME = "master-network-identity.protector";
    public static final String PAIRED_CLIENTS_FILE_NAME = "paired-clients.json";
    public static final String KEY_DERIVATION_NAMESPACE = "GALTEK_CLASSROOM_MASTER_NETWORK_IDENTITY_V1";
    public static final Duration CHALLENGE_TTL = Duration.ofMinutes(5);
    public static final int NONCE_SIZE_BYTES = 32;
    public static final int RSA_KEY_SIZE_BITS = 2048;

    public static final String EXPLICIT_INTENT_REQUIRED = "PAIRING_EXPLICIT_INTENT_REQUIRED";
    public static final String CHALLENGE_INVALID = "PAIRING_CHALLENGE_INVALID";
    public static final String CHALLENGE_EXPIRED = "PAIRING_CHALLENGE_EXPIRED";
    public static final String REPLAY_REJECTED = "PAIRING_REPLAY_REJECTED";
    public static final String SIGNATURE_INVALID = "PAIRING_SIGNATURE_INVALID";
    public static final String FINGERPRINT_MISMATCH = "PAIRING_FINGERPRINT_MISMATCH";
    public static final String CLIENT_REVOKED = "PAIRING_CLIENT_REVOKED";
    public static final String TRUST_STORE_INVALID = "PAIRING_TRUST_STORE_INVALID";
    public static final String MASTER_NETWORK_IDENTITY_UNAVAILABLE = "MASTER_NETWORK_IDENTITY_UNAVAILABLE";

    private PairingConstants() {
    }
}
