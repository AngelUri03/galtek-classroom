package com.galtek.classroom.persistence.sqlite;

import java.nio.file.Path;

public record MasterDatabasePath(
        Path dataDirectory,
        Path databaseFile) {
}
