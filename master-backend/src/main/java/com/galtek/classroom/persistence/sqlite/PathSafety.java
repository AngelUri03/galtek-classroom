package com.galtek.classroom.persistence.sqlite;

public final class PathSafety {

    private PathSafety() {
    }

    public static void requireSimpleFileName(String fileName) {
        if (fileName == null || fileName.isBlank()
                || fileName.contains("/")
                || fileName.contains("\\")
                || fileName.contains("..")) {
            throw new IllegalArgumentException("databaseFileName must be a simple file name.");
        }
    }
}
