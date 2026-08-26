package com.galtek.classroom.persistence.sqlite;

import java.nio.file.Path;
import java.util.function.Function;

public class MasterDataDirectoryResolver {

    public static final String ENVIRONMENT_OVERRIDE = "GALTEK_CLASSROOM_MASTER_DATA_DIR";

    private final CommonApplicationDataResolver commonApplicationDataResolver;
    private final Function<String, String> environment;

    public MasterDataDirectoryResolver(CommonApplicationDataResolver commonApplicationDataResolver) {
        this(commonApplicationDataResolver, System::getenv);
    }

    MasterDataDirectoryResolver(
            CommonApplicationDataResolver commonApplicationDataResolver,
            Function<String, String> environment) {
        this.commonApplicationDataResolver = commonApplicationDataResolver;
        this.environment = environment;
    }

    public Path resolve(String configuredDataDir) {
        if (configuredDataDir != null && !configuredDataDir.isBlank()) {
            return Path.of(configuredDataDir).toAbsolutePath().normalize();
        }

        String environmentOverride = environment.apply(ENVIRONMENT_OVERRIDE);
        if (environmentOverride != null && !environmentOverride.isBlank()) {
            return Path.of(environmentOverride).toAbsolutePath().normalize();
        }

        return commonApplicationDataResolver.resolve()
                .resolve("Galtek")
                .resolve("Classroom")
                .resolve("Master")
                .toAbsolutePath()
                .normalize();
    }
}
