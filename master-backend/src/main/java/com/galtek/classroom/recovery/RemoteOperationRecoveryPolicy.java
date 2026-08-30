package com.galtek.classroom.recovery;

import com.galtek.classroom.operations.TargetExecutionStatus;

public class RemoteOperationRecoveryPolicy {

    public RemoteOperationRecoveryDecision decide(
            RemoteOperationDeliveryState deliveryState,
            TargetExecutionStatus confirmedStatus) {
        if (deliveryState != RemoteOperationDeliveryState.RESULT_CONFIRMED) {
            return RemoteOperationRecoveryDecision.RECONCILE_REQUIRED;
        }

        if (confirmedStatus == TargetExecutionStatus.SUCCESS
                || confirmedStatus == TargetExecutionStatus.NO_CHANGE) {
            return RemoteOperationRecoveryDecision.CONFIRMED_SUCCESS;
        }

        return RemoteOperationRecoveryDecision.CONFIRMED_FAILURE;
    }
}
