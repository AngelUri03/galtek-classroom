using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Pairing;

public sealed record ClientPairingAcceptResult(
    bool Accepted,
    string? ErrorCode,
    string? ErrorMessage,
    PairingStatus Status,
    PairingResponse? Response)
{
    public static ClientPairingAcceptResult Success(PairingResponse response)
    {
        return new ClientPairingAcceptResult(true, null, null, PairingStatus.Paired, response);
    }

    public static ClientPairingAcceptResult Failed(
        string errorCode,
        string errorMessage,
        PairingStatus status = PairingStatus.Unpaired)
    {
        return new ClientPairingAcceptResult(false, errorCode, errorMessage, status, null);
    }
}

public sealed record ClientTrustAuthorization(
    bool Authorized,
    PairingStatus Status,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ClientTrustAuthorization Success()
    {
        return new ClientTrustAuthorization(true, PairingStatus.Paired, null, null);
    }

    public static ClientTrustAuthorization Blocked(
        PairingStatus status,
        string errorCode,
        string errorMessage)
    {
        return new ClientTrustAuthorization(false, status, errorCode, errorMessage);
    }
}

public sealed record MasterNetworkIdentityDescriptor(
    Guid MasterNetworkIdentityId,
    string PublicKeyFingerprint,
    string SubjectPublicKeyInfoBase64);

public sealed class ClientPairingService
{
    private readonly ClientTrustStore _trustStore;
    private readonly INetworkIdentityKeyStore _keyStore;
    private readonly ISystemClock _clock;

    public ClientPairingService(
        ClientTrustStore trustStore,
        INetworkIdentityKeyStore keyStore,
        ISystemClock clock)
    {
        _trustStore = trustStore;
        _keyStore = keyStore;
        _clock = clock;
    }

