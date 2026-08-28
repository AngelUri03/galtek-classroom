package com.galtek.classroom.network;

import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.time.Clock;
import java.util.Date;
import java.util.Base64;
import javax.net.ssl.X509TrustManager;

public class MasterTlsPeerTrustManager implements X509TrustManager {

    private final MasterTrustStore trustStore;
    private final Clock clock;

    public MasterTlsPeerTrustManager(MasterTrustStore trustStore) {
        this(trustStore, Clock.systemUTC());
    }

    public MasterTlsPeerTrustManager(MasterTrustStore trustStore, Clock clock) {
        this.trustStore = trustStore;
        this.clock = clock;
    }

    @Override
    public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
        if (chain == null || chain.length == 0) {
            throw new CertificateException("mTLS client certificate is required.");
        }

        X509Certificate certificate = chain[0];
        certificate.checkValidity(Date.from(clock.instant()));
        String fingerprint = NetworkIdentityCrypto.fingerprint(certificate.getPublicKey().getEncoded());
        String subjectPublicKeyInfoBase64 = Base64.getEncoder().encodeToString(
                certificate.getPublicKey().getEncoded());

        MasterTrustStoreReadResult read = trustStore.read();
        if (read.status() != MasterTrustStoreReadStatus.LOADED) {
            throw new CertificateException("Master trust store is unavailable.");
        }

        boolean trusted = read.document().pairedClients().stream()
                .anyMatch(client -> client.status() == PairingStatus.PAIRED
                        && client.revokedAtUtc() == null
                        && client.clientPublicKeyFingerprint().equals(fingerprint)
                        && client.clientPublicKeySubjectPublicKeyInfoBase64().equals(subjectPublicKeyInfoBase64));

        if (!trusted) {
            throw new CertificateException("mTLS client certificate is not paired with this Master.");
        }
    }

    @Override
    public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
        throw new CertificateException("Master does not accept arbitrary server certificates.");
    }

    @Override
    public X509Certificate[] getAcceptedIssuers() {
        return new X509Certificate[0];
    }
}
