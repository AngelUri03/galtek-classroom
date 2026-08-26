package com.galtek.classroom.localagent;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import org.junit.jupiter.api.Test;

class LocalIpcFramingTest {

    @Test
    void frameJsonUsesBigEndianLengthPrefix() throws Exception {
        byte[] frame = LocalIpcFraming.frameJson("{}");

        assertThat(frame).hasSize(6);
        assertThat(Arrays.copyOfRange(frame, 0, 4)).containsExactly(0, 0, 0, 2);
        assertThat(new String(LocalIpcFraming.payloadFromFrame(frame), StandardCharsets.UTF_8))
                .isEqualTo("{}");
    }

    @Test
    void readJsonReadsExactPayload() throws Exception {
        ByteArrayOutputStream outputStream = new ByteArrayOutputStream();
        LocalIpcFraming.writeJson(outputStream, "{\"operation\":\"PING\"}");

        String json = LocalIpcFraming.readJson(new ByteArrayInputStream(outputStream.toByteArray()));

        assertThat(json).isEqualTo("{\"operation\":\"PING\"}");
    }

    @Test
    void frameJsonRejectsMessagesLargerThanLimit() {
        String oversizedJson = "a".repeat(LocalIpcProtocol.MAX_MESSAGE_BYTES + 1);

        assertThatThrownBy(() -> LocalIpcFraming.frameJson(oversizedJson))
                .isInstanceOf(LocalIpcFrameException.class);
    }

    @Test
    void frameJsonAcceptsMessageAtLimit() throws Exception {
        String maxSizeJson = "a".repeat(LocalIpcProtocol.MAX_MESSAGE_BYTES);

        byte[] frame = LocalIpcFraming.frameJson(maxSizeJson);

        assertThat(frame).hasSize(Integer.BYTES + LocalIpcProtocol.MAX_MESSAGE_BYTES);
        assertThat(Arrays.copyOfRange(frame, 0, 4)).containsExactly(0, 1, 0, 0);
    }

    @Test
    void readJsonRejectsInvalidLength() {
        byte[] zeroLengthFrame = new byte[] { 0, 0, 0, 0 };

        assertThatThrownBy(() -> LocalIpcFraming.readJson(new ByteArrayInputStream(zeroLengthFrame)))
                .isInstanceOf(LocalIpcFrameException.class);
    }

    @Test
    void readJsonRejectsLengthLargerThanLimit() {
        byte[] oversizedLengthFrame = new byte[] { 0, 1, 0, 1 };

        assertThatThrownBy(() -> LocalIpcFraming.readJson(new ByteArrayInputStream(oversizedLengthFrame)))
                .isInstanceOf(LocalIpcFrameException.class);
    }
}
