package com.galtek.classroom.network;

import com.galtek.classroom.operations.ErrorCode;
import java.io.IOException;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

public class MasterPairingService {

    private final MasterNetworkIdentityResolver masterIdentityResolver;
    private final MasterNetworkIdentityKeyStore keyStore;
    private final MasterTrustStore trustStore;
    private final Clock clock;
    private final SecureRandom secureRandom;

    public MasterPairingService(
            MasterNetworkIdentityResolver masterIdentityResolver,
            MasterNetworkIdentityKeyStore keyStore,
            MasterTrustStore trustStore,
            Clock clock) {
        this(masterIdentityResolver, keyStore, trustStore, clock, new SecureRandom());
    }

    MasterPairingService(
            MasterNetworkIdentityResolver masterIdentityResolver,
            MasterNetworkIdentityKeyStore keyStore,
            MasterTrustStore trustStore,
            Clock clock,
            SecureRandom secureRandom) {
        this.masterIdentityResolver = masterIdentityResolver;
        this.keyStore = keyStore;
        this.trustStore = trustStore;
        this.clock = clock;
        this.secureRandom = secureRandom;
    }

    public MasterPairingChallengeResult createPairingChallenge(
            ClientNetworkIdentityDescriptor client,
            boolean explicitIntent) {
        if (!explicitIntent) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.EXPLICIT_INTENT_REQUIRED,
                    "Pairing requires explicit teacher or administrator intent.");
        }

        if (!isValidClientDescriptor(client)) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.FINGERPRINT_MISMATCH,
                    "Client public key fingerprint does not match its public key.");
        }

        MasterNetworkIdentityResolution identity = masterIdentityResolver.resolve();
        if (!identity.ready()) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.MASTER_NETWORK_IDENTITY_UNAVAILABLE,
                    identity.errorMessage());
        }

        DocumentLoadResult loaded = loadOrCreateDocument(identity.metadata());
        if (!loaded.valid()) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.TRUST_STORE_INVALID,
                    loaded.errorMessage());
        }

        ClientTrustRecord existing = findClient(loaded.document(), client.clientNetworkIdentityId());
        if (existing != null && existing.status() == PairingStatus.REVOKED) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.CLIENT_REVOKED,
                    "Client pairing was revoked and cannot be reused silently.");
        }

        Instant issuedAt = clock.instant();
        PairingChallenge unsignedChallenge = new PairingChallenge(
                PairingConstants.SCHEMA_VERSION,
                PairingConstants.PURPOSE,
                UUID.randomUUID(),
                identity.metadata().masterNetworkIdentityId(),
                client.clientNetworkIdentityId(),
                client.clientInstallationId(),
                identity.metadata().publicKeyFingerprint(),
                client.publicKeyFingerprint(),
                identity.metadata().publicKeySubjectPublicKeyInfoBase64(),
                client.subjectPublicKeyInfoBase64(),
                NetworkIdentityCrypto.createNonceBase64(secureRandom),
                issuedAt,
                issuedAt.plus(PairingConstants.CHALLENGE_TTL),
                "");

        MasterNetworkSignatureResult signature = keyStore.sign(
                identity.metadata().keyId(),
                NetworkIdentityCrypto.canonicalChallengeBytes(unsignedChallenge));
        if (!signature.signed()) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.MASTER_NETWORK_IDENTITY_UNAVAILABLE,
                    signature.errorMessage());
        }

        PairingChallenge challenge = new PairingChallenge(
                unsignedChallenge.schemaVersion(),
                unsignedChallenge.purpose(),
                unsignedChallenge.challengeId(),
                unsignedChallenge.masterNetworkIdentityId(),
                unsignedChallenge.clientNetworkIdentityId(),
                unsignedChallenge.clientInstallationId(),
                unsignedChallenge.masterPublicKeyFingerprint(),
                unsignedChallenge.clientPublicKeyFingerprint(),
                unsignedChallenge.masterPublicKeySubjectPublicKeyInfoBase64(),
                unsignedChallenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                unsignedChallenge.nonceBase64(),
                unsignedChallenge.issuedAtUtc(),
                unsignedChallenge.expiresAtUtc(),
                signature.signatureBase64());

        MasterTrustDocument updated = upsertPending(loaded.document(), challenge);
        try {
            trustStore.save(updated);
        } catch (IOException | IllegalArgumentException exception) {
            return MasterPairingChallengeResult.failed(
                    PairingConstants.TRUST_STORE_INVALID,
                    "paired-clients.json could not be written: " + exception.getMessage());
        }

        return MasterPairingChallengeResult.created(challenge);
    }

    public MasterPairingCompletionResult completePairing(PairingResponse response) {
        if (!isValidResponse(response)) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.CHALLENGE_INVALID,
                    "Pairing response is malformed.",
                    PairingStatus.UNPAIRED);
        }

        MasterNetworkIdentityResolution identity = masterIdentityResolver.resolve();
        if (!identity.ready()) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.MASTER_NETWORK_IDENTITY_UNAVAILABLE,
                    identity.errorMessage(),
                    PairingStatus.UNPAIRED);
        }

        DocumentLoadResult loaded = loadOrCreateDocument(identity.metadata());
        if (!loaded.valid()) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.TRUST_STORE_INVALID,
                    loaded.errorMessage(),
                    PairingStatus.UNPAIRED);
        }

        MasterPairingChallengeRecord pending = findChallenge(loaded.document(), response.challengeId());
        if (pending == null || pending.status() != PairingStatus.PAIRING_PENDING) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.REPLAY_REJECTED,
                    "Pairing challenge has already been consumed or was not issued by this Master.",
                    PairingStatus.UNPAIRED);
        }

        if (clock.instant().isAfter(pending.expiresAtUtc())) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.CHALLENGE_EXPIRED,
                    "Pairing challenge has expired.",
                    PairingStatus.PAIRING_PENDING);
        }

        if (!matchesPendingChallenge(response, pending)) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.FINGERPRINT_MISMATCH,
                    "Pairing response is not bound to the pending challenge.",
                    PairingStatus.PAIRING_PENDING);
        }

        if (!NetworkIdentityCrypto.verifySignature(
                pending.clientPublicKeySubjectPublicKeyInfoBase64(),
                NetworkIdentityCrypto.canonicalResponseBytes(response),
                response.clientSignatureBase64())) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.SIGNATURE_INVALID,
                    "Client pairing response signature is invalid.",
                    PairingStatus.PAIRING_PENDING);
        }

        MasterTrustDocument updated = markPaired(loaded.document(), pending, clock.instant());
        try {
            trustStore.save(updated);
        } catch (IOException | IllegalArgumentException exception) {
            return MasterPairingCompletionResult.failed(
                    PairingConstants.TRUST_STORE_INVALID,
                    "paired-clients.json could not be written: " + exception.getMessage(),
                    PairingStatus.PAIRING_PENDING);
        }

        return MasterPairingCompletionResult.success();
    }

    public MasterClientAuthorization isClientAuthorized(ClientNetworkIdentityDescriptor client) {
        if (!isValidClientDescriptor(client)) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.UNPAIRED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client public key fingerprint does not match its public key.");
        }

        return storedClientAuthorization(client);
    }

    MasterClientAuthorization isAuthenticatedClientAuthorized(ClientNetworkIdentityDescriptor client) {
        if (!hasDescriptorIdentity(client)) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.UNPAIRED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client Network Identity descriptor is incomplete.");
        }

        return storedClientAuthorization(client);
    }

    private MasterClientAuthorization storedClientAuthorization(ClientNetworkIdentityDescriptor client) {
        if (client == null) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.UNPAIRED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client Network Identity descriptor is required.");
        }

        MasterNetworkIdentityResolution identity = masterIdentityResolver.resolve();
        if (!identity.ready()) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.UNPAIRED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Master Network Identity is unavailable.");
        }

        MasterTrustStoreReadResult read = trustStore.read();
        if (read.status() != MasterTrustStoreReadStatus.LOADED
                || !matchesMaster(read.document(), identity.metadata())) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.UNPAIRED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }

        ClientTrustRecord record = findClient(read.document(), client.clientNetworkIdentityId());
        if (record == null) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.UNPAIRED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }

        if (record.status() == PairingStatus.REVOKED) {
            return MasterClientAuthorization.blocked(
                    PairingStatus.REVOKED,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client pairing has been revoked.");
        }

        if (record.status() != PairingStatus.PAIRED
                || !record.clientInstallationId().equals(client.clientInstallationId())
                || !record.clientPublicKeyFingerprint().equals(client.publicKeyFingerprint())
                || !record.clientPublicKeySubjectPublicKeyInfoBase64().equals(client.subjectPublicKeyInfoBase64())) {
            return MasterClientAuthorization.blocked(
                    record.status(),
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }

        return MasterClientAuthorization.success();
    }

    public List<KnownMasterClient> knownClients() {
        MasterNetworkIdentityResolution identity = masterIdentityResolver.resolve();
        if (!identity.ready()) {
            return List.of();
        }

        MasterTrustStoreReadResult read = trustStore.read();
        if (read.status() != MasterTrustStoreReadStatus.LOADED
                || !matchesMaster(read.document(), identity.metadata())) {
            return List.of();
        }

        return read.document().pairedClients().stream()
                .map(client -> new KnownMasterClient(
                        client.status(),
                        client.clientNetworkIdentityId(),
                        client.clientInstallationId(),
                        client.clientPublicKeyFingerprint(),
                        client.clientPublicKeySubjectPublicKeyInfoBase64(),
                        client.pairedAtUtc(),
                        client.revokedAtUtc()))
                .toList();
    }

    public Optional<KnownMasterClient> knownClient(UUID clientNetworkIdentityId) {
        if (clientNetworkIdentityId == null) {
            return Optional.empty();
        }

        return knownClients().stream()
                .filter(client -> client.clientNetworkIdentityId().equals(clientNetworkIdentityId))
                .findFirst();
    }

    public boolean revokeClient(UUID clientNetworkIdentityId) {
        MasterNetworkIdentityResolution identity = masterIdentityResolver.resolve();
        if (!identity.ready()) {
            return false;
        }

        MasterTrustStoreReadResult read = trustStore.read();
        if (read.status() != MasterTrustStoreReadStatus.LOADED
                || !matchesMaster(read.document(), identity.metadata())) {
            return false;
        }

        List<ClientTrustRecord> clients = new ArrayList<>(read.document().pairedClients());
        int index = findClientIndex(clients, clientNetworkIdentityId);
        if (index < 0 || clients.get(index).pairedAtUtc() == null) {
            return false;
        }

        ClientTrustRecord existing = clients.get(index);
        clients.set(index, new ClientTrustRecord(
                existing.schemaVersion(),
                PairingStatus.REVOKED,
                existing.masterNetworkIdentityId(),
                existing.clientNetworkIdentityId(),
                existing.clientInstallationId(),
                existing.masterPublicKeyFingerprint(),
                existing.clientPublicKeyFingerprint(),
                existing.clientPublicKeySubjectPublicKeyInfoBase64(),
                existing.pairedAtUtc(),
                clock.instant(),
                existing.pairedChallengeId(),
                existing.certificateThumbprint()));

        List<MasterPairingChallengeRecord> challenges = read.document().pairingChallenges().stream()
                .map(challenge -> challenge.clientNetworkIdentityId().equals(clientNetworkIdentityId)
                        && challenge.status() == PairingStatus.PAIRING_PENDING
                        ? new MasterPairingChallengeRecord(
                                challenge.schemaVersion(),
                                PairingStatus.REVOKED,
                                challenge.challengeId(),
                                challenge.masterNetworkIdentityId(),
                                challenge.clientNetworkIdentityId(),
                                challenge.clientInstallationId(),
                                challenge.masterPublicKeyFingerprint(),
                                challenge.clientPublicKeyFingerprint(),
                                challenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                                challenge.nonceBase64(),
                                challenge.issuedAtUtc(),
                                challenge.expiresAtUtc(),
                                challenge.consumedAtUtc())
                        : challenge)
                .toList();

        try {
            trustStore.save(new MasterTrustDocument(
                    read.document().schemaVersion(),
                    read.document().masterNetworkIdentityId(),
                    read.document().masterPublicKeyFingerprint(),
                    clients,
                    challenges));
            return true;
        } catch (IOException | IllegalArgumentException exception) {
            return false;
        }
    }

    private DocumentLoadResult loadOrCreateDocument(MasterNetworkIdentityMetadata masterIdentity) {
        MasterTrustStoreReadResult read = trustStore.read();
        if (read.status() == MasterTrustStoreReadStatus.MISSING) {
            return DocumentLoadResult.loaded(MasterTrustDocument.empty(masterIdentity));
        }
        if (read.status() == MasterTrustStoreReadStatus.INVALID
                || !matchesMaster(read.document(), masterIdentity)) {
            return DocumentLoadResult.invalid(
                    read.errorMessage() == null
                            ? "paired-clients.json belongs to a different Master Network Identity."
                            : read.errorMessage());
        }
        return DocumentLoadResult.loaded(read.document());
    }

    private MasterTrustDocument upsertPending(
            MasterTrustDocument document,
            PairingChallenge challenge) {
        List<ClientTrustRecord> clients = new ArrayList<>(document.pairedClients());
        ClientTrustRecord pendingClient = new ClientTrustRecord(
                PairingConstants.SCHEMA_VERSION,
                PairingStatus.PAIRING_PENDING,
                challenge.masterNetworkIdentityId(),
                challenge.clientNetworkIdentityId(),
                challenge.clientInstallationId(),
                challenge.masterPublicKeyFingerprint(),
                challenge.clientPublicKeyFingerprint(),
                challenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                null,
                null,
                null,
                null);

        int clientIndex = findClientIndex(clients, challenge.clientNetworkIdentityId());
        if (clientIndex >= 0) {
            clients.set(clientIndex, pendingClient);
        } else {
            clients.add(pendingClient);
        }

        List<MasterPairingChallengeRecord> challenges = new ArrayList<>(document.pairingChallenges());
        challenges.add(new MasterPairingChallengeRecord(
                PairingConstants.SCHEMA_VERSION,
                PairingStatus.PAIRING_PENDING,
                challenge.challengeId(),
                challenge.masterNetworkIdentityId(),
                challenge.clientNetworkIdentityId(),
                challenge.clientInstallationId(),
                challenge.masterPublicKeyFingerprint(),
                challenge.clientPublicKeyFingerprint(),
                challenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                challenge.nonceBase64(),
                challenge.issuedAtUtc(),
                challenge.expiresAtUtc(),
                null));

        return new MasterTrustDocument(
                document.schemaVersion(),
                document.masterNetworkIdentityId(),
                document.masterPublicKeyFingerprint(),
                clients,
                challenges);
    }

    private MasterTrustDocument markPaired(
            MasterTrustDocument document,
            MasterPairingChallengeRecord pending,
            Instant pairedAtUtc) {
        List<ClientTrustRecord> clients = new ArrayList<>(document.pairedClients());
        ClientTrustRecord pairedClient = new ClientTrustRecord(
                PairingConstants.SCHEMA_VERSION,
                PairingStatus.PAIRED,
                pending.masterNetworkIdentityId(),
                pending.clientNetworkIdentityId(),
                pending.clientInstallationId(),
                pending.masterPublicKeyFingerprint(),
                pending.clientPublicKeyFingerprint(),
                pending.clientPublicKeySubjectPublicKeyInfoBase64(),
                pairedAtUtc,
                null,
                pending.challengeId(),
                null);
        int clientIndex = findClientIndex(clients, pending.clientNetworkIdentityId());
        if (clientIndex >= 0) {
            clients.set(clientIndex, pairedClient);
        } else {
            clients.add(pairedClient);
        }

        List<MasterPairingChallengeRecord> challenges = document.pairingChallenges().stream()
                .map(challenge -> challenge.challengeId().equals(pending.challengeId())
                        ? new MasterPairingChallengeRecord(
                                challenge.schemaVersion(),
                                PairingStatus.PAIRED,
                                challenge.challengeId(),
                                challenge.masterNetworkIdentityId(),
                                challenge.clientNetworkIdentityId(),
                                challenge.clientInstallationId(),
                                challenge.masterPublicKeyFingerprint(),
                                challenge.clientPublicKeyFingerprint(),
                                challenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                                challenge.nonceBase64(),
                                challenge.issuedAtUtc(),
                                challenge.expiresAtUtc(),
                                pairedAtUtc)
                        : challenge)
                .toList();

        return new MasterTrustDocument(
                document.schemaVersion(),
                document.masterNetworkIdentityId(),
                document.masterPublicKeyFingerprint(),
                clients,
                challenges);
    }

    private boolean matchesPendingChallenge(
            PairingResponse response,
            MasterPairingChallengeRecord pending) {
        return response.schemaVersion() == PairingConstants.SCHEMA_VERSION
                && PairingConstants.PURPOSE.equals(response.purpose())
                && response.challengeId().equals(pending.challengeId())
                && response.masterNetworkIdentityId().equals(pending.masterNetworkIdentityId())
                && response.clientNetworkIdentityId().equals(pending.clientNetworkIdentityId())
                && response.clientInstallationId().equals(pending.clientInstallationId())
                && response.masterPublicKeyFingerprint().equals(pending.masterPublicKeyFingerprint())
                && response.clientPublicKeyFingerprint().equals(pending.clientPublicKeyFingerprint())
                && response.challengeNonceBase64().equals(pending.nonceBase64());
    }

    private boolean isValidResponse(PairingResponse response) {
        return response != null
                && response.schemaVersion() == PairingConstants.SCHEMA_VERSION
                && PairingConstants.PURPOSE.equals(response.purpose())
                && response.challengeId() != null
                && response.masterNetworkIdentityId() != null
                && response.clientNetworkIdentityId() != null
                && response.clientInstallationId() != null
                && NetworkIdentityCrypto.isValidSha256Hex(response.masterPublicKeyFingerprint())
                && NetworkIdentityCrypto.isValidSha256Hex(response.clientPublicKeyFingerprint())
                && NetworkIdentityCrypto.isValidNonce(response.challengeNonceBase64())
                && NetworkIdentityCrypto.isValidNonce(response.responseNonceBase64())
                && response.signedAtUtc() != null
                && response.clientSignatureBase64() != null
                && !response.clientSignatureBase64().isBlank();
    }

    private boolean isValidClientDescriptor(ClientNetworkIdentityDescriptor client) {
        if (!hasDescriptorIdentity(client)) {
            return false;
        }

        try {
            NetworkIdentityCrypto.importPublicKey(client.subjectPublicKeyInfoBase64());
            return client.publicKeyFingerprint().equals(NetworkIdentityCrypto.publicKeyFingerprintBase64(
                    client.subjectPublicKeyInfoBase64()));
        } catch (Exception exception) {
            return false;
        }
    }

    private boolean hasDescriptorIdentity(ClientNetworkIdentityDescriptor client) {
        return client != null
                && client.clientNetworkIdentityId() != null
                && client.clientInstallationId() != null
                && NetworkIdentityCrypto.isValidSha256Hex(client.publicKeyFingerprint())
                && client.subjectPublicKeyInfoBase64() != null
                && !client.subjectPublicKeyInfoBase64().isBlank();
    }

    private boolean matchesMaster(
            MasterTrustDocument document,
            MasterNetworkIdentityMetadata masterIdentity) {
        return document != null
                && document.masterNetworkIdentityId().equals(masterIdentity.masterNetworkIdentityId())
                && document.masterPublicKeyFingerprint().equals(masterIdentity.publicKeyFingerprint());
    }

    private ClientTrustRecord findClient(MasterTrustDocument document, UUID clientNetworkIdentityId) {
        return document.pairedClients().stream()
                .filter(client -> client.clientNetworkIdentityId().equals(clientNetworkIdentityId))
                .findFirst()
                .orElse(null);
    }

    private MasterPairingChallengeRecord findChallenge(
            MasterTrustDocument document,
            UUID challengeId) {
        return document.pairingChallenges().stream()
                .filter(challenge -> challenge.challengeId().equals(challengeId))
                .findFirst()
                .orElse(null);
    }

    private int findClientIndex(List<ClientTrustRecord> clients, UUID clientNetworkIdentityId) {
        for (int index = 0; index < clients.size(); index++) {
            if (clients.get(index).clientNetworkIdentityId().equals(clientNetworkIdentityId)) {
                return index;
            }
        }
        return -1;
    }

    private record DocumentLoadResult(
            boolean valid,
            MasterTrustDocument document,
            String errorMessage) {

        static DocumentLoadResult loaded(MasterTrustDocument document) {
            return new DocumentLoadResult(true, document, null);
        }

        static DocumentLoadResult invalid(String errorMessage) {
            return new DocumentLoadResult(false, null, errorMessage);
        }
    }
}
