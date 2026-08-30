package com.galtek.classroom.recovery;

import com.galtek.classroom.performance.ResourceWorkClass;

public class StartupWorkPolicy {

    public boolean allowedDuringControlPlaneStartup(ResourceWorkClass workClass) {
        return workClass == ResourceWorkClass.CONTROL_CRITICAL;
    }

    public boolean deferredDuringRecovery(ResourceWorkClass workClass) {
        return workClass == ResourceWorkClass.VISUAL
                || workClass == ResourceWorkClass.BACKGROUND
                || workClass == ResourceWorkClass.TRANSFER;
    }

    public boolean automaticCaptureAllowed(StartupEvent event) {
        return false;
    }
}
