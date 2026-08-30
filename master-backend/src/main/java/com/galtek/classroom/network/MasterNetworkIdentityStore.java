package com.galtek.classroom.network;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.galtek.classroom.persistence.AtomicFiles;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public class MasterNetworkIdentityStore {

    private static final JsonMapper OBJECT_MAPPER = JsonMapper.builder()
            .findAndAddModules()
            .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
            .build();

    private final Path dataDirectory;
    private final Path filePath;

    public MasterNetworkIdentityStore(Path dataDirectory) {
        this.dataDirectory = dataDirectory.toAbsolutePath().normalize();
        this.filePath = this.dataDirectory.resolve(PairingConstants.MASTER_NETWORK_IDENTITY_FILE_NAME);
    }

    public Path filePath() {
        return filePath;
    }

    public MasterNetworkIdentityStoreReadResult read() {
        if (!Files.exists(filePath)) {
            return MasterNetworkIdentityStoreReadResult.missing(filePath.toString());
        }

        try {
            MasterNetworkIdentityMetadata metadata = OBJECT_MAPPER.readValue(
                    filePath.toFile(),
                    MasterNetworkIdentityMetadata.class);
            if (!MasterNetworkIdentityValidator.isValid(metadata, true)) {
                return MasterNetworkIdentityStoreReadResult.invalid(
                        filePath.toString(),
                        "master-network-identity.json is corrupt or incomplete.");
            }
            return MasterNetworkIdentityStoreReadResult.loaded(metadata, filePath.toString());
        } catch (IOException | RuntimeException exception) {
            return MasterNetworkIdentityStoreReadResult.invalid(
                    filePath.toString(),
                    "master-network-identity.json could not be read: " + exception.getMessage());
        }
    }

    public void writeNew(MasterNetworkIdentityMetadata metadata) throws IOException {
        if (!MasterNetworkIdentityValidator.isValid(metadata, true)) {
            throw new IllegalArgumentException("Cannot persist invalid Master Network Identity metadata.");
        }

        Files.createDirectories(dataDirectory);
        if (Files.exists(filePath)) {
            throw new IOException("master-network-identity.json already exists; refusing to overwrite it.");
        }

        try {
            AtomicFiles.writeAtomically(
                    filePath,
                    false,
                    output -> OBJECT_MAPPER.writerWithDefaultPrettyPrinter().writeValue(output, metadata));
        } catch (JsonProcessingException exception) {
            throw new IOException("master-network-identity.json could not be serialized.", exception);
        }
    }
}
