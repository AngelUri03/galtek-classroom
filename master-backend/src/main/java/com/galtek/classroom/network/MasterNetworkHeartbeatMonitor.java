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
    public void start() {
        if (running) {
            return;
        }

        long periodSeconds = Math.max(5, properties.getHeartbeatTimeout().toSeconds() / 3);
        executor = Executors.newSingleThreadScheduledExecutor(task ->
                Thread.ofVirtual().name("galtek-master-network-heartbeat-monitor").unstarted(task));
        executor.scheduleWithFixedDelay(
                () -> connectionRegistry.expireTimedOut(properties.getHeartbeatTimeout()),
                periodSeconds,
                periodSeconds,
                TimeUnit.SECONDS);
        running = true;
    }

    @Override
    public void stop() {
        if (executor != null) {
            executor.shutdownNow();
        }
        running = false;
    }

    @Override
    public boolean isRunning() {
        return running;
    }
}
