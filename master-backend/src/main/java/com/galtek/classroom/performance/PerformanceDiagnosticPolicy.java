package com.galtek.classroom.performance;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record PerformanceDiagnosticPolicy(
        DiagnosticSamplingMode samplingMode,
        boolean continuousCollectionAllowed,
        boolean persistedTelemetryAllowed,
        boolean sentWithHeartbeatAllowed) {

    public PerformanceDiagnosticPolicy {
        requireNonNull(samplingMode, "samplingMode");
    }

    public static PerformanceDiagnosticPolicy onDemandOnly() {
        return new PerformanceDiagnosticPolicy(
                DiagnosticSamplingMode.ON_DEMAND,
                false,
                false,
                false);
    }
}
