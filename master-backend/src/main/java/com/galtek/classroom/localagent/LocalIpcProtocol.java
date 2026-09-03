package com.galtek.classroom.localagent;

public final class LocalIpcProtocol {

    public static final int PROTOCOL_VERSION = 1;
    public static final String PIPE_NAME = "GaltekClassroom.Agent.v1";
    public static final String PIPE_PATH = "\\\\.\\pipe\\" + PIPE_NAME;
    public static final int MAX_MESSAGE_BYTES = 64 * 1024;

    public static final String OPERATION_PING = "PING";
    public static final String OPERATION_GET_DEVICE_STATUS = "GET_DEVICE_STATUS";
    public static final String OPERATION_GET_MACHINE_CODE = "GET_MACHINE_CODE";
    public static final String OPERATION_GET_MASTER_AUTHORIZATION = "GET_MASTER_AUTHORIZATION";
    public static final String OPERATION_GET_MASTER_UNLOCK_AUTHORIZATION = "GET_MASTER_UNLOCK_AUTHORIZATION";

    public static final String ERROR_RESPONSE_MISMATCH = "IPC_RESPONSE_MISMATCH";
    public static final String ERROR_MALFORMED_RESPONSE = "IPC_MALFORMED_RESPONSE";

    private LocalIpcProtocol() {
    }
}
