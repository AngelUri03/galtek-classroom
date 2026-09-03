package com.galtek.classroom.credentialvault;

final class CredentialVaultLimits {

    static final int MASTER_PASSWORD_MAX_CHARS = 4096;
    static final int CREDENTIAL_PASSWORD_MAX_CHARS = 8192;
    static final int DISPLAY_NAME_MAX_CHARS = 256;
    static final int LOGIN_IDENTIFIER_MAX_CHARS = 512;
    static final int CREDENTIAL_ID_MAX_CHARS = 128;

    private CredentialVaultLimits() {
    }
}
