package com.galtek.classroom.persistence.sqlite;

import org.springframework.boot.context.properties.ConfigurationProperties;

@ConfigurationProperties(prefix = "galtek.classroom.master.storage")
public class MasterStorageProperties {

    private boolean enabled = true;
    private String dataDir = "";
    private String databaseFileName = "classroom.db";
    private int busyTimeoutMs = 5000;
    private int maximumPoolSize = 4;
    private boolean quickCheckOnStartup = true;

    public boolean isEnabled() {
        return enabled;
    }

    public void setEnabled(boolean enabled) {
        this.enabled = enabled;
    }

    public String getDataDir() {
        return dataDir;
    }

    public void setDataDir(String dataDir) {
        this.dataDir = dataDir;
    }

    public String getDatabaseFileName() {
        return databaseFileName;
    }

    public void setDatabaseFileName(String databaseFileName) {
        this.databaseFileName = databaseFileName;
    }

    public int getBusyTimeoutMs() {
        return busyTimeoutMs;
    }

    public void setBusyTimeoutMs(int busyTimeoutMs) {
        this.busyTimeoutMs = busyTimeoutMs;
    }

    public int getMaximumPoolSize() {
        return maximumPoolSize;
    }

    public void setMaximumPoolSize(int maximumPoolSize) {
        this.maximumPoolSize = maximumPoolSize;
    }

    public boolean isQuickCheckOnStartup() {
        return quickCheckOnStartup;
    }

    public void setQuickCheckOnStartup(boolean quickCheckOnStartup) {
        this.quickCheckOnStartup = quickCheckOnStartup;
    }
}
