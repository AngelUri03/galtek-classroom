package com.galtek.classroom.master;

import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import org.springframework.stereotype.Service;

@Service
public class MasterAuthorizationService {

    private final LocalAgentClient localAgentClient;

    public MasterAuthorizationService(LocalAgentClient localAgentClient) {
        this.localAgentClient = localAgentClient;
    }

    public MasterAuthorizationResponse currentAuthorization() {
        return localAgentClient.getMasterAuthorization();
    }
}
