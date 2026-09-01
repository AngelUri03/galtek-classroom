package com.galtek.classroom.network;

import com.galtek.classroom.persistence.sqlite.MasterDatabasePath;
import java.time.Clock;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
@EnableConfigurationProperties(MasterNetworkGrpcProperties.class)
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterNetworkConfiguration {

    @Bean
    MasterNetworkIdentityStore masterNetworkIdentityStore(MasterDatabasePath databasePath) {
        return new MasterNetworkIdentityStore(databasePath.dataDirectory());
    }

    @Bean
    MasterNetworkIdentityKeyStore masterNetworkIdentityKeyStore(MasterDatabasePath databasePath) {
        return new FileMasterNetworkIdentityKeyStore(databasePath.dataDirectory());
    }

    @Bean
    MasterNetworkIdentityResolver masterNetworkIdentityResolver(
            MasterNetworkIdentityStore store,
            MasterNetworkIdentityKeyStore keyStore,
            Clock clock) {
        return new MasterNetworkIdentityResolver(store, keyStore, clock);
    }

    @Bean
    MasterTrustStore masterTrustStore(MasterDatabasePath databasePath) {
        return new MasterTrustStore(databasePath.dataDirectory());
    }

    @Bean
    MasterPairingService masterPairingService(
            MasterNetworkIdentityResolver identityResolver,
            MasterNetworkIdentityKeyStore keyStore,
            MasterTrustStore trustStore,
            Clock clock) {
        return new MasterPairingService(identityResolver, keyStore, trustStore, clock);
    }

    @Bean
    ClientConnectionRegistry clientConnectionRegistry(Clock clock) {
        return new ClientConnectionRegistry(clock);
    }

    @Bean
    @ConditionalOnProperty(
            prefix = "galtek.classroom.master.network.grpc",
            name = "enabled",
            havingValue = "true")
    MasterNetworkConnectionAuthenticator masterNetworkConnectionAuthenticator(
            MasterPairingService pairingService) {
        return new MasterNetworkConnectionAuthenticator(pairingService);
    }

    @Bean
    @ConditionalOnProperty(
            prefix = "galtek.classroom.master.network.grpc",
            name = "enabled",
            havingValue = "true")
    MasterNetworkHeartbeatMonitor masterNetworkHeartbeatMonitor(
            ClientConnectionRegistry connectionRegistry,
            MasterNetworkGrpcProperties properties) {
        return new MasterNetworkHeartbeatMonitor(connectionRegistry, properties);
    }

    @Bean
    @ConditionalOnProperty(
            prefix = "galtek.classroom.master.network.grpc",
            name = "enabled",
            havingValue = "true")
    MasterNetworkGrpcService masterNetworkGrpcService(
            MasterNetworkConnectionAuthenticator authenticator,
            ClientConnectionRegistry connectionRegistry,
            NetworkClientConnectionService networkClientConnectionService,
            MasterRemoteOperationGateway remoteOperationGateway,
            PowerOperationReconciliationService reconciliationService,
            Clock clock,
            MasterNetworkHeartbeatMonitor heartbeatMonitor) {
        return new MasterNetworkGrpcService(
                authenticator,
                connectionRegistry,
                networkClientConnectionService,
                remoteOperationGateway,
                reconciliationService,
                clock,
                heartbeatMonitor::ensureScanning);
    }

    @Bean
    @ConditionalOnProperty(
            prefix = "galtek.classroom.master.network.grpc",
            name = "enabled",
            havingValue = "true")
    MasterNetworkGrpcServer masterNetworkGrpcServer(
            MasterNetworkGrpcProperties properties,
            MasterNetworkIdentityResolver identityResolver,
            MasterNetworkIdentityKeyStore keyStore,
            MasterTrustStore trustStore,
            MasterNetworkGrpcService grpcService) {
        return new MasterNetworkGrpcServer(properties, identityResolver, keyStore, trustStore, grpcService);
    }
}
