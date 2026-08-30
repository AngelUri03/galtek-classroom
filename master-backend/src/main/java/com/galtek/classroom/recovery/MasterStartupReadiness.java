package com.galtek.classroom.recovery;

public record MasterStartupReadiness(
        boolean processAlive,
        boolean storageReady,
        int onlineClientCount) {

    public MasterStartupReadiness {
        if (onlineClientCount < 0) {
            throw new IllegalArgumentException("onlineClientCount cannot be negative.");
        }
    }

    public boolean controlPlaneReady() {
        return processAlive && storageReady;
    }

    public boolean waitsForClients() {
        return false;
    }
}
