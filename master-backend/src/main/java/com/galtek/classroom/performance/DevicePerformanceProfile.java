package com.galtek.classroom.performance;

public enum DevicePerformanceProfile {
    LEGACY,
    STANDARD;

    public static DevicePerformanceProfile defaultForUnknownClient() {
        return LEGACY;
    }

    public static DevicePerformanceProfile configuredOrDefault(DevicePerformanceProfile configuredProfile) {
        return configuredProfile == null ? defaultForUnknownClient() : configuredProfile;
    }
}
