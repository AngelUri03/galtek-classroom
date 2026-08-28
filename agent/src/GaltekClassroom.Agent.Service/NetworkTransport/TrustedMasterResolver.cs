using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Pairing;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed record TrustedMasterResolution(
    bool Trusted,
    PairingStatus Status,
    AuthorizedMasterTrustRecord? Master,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static TrustedMasterResolution Success(AuthorizedMasterTrustRecord master)
    {
        return new TrustedMasterResolution(true, PairingStatus.Paired, master, null, null);
    }

    public static TrustedMasterResolution Blocked(
        PairingStatus status,
        string errorCode,
        string errorMessage)
    {
        return new TrustedMasterResolution(false, status, null, errorCode, errorMessage);
    }
}

public sealed class TrustedMasterResolver
{
    private readonly ClientTrustStore _trustStore;

    public TrustedMasterResolver(ClientTrustStore trustStore)
    {
        _trustStore = trustStore;
    }

    public async Task<TrustedMasterResolution> ResolveAsync(
        Guid masterNetworkIdentityId,
        NetworkIdentityMetadata clientNetworkIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientNetworkIdentity);

        if (masterNetworkIdentityId == Guid.Empty)
        {
            return TrustedMasterResolution.Blocked(
                PairingStatus.Unpaired,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master Network Identity id is required.");
        }

        var read = await _trustStore.ReadAsync(cancellationToken);
        if (read.Status == ClientTrustStoreReadStatus.Missing)
        {
            return TrustedMasterResolution.Blocked(
                PairingStatus.Unpaired,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master is not paired with this Client.");
        }

        if (read.Status == ClientTrustStoreReadStatus.Invalid
            || !MatchesClient(read.Document!, clientNetworkIdentity))
        {
            return TrustedMasterResolution.Blocked(
                PairingStatus.Unpaired,
                PairingConstants.TrustStoreInvalidErrorCode,
                read.ErrorMessage ?? "Client trust store is invalid.");
        }

        var master = read.Document!.AuthorizedMasters
            .FirstOrDefault(item => item.MasterNetworkIdentityId == masterNetworkIdentityId);
        if (master is null)
        {
            return TrustedMasterResolution.Blocked(
                PairingStatus.Unpaired,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master is not paired with this Client.");
        }

        if (!PairingStatusExtensions.TryParseCode(master.Status, out var status))
        {
            return TrustedMasterResolution.Blocked(
                PairingStatus.Unpaired,
                PairingConstants.TrustStoreInvalidErrorCode,
                "Master trust status is invalid.");
        }

        if (status == PairingStatus.Revoked)
        {
            return TrustedMasterResolution.Blocked(
                PairingStatus.Revoked,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master pairing has been revoked.");
        }

        if (status != PairingStatus.Paired
            || master.ClientNetworkIdentityId != clientNetworkIdentity.NetworkIdentityId
            || master.ClientInstallationId != clientNetworkIdentity.InstallationId
            || !string.Equals(
                master.ClientPublicKeyFingerprint,
                clientNetworkIdentity.PublicKeyFingerprint,
                StringComparison.Ordinal)
            || !PairingCrypto.TryComputePublicKeyFingerprint(
                master.MasterPublicKeySubjectPublicKeyInfoBase64,
                out var computedFingerprint)
            || !string.Equals(computedFingerprint, master.MasterPublicKeyFingerprint, StringComparison.Ordinal))
        {
            return TrustedMasterResolution.Blocked(
                status,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master is not paired with this Client.");
        }

        return TrustedMasterResolution.Success(master);
    }

    private static bool MatchesClient(
        ClientTrustDocument document,
        NetworkIdentityMetadata clientNetworkIdentity)
    {
        return document.ClientNetworkIdentityId == clientNetworkIdentity.NetworkIdentityId
            && document.ClientInstallationId == clientNetworkIdentity.InstallationId
            && string.Equals(
                document.ClientPublicKeyFingerprint,
                clientNetworkIdentity.PublicKeyFingerprint,
                StringComparison.Ordinal);
    }
}
