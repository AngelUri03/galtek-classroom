package com.galtek.classroom.performance;

import com.galtek.classroom.operations.OperationPriority;

public enum ResourceWorkClass {
    CONTROL_CRITICAL(OperationPriority.CRITICAL),
    CLASS_PREPARATION(OperationPriority.HIGH),
    INTERACTIVE(OperationPriority.NORMAL),
    TRANSFER(OperationPriority.NORMAL),
    VISUAL(OperationPriority.LOW),
    BACKGROUND(OperationPriority.LOW);

    private final OperationPriority priorityFloor;

    ResourceWorkClass(OperationPriority priorityFloor) {
        this.priorityFloor = priorityFloor;
    }

    public OperationPriority priorityFloor() {
        return priorityFloor;
    }
}
