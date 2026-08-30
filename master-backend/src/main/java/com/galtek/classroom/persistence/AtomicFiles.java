package com.galtek.classroom.persistence;

import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.channels.FileChannel;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;
import java.util.UUID;

public final class AtomicFiles {

    private AtomicFiles() {
    }

    public static void writeAtomically(
            Path target,
            boolean replaceExisting,
            OutputWriter writer) throws IOException {
        Path normalizedTarget = target.toAbsolutePath().normalize();
        Path directory = normalizedTarget.getParent();
        if (directory == null) {
            throw new IOException("Target path has no parent directory.");
        }

        Files.createDirectories(directory);
        Path tempPath = normalizedTarget.resolveSibling(
                normalizedTarget.getFileName() + "." + UUID.randomUUID() + ".tmp");

        try {
            try (FileOutputStream output = new FileOutputStream(tempPath.toFile())) {
                writer.write(new NonClosingOutputStream(output));
                output.flush();
                output.getFD().sync();
            }

            if (replaceExisting) {
                Files.move(
                        tempPath,
                        normalizedTarget,
                        StandardCopyOption.REPLACE_EXISTING,
                        StandardCopyOption.ATOMIC_MOVE);
            } else {
                Files.move(tempPath, normalizedTarget, StandardCopyOption.ATOMIC_MOVE);
            }
            forceDirectory(directory);
        } finally {
            Files.deleteIfExists(tempPath);
        }
    }

    public static void writeBytesAtomically(
            Path target,
            byte[] content,
            boolean replaceExisting) throws IOException {
        writeAtomically(target, replaceExisting, output -> output.write(content));
    }

    private static void forceDirectory(Path directory) {
        try (FileChannel channel = FileChannel.open(directory, StandardOpenOption.READ)) {
            channel.force(true);
        } catch (IOException ignored) {
            // Some filesystems do not allow opening directories. The data file itself is already fsynced.
        }
    }

    @FunctionalInterface
    public interface OutputWriter {
        void write(OutputStream output) throws IOException;
    }

    private static final class NonClosingOutputStream extends OutputStream {
        private final OutputStream delegate;

        private NonClosingOutputStream(OutputStream delegate) {
            this.delegate = delegate;
        }

        @Override
        public void write(int value) throws IOException {
            delegate.write(value);
        }

        @Override
        public void write(byte[] bytes) throws IOException {
            delegate.write(bytes);
        }

        @Override
        public void write(byte[] bytes, int offset, int length) throws IOException {
            delegate.write(bytes, offset, length);
        }

        @Override
        public void flush() throws IOException {
            delegate.flush();
        }

        @Override
        public void close() throws IOException {
            delegate.flush();
        }
    }
}
