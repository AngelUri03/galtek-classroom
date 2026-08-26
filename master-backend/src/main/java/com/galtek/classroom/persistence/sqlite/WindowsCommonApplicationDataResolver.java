package com.galtek.classroom.persistence.sqlite;

import java.nio.file.Path;
import java.util.function.Function;

public class WindowsCommonApplicationDataResolver implements CommonApplicationDataResolver {

    private final Function<String, String> environment;

    public WindowsCommonApplicationDataResolver() {
        this(System::getenv);
    }

    WindowsCommonApplicationDataResolver(Function<String, String> environment) {
        this.environment = environment;
    }

    @Override
    public Path resolve() {
        String programData = nonBlank(environment.apply("ProgramData"));
        if (programData != null) {
            return Path.of(programData);
        }

        String allUsersProfile = nonBlank(environment.apply("ALLUSERSPROFILE"));
        if (allUsersProfile != null) {
            return Path.of(allUsersProfile);
        }

        throw new IllegalStateException("Windows CommonApplicationData could not be resolved.");
    }

    private static String nonBlank(String value) {
        if (value == null || value.isBlank()) {
            return null;
        }

        return value;
    }
}
