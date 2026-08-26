package com.galtek.classroom.master;

import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import org.springframework.stereotype.Component;

@Component
public class MasterAccessGuard {

    private final MasterAuthorizationService masterAuthorizationService;

    public MasterAccessGuard(MasterAuthorizationService masterAuthorizationService) {
        this.masterAuthorizationService = masterAuthorizationService;
    }

    public MasterAuthorizationResponse requireAuthorized() {
        MasterAuthorizationResponse authorization = masterAuthorizationService.currentAuthorization();

        if (!authorization.authorized()) {
            throw new MasterAccessDeniedException(authorization.status());
        }

        return authorization;
    }
}
