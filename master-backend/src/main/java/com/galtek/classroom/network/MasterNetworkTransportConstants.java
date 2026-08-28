package com.galtek.classroom.network;

public final class MasterNetworkTransportConstants {

    public static final String PROTOCOL_VERSION = "network.v1";
    public static final String MUTUAL_TLS_REQUIRED = "MUTUAL_TLS_REQUIRED";
    public static final String CERTIFICATE_FINGERPRINT_MISMATCH = "CERTIFICATE_FINGERPRINT_MISMATCH";
    public static final String CLIENT_NOT_PAIRED = "CLIENT_NOT_PAIRED";
    public static final String CLIENT_REVOKED = "CLIENT_REVOKED";
    public static final String NETWORK_IDENTITY_MISMATCH = "NETWORK_IDENTITY_MISMATCH";
    public static final String PROTOCOL_VIOLATION = "PROTOCOL_VIOLATION";

    private MasterNetworkTransportConstants() {
    }
}
