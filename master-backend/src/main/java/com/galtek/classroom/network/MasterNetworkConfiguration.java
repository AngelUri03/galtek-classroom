package com.galtek.classroom.network;

import com.galtek.classroom.persistence.sqlite.MasterDatabasePath;
import java.time.Clock;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
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
}
