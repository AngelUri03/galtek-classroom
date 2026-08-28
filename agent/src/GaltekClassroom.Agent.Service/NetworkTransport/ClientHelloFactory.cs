using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class ClientHelloFactory
{
    private readonly INetworkIdentityKeyStore _keyStore;
    private readonly IHostNameProvider _hostNameProvider;
    private readonly ISystemClock _clock;

    public ClientHelloFactory(
        INetworkIdentityKeyStore keyStore,
        IHostNameProvider hostNameProvider,
        ISystemClock clock)
    {
        _keyStore = keyStore;
        _hostNameProvider = hostNameProvider;
        _clock = clock;
    }

    public ClientHelloCreationResult Create(
        NetworkIdentityMetadata clientNetworkIdentity,
        MasterConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(clientNetworkIdentity);
        ArgumentNullException.ThrowIfNull(options);

        var publicKey = _keyStore.GetPublicKey(clientNetworkIdentity.KeyName);
        if (publicKey.Status != NetworkIdentityPublicKeyStatus.Found)
        {
            return ClientHelloCreationResult.Failed(
                MasterConnectionConstants.NetworkIdentityUnavailableErrorCode,
                publicKey.ErrorMessage ?? "Client Network Identity public key is unavailable.");
        }

        if (!string.Equals(
            publicKey.PublicKeyFingerprint,
            clientNetworkIdentity.PublicKeyFingerprint,
            StringComparison.Ordinal))
        {
            return ClientHelloCreationResult.Failed(
                MasterConnectionConstants.NetworkIdentityUnavailableErrorCode,
                "Client Network Identity public key does not match network-identity.json.");
        }

        var hostName = _hostNameProvider.GetHostName();
        var hello = new ClientHello
        {
            ClientNetworkIdentityId = clientNetworkIdentity.NetworkIdentityId.ToString("D"),
            ClientInstallationId = clientNetworkIdentity.InstallationId.ToString("D"),
            ClientPublicKeyFingerprint = clientNetworkIdentity.PublicKeyFingerprint,
            ClientPublicKeySubjectPublicKeyInfoBase64 = publicKey.SubjectPublicKeyInfoBase64 ?? string.Empty,
            DeviceId = string.IsNullOrWhiteSpace(options.DeviceId)
                ? clientNetworkIdentity.NetworkIdentityId.ToString("D")
                : options.DeviceId,
            DisplayName = string.IsNullOrWhiteSpace(options.DisplayName) ? hostName : options.DisplayName,
            Hostname = hostName,
            SentAtUnixMs = _clock.UtcNow.ToUnixTimeMilliseconds()
        };

        return ClientHelloCreationResult.Success(hello);
    }
}

public sealed record ClientHelloCreationResult(
    bool Created,
    ClientHello? Hello,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ClientHelloCreationResult Success(ClientHello hello)
    {
        return new ClientHelloCreationResult(true, hello, null, null);
    }

    public static ClientHelloCreationResult Failed(string errorCode, string errorMessage)
    {
        return new ClientHelloCreationResult(false, null, errorCode, errorMessage);
    }
}
