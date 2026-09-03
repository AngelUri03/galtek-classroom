package com.galtek.classroom.credentialvault;

import com.galtek.classroom.persistence.sqlite.MasterDatabasePath;
import java.security.SecureRandom;
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
public class CredentialVaultConfiguration {

    @Bean
    CredentialVaultCryptoParameters credentialVaultCryptoParameters() {
        return CredentialVaultCryptoParameters.productionDefaults();
    }

    @Bean
    CredentialVaultStore credentialVaultStore(MasterDatabasePath masterDatabasePath) {
        return new CredentialVaultStore(masterDatabasePath.dataDirectory(), new LocalCredentialVaultFileSecurity());
    }

    @Bean
    CredentialVaultCrypto credentialVaultCrypto(CredentialVaultCryptoParameters parameters) {
        return new CredentialVaultCrypto(parameters, new SecureRandom());
    }

    @Bean
    CredentialVaultSessionManager credentialVaultSessionManager(
            Clock clock,
            CredentialVaultCryptoParameters parameters) {
        return new CredentialVaultSessionManager(clock, parameters.sessionIdleTimeout(), new SecureRandom());
    }

    @Bean
    CredentialVaultAuditSink credentialVaultAuditSink() {
        return new LoggingCredentialVaultAuditSink();
    }

    @Bean
    CredentialVaultService credentialVaultService(
            CredentialVaultStore store,
            CredentialVaultCrypto crypto,
            CredentialVaultSessionManager sessionManager,
            CredentialVaultAuditSink auditSink,
            Clock clock) {
        return new CredentialVaultService(store, crypto, sessionManager, auditSink, clock, new SecureRandom());
    }
}
