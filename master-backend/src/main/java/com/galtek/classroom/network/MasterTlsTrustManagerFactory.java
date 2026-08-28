package com.galtek.classroom.network;

import io.grpc.netty.shaded.io.netty.handler.ssl.util.SimpleTrustManagerFactory;
import java.security.KeyStore;
import javax.net.ssl.ManagerFactoryParameters;
import javax.net.ssl.TrustManager;

public final class MasterTlsTrustManagerFactory extends SimpleTrustManagerFactory {

    private final TrustManager trustManager;

    public MasterTlsTrustManagerFactory(MasterTrustStore trustStore) {
        this.trustManager = new MasterTlsPeerTrustManager(trustStore);
    }

    @Override
    protected void engineInit(KeyStore keyStore) {
    }

    @Override
    protected void engineInit(ManagerFactoryParameters managerFactoryParameters) {
    }

    @Override
    protected TrustManager[] engineGetTrustManagers() {
        return new TrustManager[] {trustManager};
    }
}
