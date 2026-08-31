namespace GaltekClassroom.Agent.Shared;

public static class LocalIpcProtocol
{
    public const int ProtocolVersion = 1;
    public const string PipeName = "GaltekClassroom.Agent.v1";
    public const int MaxMessageBytes = 64 * 1024;
    public const int MaxConcurrentConnections = 4;
}

public static class LocalIpcOperations
{
    public const string Ping = "PING";
    public const string GetDeviceStatus = "GET_DEVICE_STATUS";
    public const string GetMachineCode = "GET_MACHINE_CODE";
    public const string GetMasterAuthorization = "GET_MASTER_AUTHORIZATION";
    public const string GetRuntimeDiagnostics = "GET_RUNTIME_DIAGNOSTICS";
}

public static class LocalIpcErrorCodes
{
    public const string ProtocolUnsupported = "IPC_PROTOCOL_UNSUPPORTED";
    public const string OperationNotSupported = "IPC_OPERATION_NOT_SUPPORTED";
    public const string MalformedRequest = "IPC_MALFORMED_REQUEST";
    public const string InvalidRequest = "IPC_INVALID_REQUEST";
    public const string InternalError = "IPC_INTERNAL_ERROR";
    public const string ResponseMismatch = "IPC_RESPONSE_MISMATCH";
}
