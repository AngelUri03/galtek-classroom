package com.galtek.classroom.network;

import com.galtek.classroom.network.v1.ClientEnvelope;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.ConnectionState;
import com.galtek.classroom.network.v1.ConnectionStatus;
import com.galtek.classroom.network.v1.Heartbeat;
import com.galtek.classroom.network.v1.HeartbeatAck;
import com.galtek.classroom.network.v1.MasterEnvelope;
import com.galtek.classroom.network.v1.NetworkConnectionGrpc;
import com.galtek.classroom.network.v1.OperationAccepted;
import com.galtek.classroom.network.v1.OperationResult;
import io.grpc.stub.StreamObserver;
import java.time.Clock;
import java.util.UUID;

public class MasterNetworkGrpcService extends NetworkConnectionGrpc.NetworkConnectionImplBase {

    private final MasterNetworkConnectionAuthenticator authenticator;
    private final ClientConnectionRegistry connectionRegistry;
    private final NetworkClientConnectionService networkClientConnectionService;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final Clock clock;
    private final Runnable heartbeatMonitorActivation;

    public MasterNetworkGrpcService(
            MasterNetworkConnectionAuthenticator authenticator,
            ClientConnectionRegistry connectionRegistry,
            NetworkClientConnectionService networkClientConnectionService,
            Clock clock) {
        this(
                authenticator,
                connectionRegistry,
                networkClientConnectionService,
                new MasterRemoteOperationGateway(clock),
                clock,
                () -> {
                });
    }

    public MasterNetworkGrpcService(
            MasterNetworkConnectionAuthenticator authenticator,
            ClientConnectionRegistry connectionRegistry,
            NetworkClientConnectionService networkClientConnectionService,
            MasterRemoteOperationGateway remoteOperationGateway,
            Clock clock,
            Runnable heartbeatMonitorActivation) {
        this.authenticator = authenticator;
        this.connectionRegistry = connectionRegistry;
        this.networkClientConnectionService = networkClientConnectionService;
        this.remoteOperationGateway = remoteOperationGateway;
        this.clock = clock;
        this.heartbeatMonitorActivation = heartbeatMonitorActivation == null ? () -> {
        } : heartbeatMonitorActivation;
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
                    case OPERATION_ACCEPTED -> handleOperationAccepted(envelope.getOperationAccepted());
                    case OPERATION_RESULT -> handleOperationResult(envelope.getOperationResult());
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
                RegisteredNetworkDevice registeredDevice =
                        networkClientConnectionService.recordAcceptedHello(descriptor, hello);
                connectionRegistry.markConnecting(descriptor, hello, connectionId, registeredDevice);
                sendStatus(
                        clientNetworkIdentityId.toString(),
                        ConnectionState.CONNECTION_STATE_CONNECTING,
                        "",
                        "");
                ClientConnectionSnapshot snapshot = connectionRegistry.markOnline(clientNetworkIdentityId, connectionId);
                remoteOperationGateway.registerSession(snapshot, responseObserver);
                heartbeatMonitorActivation.run();
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
                    remoteOperationGateway.disconnect(clientNetworkIdentityId, connectionId, authorization.reasonCode());
                    reject(
                            clientNetworkIdentityId.toString(),
                            authorization.reasonCode(),
                            authorization.message());
                    return;
                }

                ClientConnectionSnapshot snapshot = connectionRegistry.markOnline(clientNetworkIdentityId, connectionId);
                remoteOperationGateway.registerSession(snapshot, responseObserver);
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

            private void handleOperationAccepted(OperationAccepted operationAccepted) {
                if (!operationResponseAuthorized("OperationAccepted")) {
                    return;
                }
                if (!MasterNetworkTransportConstants.PROTOCOL_VERSION.equals(operationAccepted.getProtocolVersion())) {
                    reject(
                            clientNetworkIdentityId.toString(),
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            "OperationAccepted uses an unsupported operation protocol version.");
                    return;
                }

                remoteOperationGateway.handleAccepted(operationAccepted, clientNetworkIdentityId, connectionId);
            }

            private void handleOperationResult(OperationResult operationResult) {
                if (!operationResponseAuthorized("OperationResult")) {
                    return;
                }
                if (!MasterNetworkTransportConstants.PROTOCOL_VERSION.equals(operationResult.getProtocolVersion())) {
                    reject(
                            clientNetworkIdentityId.toString(),
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            "OperationResult uses an unsupported operation protocol version.");
                    return;
                }

                remoteOperationGateway.handleResult(operationResult, clientNetworkIdentityId, connectionId);
            }

            private boolean operationResponseAuthorized(String messageName) {
                if (!accepted || clientNetworkIdentityId == null) {
                    reject(
                            "",
                            MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                            messageName + " requires an accepted ClientHello.");
                    return false;
                }

                MasterNetworkConnectionAuthorization authorization = authenticator.authorizeExisting(
                        descriptor,
                        tlsFingerprint);
                if (!authorization.accepted()) {
                    connectionRegistry.markOffline(clientNetworkIdentityId, connectionId, authorization.reasonCode());
                    remoteOperationGateway.disconnect(clientNetworkIdentityId, connectionId, authorization.reasonCode());
                    reject(
                            clientNetworkIdentityId.toString(),
                            authorization.reasonCode(),
                            authorization.message());
                    return false;
                }

                return true;
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
                    remoteOperationGateway.disconnect(clientNetworkIdentityId, connectionId, reasonCode);
                }
            }
        };
    }
}
