namespace GaltekClassroom.Agent.Service.Network;

public static class NetworkIdentityConstants
{
    public const int SchemaVersion = 1;
    public const string FileName = "network-identity.json";
    public const string KeyNamePrefix = "GaltekClassroom.NetworkIdentity.";
    public const string KeyDerivationNamespace = "GALTEK_CLASSROOM_NETWORK_IDENTITY_V1";
    public const int RsaKeySizeBits = 2048;

    public const string NotConfiguredErrorCode = "NETWORK_IDENTITY_NOT_CONFIGURED";
    public const string InvalidErrorCode = "NETWORK_IDENTITY_INVALID";
    public const string KeyMissingErrorCode = "NETWORK_IDENTITY_KEY_MISSING";
    public const string InstallationMismatchErrorCode = "NETWORK_IDENTITY_INSTALLATION_MISMATCH";
}
