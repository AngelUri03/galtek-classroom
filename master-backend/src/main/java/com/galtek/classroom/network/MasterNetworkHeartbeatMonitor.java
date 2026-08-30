package com.galtek.classroom.network;

import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import org.springframework.context.SmartLifecycle;

public class MasterNetworkHeartbeatMonitor implements SmartLifecycle {

    private final ClientConnectionRegistry connectionRegistry;
    private final MasterNetworkGrpcProperties properties;
    private ScheduledExecutorService executor;
    private volatile boolean running;

    public MasterNetworkHeartbeatMonitor(
            ClientConnectionRegistry connectionRegistry,
            MasterNetworkGrpcProperties properties) {
        this.connectionRegistry = connectionRegistry;
        this.properties = properties;
    }

    @Override
    public synchronized void start() {
        if (running) {
            return;
        }

        running = true;
        ensureScanning();
    }

    public synchronized void ensureScanning() {
        if (!running || isScanning() || !connectionRegistry.hasActiveConnections()) {
            return;
        }

        long periodSeconds = Math.max(5, properties.getHeartbeatTimeout().toSeconds() / 3);
        executor = Executors.newSingleThreadScheduledExecutor(task ->
                Thread.ofVirtual().name("galtek-master-network-heartbeat-monitor").unstarted(task));
        executor.scheduleWithFixedDelay(
                this::expireAndPauseIfIdle,
                periodSeconds,
                periodSeconds,
                TimeUnit.SECONDS);
    }

    @Override
    public synchronized void stop() {
        shutdownExecutor();
        running = false;
    }

    @Override
    public boolean isRunning() {
        return running;
    }

    private void expireAndPauseIfIdle() {
        connectionRegistry.expireTimedOut(properties.getHeartbeatTimeout());
        if (!connectionRegistry.hasActiveConnections()) {
            synchronized (this) {
                if (!connectionRegistry.hasActiveConnections()) {
                    shutdownExecutor();
                }
            }
        }
    }

    private boolean isScanning() {
        return executor != null && !executor.isShutdown();
    }

    private void shutdownExecutor() {
        if (executor != null) {
            executor.shutdownNow();
            executor = null;
        }
    }
}
