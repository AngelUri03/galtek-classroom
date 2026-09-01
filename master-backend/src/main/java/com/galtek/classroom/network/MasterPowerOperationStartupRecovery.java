package com.galtek.classroom.network;

import com.galtek.classroom.recovery.MasterRunMarker;
import java.time.Instant;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.core.annotation.Order;
import org.springframework.stereotype.Component;

@Component
@Order(1)
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterPowerOperationStartupRecovery implements ApplicationRunner {

    private final PowerOperationReconciliationService reconciliationService;
    private final MasterRunMarker runMarker;

    public MasterPowerOperationStartupRecovery(
            PowerOperationReconciliationService reconciliationService,
            MasterRunMarker runMarker) {
        this.reconciliationService = reconciliationService;
        this.runMarker = runMarker;
    }

    @Override
    public void run(ApplicationArguments args) {
        Instant recoveryCutoffUtc = runMarker.state().startedAtUtc();
        reconciliationService.recoverOrphanedPendingPowerTargets(recoveryCutoffUtc);
    }
}
