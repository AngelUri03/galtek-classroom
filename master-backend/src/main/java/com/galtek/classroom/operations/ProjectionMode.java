package com.galtek.classroom.operations;

public enum ProjectionMode {
    SCREEN_SHARE(true, false, false),
    WHITEBOARD(false, true, false),
    POINTER(false, true, false),
    LOCAL_MEDIA(false, false, true),
    OPEN_WEB_CONTENT(false, false, true);

    private final boolean streamsScreenFrames;
    private final boolean lightweightEvents;
    private final boolean prefersLocalClientExecution;

    ProjectionMode(
            boolean streamsScreenFrames,
            boolean lightweightEvents,
            boolean prefersLocalClientExecution) {
        this.streamsScreenFrames = streamsScreenFrames;
        this.lightweightEvents = lightweightEvents;
        this.prefersLocalClientExecution = prefersLocalClientExecution;
    }

    public boolean streamsScreenFrames() {
        return streamsScreenFrames;
    }

    public boolean lightweightEvents() {
        return lightweightEvents;
    }

    public boolean prefersLocalClientExecution() {
        return prefersLocalClientExecution;
    }
}
