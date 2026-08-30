package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;

import java.lang.reflect.Field;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import java.util.concurrent.ScheduledExecutorService;
import org.junit.jupiter.api.Test;

class MasterNetworkHeartbeatMonitorTest {

    @Test
    void schedulerStartsOnlyWhenConnectionsAreActive() throws Exception {
        ClientConnectionRegistry registry = new ClientConnectionRegistry(
                Clock.fixed(Instant.parse("2026-08-30T00:00:00Z"), ZoneOffset.UTC));
        MasterNetworkHeartbeatMonitor monitor = new MasterNetworkHeartbeatMonitor(
                registry,
                new MasterNetworkGrpcProperties());

        try {
            monitor.start();

            assertThat(executorFrom(monitor)).isNull();

            registry.markOnline(UUID.randomUUID(), "connection-1");
            monitor.ensureScanning();

            assertThat(executorFrom(monitor)).isNotNull();
        } finally {
            monitor.stop();
        }
    }

    private static ScheduledExecutorService executorFrom(MasterNetworkHeartbeatMonitor monitor)
            throws ReflectiveOperationException {
        Field executor = MasterNetworkHeartbeatMonitor.class.getDeclaredField("executor");
        executor.setAccessible(true);
        return (ScheduledExecutorService) executor.get(monitor);
    }
}
