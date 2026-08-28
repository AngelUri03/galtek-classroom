package com.galtek.classroom.operations;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class ProjectionModeTest {

    @Test
    void projectionModesDoNotTreatEveryModeAsScreenFrames() {
        assertThat(ProjectionMode.SCREEN_SHARE.streamsScreenFrames()).isTrue();
        assertThat(ProjectionMode.WHITEBOARD.streamsScreenFrames()).isFalse();
        assertThat(ProjectionMode.POINTER.streamsScreenFrames()).isFalse();
        assertThat(ProjectionMode.OPEN_WEB_CONTENT.streamsScreenFrames()).isFalse();

        assertThat(ProjectionMode.WHITEBOARD.lightweightEvents()).isTrue();
        assertThat(ProjectionMode.POINTER.lightweightEvents()).isTrue();
        assertThat(ProjectionMode.LOCAL_MEDIA.prefersLocalClientExecution()).isTrue();
        assertThat(ProjectionMode.OPEN_WEB_CONTENT.prefersLocalClientExecution()).isTrue();
    }
}
