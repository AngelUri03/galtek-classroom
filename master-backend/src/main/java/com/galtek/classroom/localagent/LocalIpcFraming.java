package com.galtek.classroom.localagent;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

public final class LocalIpcFraming {

    private LocalIpcFraming() {
    }

    public static byte[] frameJson(String json) throws LocalIpcFrameException {
        byte[] payload = json.getBytes(StandardCharsets.UTF_8);
        validateLength(payload.length);

        byte[] frame = new byte[Integer.BYTES + payload.length];
        frame[0] = (byte) ((payload.length >>> 24) & 0xff);
        frame[1] = (byte) ((payload.length >>> 16) & 0xff);
        frame[2] = (byte) ((payload.length >>> 8) & 0xff);
        frame[3] = (byte) (payload.length & 0xff);
        System.arraycopy(payload, 0, frame, Integer.BYTES, payload.length);

        return frame;
    }

    public static void writeJson(OutputStream outputStream, String json) throws IOException {
        DataOutputStream dataOutputStream = new DataOutputStream(outputStream);
        byte[] payload = json.getBytes(StandardCharsets.UTF_8);
        validateLength(payload.length);
        dataOutputStream.writeInt(payload.length);
        dataOutputStream.write(payload);
        dataOutputStream.flush();
    }

    public static String readJson(InputStream inputStream) throws IOException {
        DataInputStream dataInputStream = new DataInputStream(inputStream);
        int length = dataInputStream.readInt();
        validateLength(length);

        byte[] payload = dataInputStream.readNBytes(length);

        if (payload.length != length) {
            throw new LocalIpcFrameException("IPC message ended before the declared length.");
        }

        return new String(payload, StandardCharsets.UTF_8);
    }

    public static byte[] payloadFromFrame(byte[] frame) throws LocalIpcFrameException {
        if (frame.length < Integer.BYTES) {
            throw new LocalIpcFrameException("IPC frame is shorter than the length prefix.");
        }

        int length = ((frame[0] & 0xff) << 24)
                | ((frame[1] & 0xff) << 16)
                | ((frame[2] & 0xff) << 8)
                | (frame[3] & 0xff);
        validateLength(length);

        if (frame.length != Integer.BYTES + length) {
            throw new LocalIpcFrameException("IPC frame size does not match its length prefix.");
        }

        return Arrays.copyOfRange(frame, Integer.BYTES, frame.length);
    }

    private static void validateLength(int length) throws LocalIpcFrameException {
        if (length <= 0) {
            throw new LocalIpcFrameException("IPC message length must be positive.");
        }

        if (length > LocalIpcProtocol.MAX_MESSAGE_BYTES) {
            throw new LocalIpcFrameException("IPC message length exceeds the configured limit.");
        }
    }
}
