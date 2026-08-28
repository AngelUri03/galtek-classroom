namespace GaltekClassroom.Agent.Service.NetworkTransport;

public static class MasterConnectionConstants
{
    public const string ProtocolVersion = "network.v1";
    public const string MutualTlsRequiredErrorCode = "MUTUAL_TLS_REQUIRED";
    public const string CertificateFingerprintMismatchErrorCode = "CERTIFICATE_FINGERPRINT_MISMATCH";
    public const string NetworkIdentityUnavailableErrorCode = "NETWORK_IDENTITY_UNAVAILABLE";
    public const string ProtocolViolationErrorCode = "PROTOCOL_VIOLATION";
}
