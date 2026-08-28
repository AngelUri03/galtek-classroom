package com.galtek.classroom.network;

import com.galtek.classroom.network.v1.ClientHello;

public interface NetworkClientConnectionService {

    RegisteredNetworkDevice recordAcceptedHello(
            ClientNetworkIdentityDescriptor descriptor,
            ClientHello hello);
}
