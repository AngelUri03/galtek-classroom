package com.galtek.classroom.localagent;

import jakarta.annotation.PreDestroy;
import java.io.Closeable;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.RandomAccessFile;
import java.nio.channels.Channels;
import java.time.Duration;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicReference;
import org.springframework.stereotype.Component;

@Component
public class WindowsNamedPipeTransport implements LocalIpcTransport {

    private static final Duration EXCHANGE_TIMEOUT = Duration.ofSeconds(2);

    private final ExecutorService executorService;
    private final String pipePath;

    public WindowsNamedPipeTransport() {
        this(LocalIpcProtocol.PIPE_PATH, Executors.newCachedThreadPool(new DaemonThreadFactory()));
    }

    WindowsNamedPipeTransport(String pipePath, ExecutorService executorService) {
        this.pipePath = pipePath;
        this.executorService = executorService;
    }

    @Override
    public String exchange(String requestJson) {
        AtomicReference<Closeable> pipeReference = new AtomicReference<>();
        Future<String> future = executorService.submit(() -> exchangeBlocking(requestJson, pipeReference));

        try {
            return future.get(EXCHANGE_TIMEOUT.toMillis(), TimeUnit.MILLISECONDS);
        } catch (TimeoutException exception) {
            closeQuietly(pipeReference.get());
            future.cancel(true);
            throw new LocalAgentUnavailableException("Local Agent Service IPC timed out.", exception);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            closeQuietly(pipeReference.get());
            future.cancel(true);
            throw new LocalAgentUnavailableException("Local Agent Service IPC was interrupted.", exception);
        } catch (ExecutionException exception) {
            Throwable cause = exception.getCause();

            if (cause instanceof LocalAgentProtocolException protocolException) {
                throw protocolException;
            }

            if (cause instanceof LocalAgentUnavailableException unavailableException) {
                throw unavailableException;
            }

            if (cause instanceof IOException ioException) {
                throw new LocalAgentUnavailableException("Local Agent Service IPC failed.", ioException);
            }

            throw new LocalAgentUnavailableException("Local Agent Service IPC failed.", exception);
        }
    }

    @PreDestroy
    public void shutdown() {
        executorService.shutdownNow();
    }

    private String exchangeBlocking(
            String requestJson,
            AtomicReference<Closeable> pipeReference) {
        try (RandomAccessFile pipe = new RandomAccessFile(pipePath, "rw")) {
            pipeReference.set(pipe);
            OutputStream outputStream = Channels.newOutputStream(pipe.getChannel());
            InputStream inputStream = Channels.newInputStream(pipe.getChannel());
            LocalIpcFraming.writeJson(outputStream, requestJson);
            return LocalIpcFraming.readJson(inputStream);
        } catch (FileNotFoundException exception) {
            throw new LocalAgentUnavailableException("Local Agent Service pipe is not available.", exception);
        } catch (LocalIpcFrameException exception) {
            throw new LocalAgentProtocolException(
                    LocalIpcProtocol.ERROR_MALFORMED_RESPONSE,
                    "Local Agent Service returned a malformed IPC frame.",
                    exception);
        } catch (IOException exception) {
            throw new LocalAgentUnavailableException("Local Agent Service IPC failed.", exception);
        }
    }

    private static void closeQuietly(Closeable closeable) {
        if (closeable == null) {
            return;
        }

        try {
            closeable.close();
        } catch (IOException ignored) {
        }
    }

    private static final class DaemonThreadFactory implements ThreadFactory {

        @Override
        public Thread newThread(Runnable runnable) {
            Thread thread = new Thread(runnable, "galtek-local-agent-ipc");
            thread.setDaemon(true);
            return thread;
        }
    }
}
