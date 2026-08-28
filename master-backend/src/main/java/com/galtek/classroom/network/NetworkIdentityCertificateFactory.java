package com.galtek.classroom.network;

import java.math.BigInteger;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.SecureRandom;
import java.security.cert.X509Certificate;
import java.time.Clock;
import java.time.Duration;
import java.util.Date;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.asn1.x509.BasicConstraints;
import org.bouncycastle.asn1.x509.ExtendedKeyUsage;
import org.bouncycastle.asn1.x509.Extension;
import org.bouncycastle.asn1.x509.KeyPurposeId;
import org.bouncycastle.asn1.x509.KeyUsage;
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter;
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;

public final class NetworkIdentityCertificateFactory {

    private static final Duration DEFAULT_LIFETIME = Duration.ofDays(7);
    private static final Duration CLOCK_SKEW = Duration.ofMinutes(5);

    private NetworkIdentityCertificateFactory() {
    }

    public static X509Certificate createSelfSigned(
            PublicKey publicKey,
            PrivateKey privateKey,
            String commonName,
            Clock clock,
            SecureRandom secureRandom,
            KeyPurposeId... purposes) {
        try {
            X500Name subject = new X500Name("CN=" + sanitize(commonName));
            Date notBefore = Date.from(clock.instant().minus(CLOCK_SKEW));
            Date notAfter = Date.from(clock.instant().plus(DEFAULT_LIFETIME));
            BigInteger serial = new BigInteger(160, secureRandom).abs();
            var builder = new JcaX509v3CertificateBuilder(
                    subject,
                    serial,
                    notBefore,
                    notAfter,
                    subject,
                    publicKey);

            builder.addExtension(Extension.basicConstraints, true, new BasicConstraints(false));
            builder.addExtension(Extension.keyUsage, true, new KeyUsage(KeyUsage.digitalSignature));
            if (purposes != null && purposes.length > 0) {
                builder.addExtension(Extension.extendedKeyUsage, false, new ExtendedKeyUsage(purposes));
            }

            var signer = new JcaContentSignerBuilder("SHA256withRSA").build(privateKey);
            X509Certificate certificate = new JcaX509CertificateConverter()
                    .getCertificate(builder.build(signer));
            certificate.verify(publicKey);
            return certificate;
        } catch (Exception exception) {
            throw new IllegalStateException("Network Identity TLS certificate could not be created.", exception);
        }
    }

    private static String sanitize(String commonName) {
        String value = commonName == null || commonName.isBlank() ? "Galtek Classroom" : commonName;
        return value.replaceAll("[^A-Za-z0-9 ._-]", "_");
    }
}
