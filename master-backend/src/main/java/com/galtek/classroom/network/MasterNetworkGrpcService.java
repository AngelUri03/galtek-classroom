package com.galtek.classroom.network;

import com.galtek.classroom.network.v1.ClientEnvelope;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.ConnectionState;
import com.galtek.classroom.network.v1.ConnectionStatus;
import com.galtek.classroom.network.v1.Heartbeat;
import com.galtek.classroom.network.v1.HeartbeatAck;
import com.galtek.classroom.network.v1.MasterEnvelope;
import com.galtek.classroom.network.v1.NetworkConnectionGrpc;
import io.grpc.stub.StreamObserver;
import java.time.Clock;
import java.util.UUID;

public class MasterNetworkGrpcService extends NetworkConnectionGrpc.NetworkConnectionImplBase {

    private final MasterNetworkConnectionAuthenticator authenticator;
    private final ClientConnectionRegistry connectionRegistry;
    private final Clock clock;

    public MasterNetworkGrpcService(
            MasterNetworkConnectionAuthenticator authenticator,
            ClientConnectionRegistry connectionRegistry,
            Clock clock) {
        this.authenticator = authenticator;
        this.connectionRegistry = connectionRegistry;
        this.clock = clock;
    }

    @Override
    public StreamObserver<ClientEnvelope> connect(StreamObserver<MasterEnvelope> responseObserver) {
        String tlsFingerprint = MtlsPeerCertificateServerInterceptor.CLIENT_CERTIFICATE_FINGERPRINT.get();
        String connectionId = UUID.randomUUID().toString();

        return new StreamObserver<>() {
            private UUID clientNetworkIdentityId;
            private ClientNetworkIdentityDescriptor descriptor;
            private boolean accepted;
            private boolean closed;

            @Override
            public void onNext(ClientEnvelope envelope) {
                if (closed) {
                    return;
                }

                if (!MasterNetworkTransportConstants.PROTOCOL_VERSION.equals(envelope.getProtocolVersion())) {
                    reject(
                            "",
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            "Unsupported network protocol version.");
                    return;
                }

                switch (envelope.getPayloadCase()) {
                    case CLIENT_HELLO -> handleHello(envelope.getClientHello());
                    case HEARTBEAT -> handleHeartbeat(envelope.getHeartbeat());
                    default -> reject(
                            clientNetworkIdentityId == null ? "" : clientNetworkIdentityId.toString(),
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            "Client message payload is required.");
                }
            }

            @Override
            public void onError(Throwable throwable) {
                closeOffline("STREAM_ERROR");
            }

            @Override
            public void onCompleted() {
                closeOffline("STREAM_COMPLETED");
                if (!closed) {
                    responseObserver.onCompleted();
                    closed = true;
                }
            }

            private void handleHello(ClientHello hello) {
                if (accepted) {
                    reject(
                            clientNetworkIdentityId.toString(),
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            "ClientHello can only be sent once.");
                    return;
                }

                MasterNetworkConnectionAuthorization authorization = authenticator.authenticate(
                        hello,
                        tlsFingerprint);
                if (!authorization.accepted()) {
                    reject(
                            authorization.clientNetworkIdentityId() == null
                                    ? hello.getClientNetworkIdentityId()
                                    : authorization.clientNetworkIdentityId().toString(),
                            authorization.reasonCode(),
                            authorization.message());
                    return;
                }

                clientNetworkIdentityId = authorization.clientNetworkIdentityId();
                descriptor = authorization.descriptor();
                connectionRegistry.markConnecting(descriptor, hello, connectionId);
                sendStatus(
                        clientNetworkIdentityId.toString(),
                        ConnectionState.CONNECTION_STATE_CONNECTING,
                        "",
                        "");
                connectionRegistry.markOnline(clientNetworkIdentityId, connectionId);
                accepted = true;
                sendStatus(
                        clientNetworkIdentityId.toString(),
                        ConnectionState.CONNECTION_STATE_ONLINE,
                        "",
                        "");
            }

            private void handleHeartbeat(Heartbeat heartbeat) {
                if (!accepted || clientNetworkIdentityId == null) {
                    reject(
                            "",
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            "Heartbeat requires an accepted ClientHello.");
                    return;
                }

                MasterNetworkConnectionAuthorization authorization = authenticator.authorizeExisting(
                        descriptor,
                        tlsFingerprint);
                if (!authorization.accepted()) {
                    connectionRegistry.markOffline(clientNetworkIdentityId, connectionId, authorization.reasonCode());
                    reject(
                            clientNetworkIdentityId.toString(),
                            authorization.reasonCode(),
                            authorization.message());
                    return;
                }

                connectionRegistry.markOnline(clientNetworkIdentityId, connectionId, heartbeat);
                responseObserver.onNext(MasterEnvelope.newBuilder()
                        .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                        .setHeartbeatAck(HeartbeatAck.newBuilder()
                                .setHeartbeatId(heartbeat.getHeartbeatId())
                                .setClientNetworkIdentityId(clientNetworkIdentityId.toString())
                                .setReceivedAtUnixMs(clock.instant().toEpochMilli())
                                .setStatus(ConnectionState.CONNECTION_STATE_ONLINE)
                                .build())
                        .build());
            }

            private void reject(String clientIdentityId, String reasonCode, String message) {
                sendStatus(
                        clientIdentityId == null ? "" : clientIdentityId,
                        ConnectionState.CONNECTION_STATE_REJECTED,
                        reasonCode,
                        message == null ? "" : message);
                responseObserver.onCompleted();
                closed = true;
            }

            private void sendStatus(
                    String clientIdentityId,
                    ConnectionState state,
                    String reasonCode,
                    String message) {
                responseObserver.onNext(MasterEnvelope.newBuilder()
                        .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                        .setConnectionStatus(ConnectionStatus.newBuilder()
                                .setClientNetworkIdentityId(clientIdentityId)
                                .setStatus(state)
                                .setReasonCode(reasonCode == null ? "" : reasonCode)
                                .setMessage(message == null ? "" : message)
                                .setServerTimeUnixMs(clock.instant().toEpochMilli())
                                .build())
                        .build());
            }

            private void closeOffline(String reasonCode) {
                if (accepted && clientNetworkIdentityId != null) {
                    connectionRegistry.markOffline(clientNetworkIdentityId, connectionId, reasonCode);
                }
            }
        };
    }
}
