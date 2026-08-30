package com.galtek.classroom.network;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.galtek.classroom.persistence.AtomicFiles;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public class MasterTrustStore {

    private static final JsonMapper OBJECT_MAPPER = JsonMapper.builder()
            .findAndAddModules()
            .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
            .build();

    private final Path dataDirectory;
    private final Path filePath;

    public MasterTrustStore(Path dataDirectory) {
        this.dataDirectory = dataDirectory.toAbsolutePath().normalize();
        this.filePath = this.dataDirectory.resolve(PairingConstants.PAIRED_CLIENTS_FILE_NAME);
    }

    public Path filePath() {
        return filePath;
    }

    public MasterTrustStoreReadResult read() {
        if (!Files.exists(filePath)) {
            return MasterTrustStoreReadResult.missing(filePath.toString());
        }

        try {
            MasterTrustDocument document = OBJECT_MAPPER.readValue(filePath.toFile(), MasterTrustDocument.class);
            if (!MasterTrustValidator.isValid(document)) {
                return MasterTrustStoreReadResult.invalid(
                        filePath.toString(),
                        "paired-clients.json is corrupt or incomplete.");
            }
            return MasterTrustStoreReadResult.loaded(document, filePath.toString());
        } catch (IOException | RuntimeException exception) {
            return MasterTrustStoreReadResult.invalid(
                    filePath.toString(),
                    "paired-clients.json could not be read: " + exception.getMessage());
        }
    }

    public void save(MasterTrustDocument document) throws IOException {
        if (!MasterTrustValidator.isValid(document)) {
            throw new IllegalArgumentException("Cannot persist invalid Master trust document.");
        }

        Files.createDirectories(dataDirectory);
        try {
            AtomicFiles.writeAtomically(
                    filePath,
                    true,
                    output -> OBJECT_MAPPER.writerWithDefaultPrettyPrinter().writeValue(output, document));
        } catch (JsonProcessingException exception) {
            throw new IOException("paired-clients.json could not be serialized.", exception);
        }
    }
}
