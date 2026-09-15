package com.galtek.classroom.network;

import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.galtek.classroom.persistence.AtomicFiles;
import java.io.IOException;
import java.nio.file.Path;
import java.time.Instant;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class PairingFileBootstrapService {

    private static final String OPERATION_CREATE_CHALLENGE = "PAIRING_CREATE_CHALLENGE";
    private static final String OPERATION_COMPLETE = "PAIRING_COMPLETE";

    private static final JsonMapper OBJECT_MAPPER = JsonMapper.builder()
            .findAndAddModules()
            .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
            .serializationInclusion(JsonInclude.Include.NON_NULL)
            .build();

    private final MasterPairingService pairingService;

    public PairingFileBootstrapService(MasterPairingService pairingService) {
        this.pairingService = pairingService;
    }

    public PairingFileBootstrapResult createChallenge(
            String descriptorInputPath,
            String challengeOutputPath,
            boolean explicitIntent) {
        ClientPairingDescriptor descriptor;
        try {
            descriptor = OBJECT_MAPPER.readValue(
                    Path.of(descriptorInputPath).toAbsolutePath().normalize().toFile(),
                    ClientPairingDescriptor.class);
        } catch (IOException | RuntimeException exception) {
            return PairingFileBootstrapResult.failed(
                    OPERATION_CREATE_CHALLENGE,
                    PairingConstants.CHALLENGE_INVALID,
                    "Client pairing descriptor file could not be read: " + exception.getMessage(),
                    null);
        }

        if (!isValidDescriptorEnvelope(descriptor)) {
            return PairingFileBootstrapResult.failed(
                    OPERATION_CREATE_CHALLENGE,
                    PairingConstants.FINGERPRINT_MISMATCH,
                    "Client pairing descriptor is malformed.",
                    null);
        }

        ClientNetworkIdentityDescriptor client = new ClientNetworkIdentityDescriptor(
                descriptor.networkIdentityId(),
                descriptor.installationId(),
                descriptor.publicKeyFingerprint(),
                descriptor.publicKeySubjectPublicKeyInfoBase64());
        MasterPairingChallengeResult result = pairingService.createPairingChallenge(client, explicitIntent);
        if (!result.created()) {
            return PairingFileBootstrapResult.failed(
                    OPERATION_CREATE_CHALLENGE,
                    result.errorCode(),
                    result.errorMessage(),
                    null);
        }

        try {
            AtomicFiles.writeAtomically(
                    Path.of(challengeOutputPath).toAbsolutePath().normalize(),
                    true,
                    output -> OBJECT_MAPPER.writerWithDefaultPrettyPrinter().writeValue(output, result.challenge()));
        } catch (IOException | RuntimeException exception) {
            return PairingFileBootstrapResult.failed(
                    OPERATION_CREATE_CHALLENGE,
                    PairingConstants.TRUST_STORE_INVALID,
                    "Pairing challenge file could not be written: " + exception.getMessage(),
                    null);
        }

        return PairingFileBootstrapResult.challengeCreated(
                Path.of(challengeOutputPath).toAbsolutePath().normalize().toString(),
                result.challenge());
    }

    public PairingFileBootstrapResult completePairing(String responseInputPath) {
        PairingResponse response;
        try {
            response = OBJECT_MAPPER.readValue(
                    Path.of(responseInputPath).toAbsolutePath().normalize().toFile(),
                    PairingResponse.class);
        } catch (IOException | RuntimeException exception) {
            return PairingFileBootstrapResult.failed(
                    OPERATION_COMPLETE,
                    PairingConstants.CHALLENGE_INVALID,
                    "Pairing response file could not be read: " + exception.getMessage(),
                    PairingStatus.UNPAIRED);
        }

        MasterPairingCompletionResult result = pairingService.completePairing(response);
        if (!result.paired()) {
            return PairingFileBootstrapResult.failed(
                    OPERATION_COMPLETE,
                    result.errorCode(),
                    result.errorMessage(),
                    result.status());
        }

        return PairingFileBootstrapResult.pairingCompleted(response);
    }

    public String serializeResult(PairingFileBootstrapResult result) throws IOException {
        return OBJECT_MAPPER.writerWithDefaultPrettyPrinter().writeValueAsString(result);
    }

    private static boolean isValidDescriptorEnvelope(ClientPairingDescriptor descriptor) {
        return descriptor != null
                && descriptor.schemaVersion() == PairingConstants.SCHEMA_VERSION
                && PairingConstants.CLIENT_DESCRIPTOR_PURPOSE.equals(descriptor.purpose())
                && descriptor.installationId() != null
                && descriptor.networkIdentityId() != null
                && NetworkIdentityCrypto.isValidSha256Hex(descriptor.publicKeyFingerprint())
                && descriptor.publicKeySubjectPublicKeyInfoBase64() != null
                && !descriptor.publicKeySubjectPublicKeyInfoBase64().isBlank();
    }
}

record ClientPairingDescriptor(
        int schemaVersion,
        String purpose,
        java.util.UUID installationId,
        java.util.UUID networkIdentityId,
        String publicKeyFingerprint,
        String publicKeySubjectPublicKeyInfoBase64,
        Instant exportedAtUtc) {
}
