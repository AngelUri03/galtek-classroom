package com.galtek.classroom.network;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.spec.X509EncodedKeySpec;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeFormatterBuilder;
import java.time.temporal.ChronoField;
import java.util.Base64;
import java.util.HexFormat;
import java.util.Locale;

public final class NetworkIdentityCrypto {

    private static final DateTimeFormatter CANONICAL_UTC_FORMATTER = new DateTimeFormatterBuilder()
            .appendPattern("yyyy-MM-dd'T'HH:mm:ss")
            .appendFraction(ChronoField.NANO_OF_SECOND, 0, 7, true)
            .appendLiteral('Z')
            .toFormatter(Locale.ROOT)
            .withZone(ZoneOffset.UTC);

    private NetworkIdentityCrypto() {
    }

    public static String createNonceBase64(SecureRandom random) {
        byte[] nonce = new byte[PairingConstants.NONCE_SIZE_BYTES];
        random.nextBytes(nonce);
        return Base64.getEncoder().encodeToString(nonce);
    }

    public static byte[] canonicalChallengeBytes(PairingChallenge challenge) {
        String canonical = String.join("\n",
                "schemaVersion=" + challenge.schemaVersion(),
                "purpose=" + challenge.purpose(),
                "challengeId=" + challenge.challengeId(),
                "masterNetworkIdentityId=" + challenge.masterNetworkIdentityId(),
                "clientNetworkIdentityId=" + challenge.clientNetworkIdentityId(),
                "clientInstallationId=" + challenge.clientInstallationId(),
                "masterPublicKeyFingerprint=" + challenge.masterPublicKeyFingerprint(),
                "clientPublicKeyFingerprint=" + challenge.clientPublicKeyFingerprint(),
                "masterPublicKeySubjectPublicKeyInfoBase64=" + challenge.masterPublicKeySubjectPublicKeyInfoBase64(),
                "clientPublicKeySubjectPublicKeyInfoBase64=" + challenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                "nonceBase64=" + challenge.nonceBase64(),
                "issuedAtUtc=" + formatUtc(challenge.issuedAtUtc()),
                "expiresAtUtc=" + formatUtc(challenge.expiresAtUtc()));
        return canonical.getBytes(StandardCharsets.UTF_8);
    }

    public static byte[] canonicalResponseBytes(PairingResponse response) {
        String canonical = String.join("\n",
                "schemaVersion=" + response.schemaVersion(),
                "purpose=" + response.purpose(),
                "challengeId=" + response.challengeId(),
                "masterNetworkIdentityId=" + response.masterNetworkIdentityId(),
                "clientNetworkIdentityId=" + response.clientNetworkIdentityId(),
                "clientInstallationId=" + response.clientInstallationId(),
                "masterPublicKeyFingerprint=" + response.masterPublicKeyFingerprint(),
                "clientPublicKeyFingerprint=" + response.clientPublicKeyFingerprint(),
                "challengeNonceBase64=" + response.challengeNonceBase64(),
                "responseNonceBase64=" + response.responseNonceBase64(),
                "signedAtUtc=" + formatUtc(response.signedAtUtc()));
        return canonical.getBytes(StandardCharsets.UTF_8);
    }

    public static boolean verifySignature(
            String subjectPublicKeyInfoBase64,
            byte[] canonicalPayload,
            String signatureBase64) {
        try {
            PublicKey publicKey = importPublicKey(subjectPublicKeyInfoBase64);
            Signature verifier = Signature.getInstance("SHA256withRSA");
            verifier.initVerify(publicKey);
            verifier.update(canonicalPayload);
            return verifier.verify(Base64.getDecoder().decode(signatureBase64));
        } catch (IllegalArgumentException | GeneralSecurityException exception) {
            return false;
        }
    }

    public static PublicKey importPublicKey(String subjectPublicKeyInfoBase64) throws GeneralSecurityException {
        byte[] encoded = Base64.getDecoder().decode(subjectPublicKeyInfoBase64);
        return KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(encoded));
    }

    public static String publicKeyFingerprintBase64(String subjectPublicKeyInfoBase64) {
        return fingerprint(Base64.getDecoder().decode(subjectPublicKeyInfoBase64));
    }

    public static String fingerprint(byte[] subjectPublicKeyInfo) {
        return HexFormat.of().formatHex(sha256(subjectPublicKeyInfo));
    }

    public static byte[] sha256(byte[] value) {
        try {
            return MessageDigest.getInstance("SHA-256").digest(value);
        } catch (GeneralSecurityException exception) {
            throw new IllegalStateException("SHA-256 is not available.", exception);
        }
    }

    public static boolean isValidSha256Hex(String value) {
        return value != null && value.matches("[0-9a-f]{64}");
    }

    public static boolean isValidNonce(String value) {
        if (value == null || value.isBlank()) {
            return false;
        }
        try {
            return Base64.getDecoder().decode(value).length >= 16;
        } catch (IllegalArgumentException exception) {
            return false;
        }
    }

    public static String formatUtc(Instant value) {
        return CANONICAL_UTC_FORMATTER.format(value);
    }
}
