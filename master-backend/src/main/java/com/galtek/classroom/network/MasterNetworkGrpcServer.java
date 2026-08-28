package com.galtek.classroom.network;

import io.grpc.Server;
import io.grpc.ServerInterceptors;
import io.grpc.netty.shaded.io.grpc.netty.GrpcSslContexts;
import io.grpc.netty.shaded.io.grpc.netty.NettyServerBuilder;
import io.grpc.netty.shaded.io.netty.handler.ssl.ClientAuth;
import io.grpc.netty.shaded.io.netty.handler.ssl.SslContextBuilder;
import java.io.IOException;
import java.util.concurrent.TimeUnit;
import org.springframework.context.SmartLifecycle;

public class MasterNetworkGrpcServer implements SmartLifecycle {

    private final MasterNetworkGrpcProperties properties;
    private final MasterNetworkIdentityResolver identityResolver;
    private final MasterNetworkIdentityKeyStore keyStore;
    private final MasterTrustStore trustStore;
    private final MasterNetworkGrpcService grpcService;
    private Server server;
    private volatile boolean running;

    public MasterNetworkGrpcServer(
            MasterNetworkGrpcProperties properties,
            MasterNetworkIdentityResolver identityResolver,
            MasterNetworkIdentityKeyStore keyStore,
            MasterTrustStore trustStore,
            MasterNetworkGrpcService grpcService) {
        this.properties = properties;
        this.identityResolver = identityResolver;
        this.keyStore = keyStore;
        this.trustStore = trustStore;
        this.grpcService = grpcService;
    }

    @Override
    public void start() {
        if (running) {
            return;
        }

        MasterNetworkIdentityResolution identity = identityResolver.resolve();
        if (!identity.ready()) {
            throw new IllegalStateException("Master Network Identity is unavailable: " + identity.errorMessage());
        }

        MasterNetworkTlsIdentityResult tlsIdentity = keyStore.tlsIdentity(identity.metadata().keyId());
        if (!tlsIdentity.ready()) {
            throw new IllegalStateException(tlsIdentity.errorMessage());
        }

        try {
            var sslContext = GrpcSslContexts.configure(SslContextBuilder.forServer(
                            tlsIdentity.privateKey(),
                            tlsIdentity.certificate()))
                    .trustManager(new MasterTlsTrustManagerFactory(trustStore))
                    .clientAuth(ClientAuth.REQUIRE)
                    .build();

            server = NettyServerBuilder.forPort(properties.getPort())
                    .sslContext(sslContext)
                    .maxInboundMessageSize(properties.getMaxInboundMessageSize())
                    .addService(ServerInterceptors.intercept(
                            grpcService,
                            new MtlsPeerCertificateServerInterceptor()))
                    .build()
                    .start();
            running = true;
        } catch (IOException exception) {
            throw new IllegalStateException("Master gRPC network server could not start.", exception);
        }
    }

    @Override
    public void stop() {
        if (server != null) {
            server.shutdown();
            try {
                if (!server.awaitTermination(5, TimeUnit.SECONDS)) {
                    server.shutdownNow();
                }
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
                server.shutdownNow();
            }
        }
        running = false;
    }

    @Override
    public boolean isRunning() {
        return running;
    }
}