    public async Task<ClientPairingAcceptResult> AcceptChallengeAsync(
        PairingChallenge challenge,
        InstallationIdentity installationIdentity,
        NetworkIdentityMetadata clientNetworkIdentity,
        bool explicitApproval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(installationIdentity);
        ArgumentNullException.ThrowIfNull(clientNetworkIdentity);

        if (!explicitApproval)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.ExplicitApprovalRequiredErrorCode,
                "Pairing requires explicit teacher or administrator approval.");
        }

        var validation = ValidateChallenge(challenge, installationIdentity, clientNetworkIdentity);
        if (validation is not null)
        {
            return validation;
        }

        var publicKey = _keyStore.GetPublicKey(clientNetworkIdentity.KeyName);
        if (publicKey.Status != NetworkIdentityPublicKeyStatus.Found)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.NetworkIdentityUnavailableErrorCode,
                publicKey.ErrorMessage ?? "Client Network Identity key is unavailable.");
        }

        if (!string.Equals(publicKey.PublicKeyFingerprint, clientNetworkIdentity.PublicKeyFingerprint, StringComparison.Ordinal)
            || !string.Equals(
                publicKey.SubjectPublicKeyInfoBase64,
                challenge.ClientPublicKeySubjectPublicKeyInfoBase64,
                StringComparison.Ordinal))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.FingerprintMismatchErrorCode,
                "Pairing challenge does not target the local Client Network Identity.");
        }

        if (!PairingCrypto.VerifySignature(
            challenge.MasterPublicKeySubjectPublicKeyInfoBase64,
            PairingCrypto.CanonicalChallengeBytes(challenge),
            challenge.MasterSignatureBase64))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.SignatureInvalidErrorCode,
                "Master pairing challenge signature is invalid.");
        }

        var documentResult = await LoadOrCreateDocumentAsync(clientNetworkIdentity, cancellationToken);
        if (!documentResult.Valid)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.TrustStoreInvalidErrorCode,
                documentResult.ErrorMessage ?? "Client trust store is invalid.");
        }

        var document = documentResult.Document!;
        if (!MatchesClient(document, clientNetworkIdentity))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.TrustStoreInvalidErrorCode,
                "Client trust store belongs to a different Network Identity.");
        }

        if (document.ConsumedChallenges.Any(consumed => consumed.ChallengeId == challenge.ChallengeId))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.ReplayRejectedErrorCode,
                "Pairing challenge has already been consumed.");
        }

        var existing = document.AuthorizedMasters
            .FirstOrDefault(master => master.MasterNetworkIdentityId == challenge.MasterNetworkIdentityId);
        if (existing is not null
            && PairingStatusExtensions.TryParseCode(existing.Status, out var existingStatus)
            && existingStatus == PairingStatus.Revoked)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.MasterRevokedErrorCode,
                "Master pairing was revoked and cannot be reused silently.",
                PairingStatus.Revoked);
        }

        var pending = UpsertMaster(
            document,
            challenge,
            PairingStatus.PairingPending,
            pairedAtUtc: null,
            revokedAtUtc: null);
        await _trustStore.SaveAsync(pending, cancellationToken);

        var response = new PairingResponse
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            Purpose = PairingConstants.Purpose,
            ChallengeId = challenge.ChallengeId,
            MasterNetworkIdentityId = challenge.MasterNetworkIdentityId,
            ClientNetworkIdentityId = challenge.ClientNetworkIdentityId,
            ClientInstallationId = challenge.ClientInstallationId,
            MasterPublicKeyFingerprint = challenge.MasterPublicKeyFingerprint,
            ClientPublicKeyFingerprint = challenge.ClientPublicKeyFingerprint,
            ChallengeNonceBase64 = challenge.NonceBase64,
            ResponseNonceBase64 = PairingCrypto.CreateNonceBase64(),
            SignedAtUtc = _clock.UtcNow.ToUniversalTime()
        };

        var signature = _keyStore.Sign(
            clientNetworkIdentity.KeyName,
            PairingCrypto.CanonicalResponseBytes(response));
        if (!signature.Signed)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.NetworkIdentityUnavailableErrorCode,
                signature.ErrorMessage ?? "Client Network Identity key could not sign pairing response.",
                PairingStatus.PairingPending);
        }

        response = response with { ClientSignatureBase64 = signature.SignatureBase64 ?? string.Empty };

        var pairedAtUtc = _clock.UtcNow.ToUniversalTime();
        var paired = UpsertMaster(
            pending,
            challenge,
            PairingStatus.Paired,
            pairedAtUtc,
            revokedAtUtc: null);
        paired = paired with
        {
            ConsumedChallenges = paired.ConsumedChallenges
                .Append(new ConsumedPairingChallengeRecord
                {
                    ChallengeId = challenge.ChallengeId,
                    MasterNetworkIdentityId = challenge.MasterNetworkIdentityId,
                    NonceBase64 = challenge.NonceBase64,
                    ConsumedAtUtc = pairedAtUtc
                })
                .ToArray()
        };

        await _trustStore.SaveAsync(paired, cancellationToken);

        return ClientPairingAcceptResult.Success(response);
    }

    public async Task<ClientTrustAuthorization> IsMasterAuthorizedAsync(
        MasterNetworkIdentityDescriptor master,
        NetworkIdentityMetadata clientNetworkIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(clientNetworkIdentity);

        if (!IsValidMasterDescriptor(master))
        {
            return ClientTrustAuthorization.Blocked(
                PairingStatus.Unpaired,
                PairingConstants.FingerprintMismatchErrorCode,
                "Master public key fingerprint does not match its public key.");
        }

        var read = await _trustStore.ReadAsync(cancellationToken);
        if (read.Status == ClientTrustStoreReadStatus.Missing)
        {
            return ClientTrustAuthorization.Blocked(
                PairingStatus.Unpaired,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master is not paired with this Client.");
        }

        if (read.Status == ClientTrustStoreReadStatus.Invalid
            || !MatchesClient(read.Document!, clientNetworkIdentity))
        {
            return ClientTrustAuthorization.Blocked(
                PairingStatus.Unpaired,
                PairingConstants.TrustStoreInvalidErrorCode,
                read.ErrorMessage ?? "Client trust store is invalid.");
        }

        var record = read.Document!.AuthorizedMasters
            .FirstOrDefault(item => item.MasterNetworkIdentityId == master.MasterNetworkIdentityId);
        if (record is null)
        {
            return ClientTrustAuthorization.Blocked(
                PairingStatus.Unpaired,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master is not paired with this Client.");
        }

        PairingStatusExtensions.TryParseCode(record.Status, out var status);
        if (status == PairingStatus.Revoked)
        {
            return ClientTrustAuthorization.Blocked(
                PairingStatus.Revoked,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master pairing has been revoked.");
        }

        if (status != PairingStatus.Paired
            || !string.Equals(record.MasterPublicKeyFingerprint, master.PublicKeyFingerprint, StringComparison.Ordinal)
            || !string.Equals(
                record.MasterPublicKeySubjectPublicKeyInfoBase64,
                master.SubjectPublicKeyInfoBase64,
                StringComparison.Ordinal)
            || !string.Equals(record.ClientPublicKeyFingerprint, clientNetworkIdentity.PublicKeyFingerprint, StringComparison.Ordinal))
        {
            return ClientTrustAuthorization.Blocked(
                status,
                ClassroomOperationErrorCodes.MasterNotPaired,
                "Master is not paired with this Client.");
        }

        return ClientTrustAuthorization.Success();
    }

    public async Task<bool> RevokeMasterAsync(
        Guid masterNetworkIdentityId,
        NetworkIdentityMetadata clientNetworkIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientNetworkIdentity);

        var read = await _trustStore.ReadAsync(cancellationToken);
        if (read.Status != ClientTrustStoreReadStatus.Loaded
            || !MatchesClient(read.Document!, clientNetworkIdentity))
        {
            return false;
        }

        var records = read.Document!.AuthorizedMasters.ToList();
        var index = records.FindIndex(item => item.MasterNetworkIdentityId == masterNetworkIdentityId);
        if (index < 0)
        {
            return false;
        }

        records[index] = records[index] with
        {
            Status = PairingStatus.Revoked.ToCode(),
            RevokedAtUtc = _clock.UtcNow.ToUniversalTime()
        };

        await _trustStore.SaveAsync(read.Document with { AuthorizedMasters = records }, cancellationToken);
        return true;
    }

    private ClientPairingAcceptResult? ValidateChallenge(
        PairingChallenge challenge,
        InstallationIdentity installationIdentity,
        NetworkIdentityMetadata clientNetworkIdentity)
    {
        if (challenge.SchemaVersion != PairingConstants.SchemaVersion
            || !string.Equals(challenge.Purpose, PairingConstants.Purpose, StringComparison.Ordinal)
            || challenge.ChallengeId == Guid.Empty
            || challenge.MasterNetworkIdentityId == Guid.Empty
            || challenge.ClientNetworkIdentityId == Guid.Empty
            || challenge.ClientInstallationId == Guid.Empty
            || !PairingCrypto.IsValidNonce(challenge.NonceBase64)
            || challenge.IssuedAtUtc == default
            || challenge.ExpiresAtUtc == default
            || challenge.ExpiresAtUtc <= challenge.IssuedAtUtc)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.ChallengeInvalidErrorCode,
                "Pairing challenge is malformed.");
        }

        if (_clock.UtcNow.ToUniversalTime() > challenge.ExpiresAtUtc.ToUniversalTime())
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.ChallengeExpiredErrorCode,
                "Pairing challenge has expired.");
        }

        if (challenge.ClientInstallationId != installationIdentity.InstallationId
            || challenge.ClientInstallationId != clientNetworkIdentity.InstallationId
            || challenge.ClientNetworkIdentityId != clientNetworkIdentity.NetworkIdentityId)
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.FingerprintMismatchErrorCode,
                "Pairing challenge is not bound to this Client installation.");
        }

        if (!string.Equals(
            challenge.ClientPublicKeyFingerprint,
            clientNetworkIdentity.PublicKeyFingerprint,
            StringComparison.Ordinal))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.FingerprintMismatchErrorCode,
                "Pairing challenge has an incorrect Client public key fingerprint.");
        }

        if (!IsValidMasterDescriptor(new MasterNetworkIdentityDescriptor(
            challenge.MasterNetworkIdentityId,
            challenge.MasterPublicKeyFingerprint,
            challenge.MasterPublicKeySubjectPublicKeyInfoBase64)))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.FingerprintMismatchErrorCode,
                "Master public key fingerprint does not match its public key.");
        }

        if (!PairingCrypto.TryComputePublicKeyFingerprint(
            challenge.ClientPublicKeySubjectPublicKeyInfoBase64,
            out var challengeClientFingerprint)
            || !string.Equals(challengeClientFingerprint, clientNetworkIdentity.PublicKeyFingerprint, StringComparison.Ordinal))
        {
            return ClientPairingAcceptResult.Failed(
                PairingConstants.FingerprintMismatchErrorCode,
                "Client public key fingerprint does not match the pairing challenge public key.");
        }

        return null;
    }

    private async Task<(bool Valid, ClientTrustDocument? Document, string? ErrorMessage)> LoadOrCreateDocumentAsync(
        NetworkIdentityMetadata clientNetworkIdentity,
        CancellationToken cancellationToken)
    {
        var read = await _trustStore.ReadAsync(cancellationToken);
        return read.Status switch
        {
            ClientTrustStoreReadStatus.Missing => (true, ClientTrustDocument.Empty(clientNetworkIdentity), null),
            ClientTrustStoreReadStatus.Loaded => (true, read.Document, null),
            _ => (false, null, read.ErrorMessage)
        };
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

    private static bool IsValidMasterDescriptor(MasterNetworkIdentityDescriptor master)
    {
        return master.MasterNetworkIdentityId != Guid.Empty
            && NetworkIdentityValidator.IsValidSha256Hex(master.PublicKeyFingerprint)
            && PairingCrypto.TryComputePublicKeyFingerprint(
                master.SubjectPublicKeyInfoBase64,
                out var computedFingerprint)
            && string.Equals(computedFingerprint, master.PublicKeyFingerprint, StringComparison.Ordinal);
    }

    private static ClientTrustDocument UpsertMaster(
        ClientTrustDocument document,
        PairingChallenge challenge,
        PairingStatus status,
        DateTimeOffset? pairedAtUtc,
        DateTimeOffset? revokedAtUtc)
    {
        var records = document.AuthorizedMasters.ToList();
        var replacement = new AuthorizedMasterTrustRecord
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            Status = status.ToCode(),
            MasterNetworkIdentityId = challenge.MasterNetworkIdentityId,
            ClientNetworkIdentityId = challenge.ClientNetworkIdentityId,
            ClientInstallationId = challenge.ClientInstallationId,
            MasterPublicKeyFingerprint = challenge.MasterPublicKeyFingerprint,
            ClientPublicKeyFingerprint = challenge.ClientPublicKeyFingerprint,
            MasterPublicKeySubjectPublicKeyInfoBase64 = challenge.MasterPublicKeySubjectPublicKeyInfoBase64,
            PairedAtUtc = pairedAtUtc,
            RevokedAtUtc = revokedAtUtc,
            PairedChallengeId = status == PairingStatus.Paired ? challenge.ChallengeId : null,
            CertificateThumbprint = null
        };

        var index = records.FindIndex(item => item.MasterNetworkIdentityId == challenge.MasterNetworkIdentityId);
        if (index >= 0)
        {
            records[index] = replacement;
        }
        else
        {
            records.Add(replacement);
        }

        return document with { AuthorizedMasters = records };
    }
}
