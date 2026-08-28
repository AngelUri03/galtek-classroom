package com.galtek.classroom.network;

import io.grpc.Context;
import io.grpc.Contexts;
import io.grpc.Grpc;
import io.grpc.Metadata;
import io.grpc.ServerCall;
import io.grpc.ServerCallHandler;
import io.grpc.ServerInterceptor;
import java.security.cert.Certificate;
import java.security.cert.X509Certificate;
import javax.net.ssl.SSLPeerUnverifiedException;
import javax.net.ssl.SSLSession;

public class MtlsPeerCertificateServerInterceptor implements ServerInterceptor {

    public static final Context.Key<String> CLIENT_CERTIFICATE_FINGERPRINT =
            Context.key("galtek-client-certificate-fingerprint");

    @Override
    public <ReqT, RespT> ServerCall.Listener<ReqT> interceptCall(
            ServerCall<ReqT, RespT> call,
            Metadata headers,
            ServerCallHandler<ReqT, RespT> next) {
        Context context = Context.current().withValue(
                CLIENT_CERTIFICATE_FINGERPRINT,
                clientCertificateFingerprint(call.getAttributes().get(Grpc.TRANSPORT_ATTR_SSL_SESSION)));
        return Contexts.interceptCall(context, call, headers, next);
    }

    private static String clientCertificateFingerprint(SSLSession session) {
        if (session == null) {
            return null;
        }

        try {
            Certificate[] certificates = session.getPeerCertificates();
            if (certificates.length == 0 || !(certificates[0] instanceof X509Certificate certificate)) {
                return null;
            }

            return NetworkIdentityCrypto.fingerprint(certificate.getPublicKey().getEncoded());
        } catch (SSLPeerUnverifiedException exception) {
            return null;
        }
    }
}
