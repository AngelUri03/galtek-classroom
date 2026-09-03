package com.galtek.classroom.master;

import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterUnlockAuthorizationResponse;
import org.springframework.stereotype.Component;

@Component
public class MasterUnlockAccessGuard {

    private final LocalAgentClient localAgentClient;

    public MasterUnlockAccessGuard(LocalAgentClient localAgentClient) {
        this.localAgentClient = localAgentClient;
    }

    /**
     * For recovery-safe actions that reduce control only. Current allowed use: UNLOCK_INPUT.
     */
    public MasterUnlockAuthorizationResponse requireUnlockAuthorized() {
        MasterUnlockAuthorizationResponse authorization = localAgentClient.getMasterUnlockAuthorization();

        if (!authorization.authorized()) {
            throw new MasterAccessDeniedException(authorization.status());
        }

        return authorization;
    }
}
