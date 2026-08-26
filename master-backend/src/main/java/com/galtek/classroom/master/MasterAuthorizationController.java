package com.galtek.classroom.master;

import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/master")
public class MasterAuthorizationController {

    private final MasterAuthorizationService masterAuthorizationService;

    public MasterAuthorizationController(MasterAuthorizationService masterAuthorizationService) {
        this.masterAuthorizationService = masterAuthorizationService;
    }

    @GetMapping("/authorization")
    public MasterAuthorizationResponse authorization() {
        return masterAuthorizationService.currentAuthorization();
    }
}
