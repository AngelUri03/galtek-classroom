package com.galtek.classroom.network;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;

@ConfigurationProperties(prefix = "galtek.classroom.master.network.grpc")
public class MasterNetworkGrpcProperties {

    private boolean enabled;
    private int port = 9443;
    private Duration heartbeatTimeout = Duration.ofSeconds(45);
    private int maxInboundMessageSize = 64 * 1024;

    public boolean isEnabled() {
        return enabled;
    }

    public void setEnabled(boolean enabled) {
        this.enabled = enabled;
    }

    public int getPort() {
        return port;
    }

    public void setPort(int port) {
        this.port = port;
    }

    public Duration getHeartbeatTimeout() {
        return heartbeatTimeout;
    }

    public void setHeartbeatTimeout(Duration heartbeatTimeout) {
        if (heartbeatTimeout != null && !heartbeatTimeout.isNegative() && !heartbeatTimeout.isZero()) {
            this.heartbeatTimeout = heartbeatTimeout;
        }
    }

    public int getMaxInboundMessageSize() {
        return maxInboundMessageSize;
    }

    public void setMaxInboundMessageSize(int maxInboundMessageSize) {
        this.maxInboundMessageSize = Math.min(Math.max(maxInboundMessageSize, 1024), 1024 * 1024);
    }
}
