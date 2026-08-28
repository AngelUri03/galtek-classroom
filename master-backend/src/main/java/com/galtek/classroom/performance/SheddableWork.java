package com.galtek.classroom.performance;

public enum SheddableWork {
    PREFETCH(ResourceWorkClass.BACKGROUND),
    NON_ESSENTIAL_INVENTORY(ResourceWorkClass.BACKGROUND),
    THUMBNAILS(ResourceWorkClass.VISUAL),
    PREVIEW_QUALITY_OR_FPS(ResourceWorkClass.VISUAL),
    NON_URGENT_TRANSFER(ResourceWorkClass.TRANSFER),
    BACKGROUND_JOB(ResourceWorkClass.BACKGROUND);

    private final ResourceWorkClass workClass;

    SheddableWork(ResourceWorkClass workClass) {
        this.workClass = workClass;
    }

    public ResourceWorkClass workClass() {
        return workClass;
    }
}
