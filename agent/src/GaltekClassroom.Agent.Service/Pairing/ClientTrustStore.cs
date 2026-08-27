using System.Text.Json;
using GaltekClassroom.Agent.Service.Network;

namespace GaltekClassroom.Agent.Service.Pairing;

public sealed record ClientTrustStoreOptions(string DataDirectory);

public enum ClientTrustStoreReadStatus
{
    Missing,
    Loaded,
    Invalid
}

public sealed record ClientTrustStoreReadResult(
    ClientTrustStoreReadStatus Status,
    ClientTrustDocument? Document,
    string FilePath,
    string? ErrorMessage)
{
    public static ClientTrustStoreReadResult Missing(string filePath)
    {
        return new ClientTrustStoreReadResult(ClientTrustStoreReadStatus.Missing, null, filePath, null);
    }

    public static ClientTrustStoreReadResult Loaded(ClientTrustDocument document, string filePath)
    {
        return new ClientTrustStoreReadResult(ClientTrustStoreReadStatus.Loaded, document, filePath, null);
    }

    public static ClientTrustStoreReadResult Invalid(string filePath, string errorMessage)
    {
        return new ClientTrustStoreReadResult(ClientTrustStoreReadStatus.Invalid, null, filePath, errorMessage);
    }
}

public sealed class ClientTrustStore
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _filePath;
    private readonly INetworkIdentityFileSecurity _fileSecurity;

    public ClientTrustStore(
        ClientTrustStoreOptions options,
        INetworkIdentityFileSecurity fileSecurity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, PairingConstants.AuthorizedMastersFileName);
        _fileSecurity = fileSecurity;
    }

    public string FilePath => _filePath;

    public async Task<ClientTrustStoreReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return ClientTrustStoreReadResult.Missing(_filePath);
        }

        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var document = await JsonSerializer.DeserializeAsync<ClientTrustDocument>(
                stream,
                ReadOptions,
                cancellationToken);

            if (!ClientTrustValidator.IsValid(document, out var validationError))
            {
                return ClientTrustStoreReadResult.Invalid(
                    _filePath,
                    $"authorized-masters.json is corrupt or incomplete: {validationError}");
            }

            return ClientTrustStoreReadResult.Loaded(document!, _filePath);
        }
        catch (JsonException exception)
        {
            return ClientTrustStoreReadResult.Invalid(
                _filePath,
                $"authorized-masters.json is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return ClientTrustStoreReadResult.Invalid(
                _filePath,
                $"authorized-masters.json could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ClientTrustStoreReadResult.Invalid(
                _filePath,
                $"authorized-masters.json could not be read: {exception.Message}");
        }
    }

    public async Task SaveAsync(ClientTrustDocument document, CancellationToken cancellationToken)
    {
        if (!ClientTrustValidator.IsValid(document, out var validationError))
        {
            throw new InvalidOperationException($"Cannot persist invalid client trust: {validationError}");
        }

        Directory.CreateDirectory(_dataDirectory);

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{PairingConstants.AuthorizedMastersFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, WriteOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            _fileSecurity.Apply(tempPath);
            File.Move(tempPath, _filePath, overwrite: true);
            _fileSecurity.Apply(_filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

public static class ClientTrustValidator
{
    public static bool IsValid(ClientTrustDocument? document, out string error)
    {
        if (document is null)
        {
            error = "client trust payload is empty";
            return false;
        }

        if (document.SchemaVersion != PairingConstants.SchemaVersion)
        {
            error = $"unsupported schemaVersion {document.SchemaVersion}";
            return false;
        }

        if (document.ClientNetworkIdentityId == Guid.Empty)
        {
            error = "clientNetworkIdentityId is empty";
            return false;
        }

        if (document.ClientInstallationId == Guid.Empty)
        {
            error = "clientInstallationId is empty";
            return false;
        }

        if (!NetworkIdentityValidator.IsValidSha256Hex(document.ClientPublicKeyFingerprint))
        {
            error = "clientPublicKeyFingerprint is missing or invalid";
            return false;
        }

        foreach (var master in document.AuthorizedMasters ?? Array.Empty<AuthorizedMasterTrustRecord>())
        {
            if (!IsValid(master, document, out error))
            {
                return false;
            }
        }

        foreach (var challenge in document.ConsumedChallenges ?? Array.Empty<ConsumedPairingChallengeRecord>())
        {
            if (challenge.ChallengeId == Guid.Empty)
            {
                error = "consumed challengeId is empty";
                return false;
            }

            if (challenge.MasterNetworkIdentityId == Guid.Empty)
            {
                error = "consumed masterNetworkIdentityId is empty";
                return false;
            }

            if (!PairingCrypto.IsValidNonce(challenge.NonceBase64))
            {
                error = "consumed challenge nonce is missing or invalid";
                return false;
            }

            if (!IsUtc(challenge.ConsumedAtUtc))
            {
                error = "consumedAtUtc must be UTC";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValid(
        AuthorizedMasterTrustRecord master,
        ClientTrustDocument document,
        out string error)
    {
        if (master.SchemaVersion != PairingConstants.SchemaVersion)
        {
            error = $"authorized master has unsupported schemaVersion {master.SchemaVersion}";
            return false;
        }

        if (!PairingStatusExtensions.TryParseCode(master.Status, out var status))
        {
            error = "authorized master status is invalid";
            return false;
        }

        if (master.MasterNetworkIdentityId == Guid.Empty)
        {
            error = "authorized masterNetworkIdentityId is empty";
            return false;
        }

        if (master.ClientNetworkIdentityId != document.ClientNetworkIdentityId
            || master.ClientInstallationId != document.ClientInstallationId)
        {
            error = "authorized master is bound to a different client identity";
            return false;
        }

        if (!string.Equals(master.ClientPublicKeyFingerprint, document.ClientPublicKeyFingerprint, StringComparison.Ordinal))
        {
            error = "authorized master is bound to a different client fingerprint";
            return false;
        }

        if (!NetworkIdentityValidator.IsValidSha256Hex(master.MasterPublicKeyFingerprint))
        {
            error = "authorized master fingerprint is missing or invalid";
            return false;
        }

        if (!PairingCrypto.TryComputePublicKeyFingerprint(
            master.MasterPublicKeySubjectPublicKeyInfoBase64,
            out var computedFingerprint)
            || !string.Equals(computedFingerprint, master.MasterPublicKeyFingerprint, StringComparison.Ordinal))
        {
            error = "authorized master public key does not match fingerprint";
            return false;
        }

        if (status is PairingStatus.Paired or PairingStatus.Revoked
            && (!master.PairedAtUtc.HasValue || !IsUtc(master.PairedAtUtc.Value)))
        {
            error = "pairedAtUtc is required for paired or revoked trust";
            return false;
        }

        if (status == PairingStatus.Revoked
            && (!master.RevokedAtUtc.HasValue || !IsUtc(master.RevokedAtUtc.Value)))
        {
            error = "revokedAtUtc is required for revoked trust";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsUtc(DateTimeOffset value)
    {
        return value != default && value.Offset == TimeSpan.Zero;
    }
}
