package com.galtek.classroom.performance;

import java.lang.management.ManagementFactory;
import java.lang.management.MemoryUsage;
import java.time.Duration;
import java.time.Instant;

public record RuntimeDiagnosticsSnapshot(
        String product,
        String component,
        String capturedAtUtc,
        String samplingMode,
        long heapUsedBytes,
        long heapCommittedBytes,
        long nonHeapUsedBytes,
        long nonHeapCommittedBytes,
        int liveThreadCount,
        long uptimeMs,
        String uptime) {

    private static final String PRODUCT_CODE = "GALTEK_CLASSROOM";
    private static final String ON_DEMAND = "ON_DEMAND";

    public static RuntimeDiagnosticsSnapshot capture(String component) {
        var memory = ManagementFactory.getMemoryMXBean();
        var threads = ManagementFactory.getThreadMXBean();
        var runtime = ManagementFactory.getRuntimeMXBean();
        MemoryUsage heap = memory.getHeapMemoryUsage();
        MemoryUsage nonHeap = memory.getNonHeapMemoryUsage();
        long uptimeMs = Math.max(0, runtime.getUptime());

        return new RuntimeDiagnosticsSnapshot(
                PRODUCT_CODE,
                component == null || component.isBlank() ? "Galtek Classroom Master Backend" : component.trim(),
                Instant.now().toString(),
                ON_DEMAND,
                nonNegative(heap.getUsed()),
                nonNegative(heap.getCommitted()),
                nonNegative(nonHeap.getUsed()),
                nonNegative(nonHeap.getCommitted()),
                threads.getThreadCount(),
                uptimeMs,
                Duration.ofMillis(uptimeMs).toString());
    }

    private static long nonNegative(long value) {
        return Math.max(0, value);
    }
}
