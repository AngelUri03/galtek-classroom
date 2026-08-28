package com.galtek.classroom.performance;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.util.List;
import java.util.Set;

public final class LoadSheddingPolicy {

    private static final List<SheddableWork> SHEDDING_ORDER = List.of(
            SheddableWork.PREFETCH,
            SheddableWork.NON_ESSENTIAL_INVENTORY,
            SheddableWork.THUMBNAILS,
            SheddableWork.PREVIEW_QUALITY_OR_FPS,
            SheddableWork.NON_URGENT_TRANSFER,
            SheddableWork.BACKGROUND_JOB);

    private static final Set<ResourceWorkClass> PROTECTED_WORK_CLASSES = Set.of(
            ResourceWorkClass.CONTROL_CRITICAL,
            ResourceWorkClass.CLASS_PREPARATION);

    public List<SheddableWork> sheddingOrder() {
        return SHEDDING_ORDER;
    }

    public boolean canShed(ResourceWorkClass workClass) {
        requireNonNull(workClass, "workClass");

        return !PROTECTED_WORK_CLASSES.contains(workClass);
    }

    public boolean protects(ResourceWorkClass workClass) {
        requireNonNull(workClass, "workClass");

        return PROTECTED_WORK_CLASSES.contains(workClass);
    }
}
