using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Pairing;

public sealed record ClientPairingDescriptor
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = PairingConstants.SchemaVersion;

    [JsonPropertyName("purpose")]
    [JsonPropertyOrder(1)]
    public string Purpose { get; init; } = PairingConstants.ClientDescriptorPurpose;

    [JsonPropertyName("installationId")]
    [JsonPropertyOrder(2)]
    public Guid InstallationId { get; init; }

    [JsonPropertyName("networkIdentityId")]
    [JsonPropertyOrder(3)]
    public Guid NetworkIdentityId { get; init; }

    [JsonPropertyName("publicKeyFingerprint")]
    [JsonPropertyOrder(4)]
    public string PublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("publicKeySubjectPublicKeyInfoBase64")]
    [JsonPropertyOrder(5)]
    public string PublicKeySubjectPublicKeyInfoBase64 { get; init; } = string.Empty;

    [JsonPropertyName("exportedAtUtc")]
    [JsonPropertyOrder(6)]
    public DateTimeOffset ExportedAtUtc { get; init; }
}

public sealed record PairingFileBootstrapResult(
    bool Succeeded,
    string Operation,
    string? ErrorCode,
    string? ErrorMessage,
    string? Status,
    string? OutputPath,
    Guid? ChallengeId,
    Guid? MasterNetworkIdentityId,
    Guid? ClientNetworkIdentityId,
    Guid? ClientInstallationId,
    DateTimeOffset? ExpiresAtUtc)
{
    public static PairingFileBootstrapResult DescriptorExported(
        string outputPath,
        ClientPairingDescriptor descriptor)
    {
        return new PairingFileBootstrapResult(
            true,
            "PAIRING_EXPORT_DESCRIPTOR",
            null,
            null,
            "READY",
            outputPath,
            null,
            null,
            descriptor.NetworkIdentityId,
            descriptor.InstallationId,
            null);
    }

    public static PairingFileBootstrapResult ChallengeAccepted(
        string outputPath,
        PairingResponse response)
    {
        return new PairingFileBootstrapResult(
            true,
            "PAIRING_ACCEPT_CHALLENGE",
            null,
            null,
            PairingStatus.Paired.ToCode(),
            outputPath,
            response.ChallengeId,
            response.MasterNetworkIdentityId,
            response.ClientNetworkIdentityId,
            response.ClientInstallationId,
            null);
    }

    public static PairingFileBootstrapResult Failed(
        string operation,
        string errorCode,
        string errorMessage,
        string? status = null)
    {
        return new PairingFileBootstrapResult(
            false,
            operation,
            errorCode,
            errorMessage,
            status,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}

public sealed class PairingFileBootstrapService
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly InstallationIdentityResolver _installationIdentityResolver;
    private readonly NetworkIdentityResolver _networkIdentityResolver;
    private readonly INetworkIdentityKeyStore _keyStore;
    private readonly ClientPairingService _pairingService;
    private readonly ISystemClock _clock;

    public PairingFileBootstrapService(
        InstallationIdentityResolver installationIdentityResolver,
        NetworkIdentityResolver networkIdentityResolver,
        INetworkIdentityKeyStore keyStore,
        ClientPairingService pairingService,
        ISystemClock clock)
    {
        _installationIdentityResolver = installationIdentityResolver;
        _networkIdentityResolver = networkIdentityResolver;
        _keyStore = keyStore;
        _pairingService = pairingService;
        _clock = clock;
    }

    public async Task<PairingFileBootstrapResult> ExportClientDescriptorAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        var local = await ResolveLocalIdentityAsync("PAIRING_EXPORT_DESCRIPTOR", cancellationToken);
        if (!local.Succeeded)
        {
            return local.Error!;
        }

        var publicKey = _keyStore.GetPublicKey(local.NetworkIdentity!.KeyName);
        if (publicKey.Status != NetworkIdentityPublicKeyStatus.Found
            || !string.Equals(publicKey.PublicKeyFingerprint, local.NetworkIdentity.PublicKeyFingerprint, StringComparison.Ordinal))
        {
            return PairingFileBootstrapResult.Failed(
                "PAIRING_EXPORT_DESCRIPTOR",
                PairingConstants.NetworkIdentityUnavailableErrorCode,
                publicKey.ErrorMessage ?? "Client Network Identity public key is unavailable.");
        }

        var descriptor = new ClientPairingDescriptor
        {
            InstallationId = local.InstallationIdentity!.InstallationId,
            NetworkIdentityId = local.NetworkIdentity.NetworkIdentityId,
            PublicKeyFingerprint = local.NetworkIdentity.PublicKeyFingerprint,
            PublicKeySubjectPublicKeyInfoBase64 = publicKey.SubjectPublicKeyInfoBase64 ?? string.Empty,
            ExportedAtUtc = _clock.UtcNow.ToUniversalTime()
        };

        await WriteJsonFileAsync(outputPath, descriptor, cancellationToken);
        return PairingFileBootstrapResult.DescriptorExported(Path.GetFullPath(outputPath), descriptor);
    }

    public async Task<PairingFileBootstrapResult> AcceptChallengeAsync(
        string challengePath,
        string responseOutputPath,
        bool explicitApproval,
        CancellationToken cancellationToken)
    {
        PairingChallenge? challenge;
        try
        {
            await using var stream = new FileStream(
                Path.GetFullPath(challengePath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            challenge = await JsonSerializer.DeserializeAsync<PairingChallenge>(
                stream,
                ReadOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return PairingFileBootstrapResult.Failed(
                "PAIRING_ACCEPT_CHALLENGE",
                PairingConstants.ChallengeInvalidErrorCode,
                $"Pairing challenge file could not be read: {exception.Message}");
        }

        if (challenge is null)
        {
            return PairingFileBootstrapResult.Failed(
                "PAIRING_ACCEPT_CHALLENGE",
                PairingConstants.ChallengeInvalidErrorCode,
                "Pairing challenge file is empty.");
        }

        var local = await ResolveLocalIdentityAsync("PAIRING_ACCEPT_CHALLENGE", cancellationToken);
        if (!local.Succeeded)
        {
            return local.Error!;
        }

        var result = await _pairingService.AcceptChallengeAsync(
            challenge,
            local.InstallationIdentity!,
            local.NetworkIdentity!,
            explicitApproval,
            cancellationToken);

        if (!result.Accepted)
        {
            return PairingFileBootstrapResult.Failed(
                "PAIRING_ACCEPT_CHALLENGE",
                result.ErrorCode ?? PairingConstants.ChallengeInvalidErrorCode,
                result.ErrorMessage ?? "Pairing challenge was rejected.",
                result.Status.ToCode());
        }

        await WriteJsonFileAsync(responseOutputPath, result.Response!, cancellationToken);
        return PairingFileBootstrapResult.ChallengeAccepted(Path.GetFullPath(responseOutputPath), result.Response!);
    }

    public string SerializeResult(PairingFileBootstrapResult result)
    {
        return JsonSerializer.Serialize(result, WriteOptions);
    }

    private async Task<LocalIdentityResolution> ResolveLocalIdentityAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        var installation = await _installationIdentityResolver.ResolveAsync(cancellationToken);
        if (installation.Status != InstallationIdentityResolutionStatus.Ready)
        {
            return LocalIdentityResolution.Failed(PairingFileBootstrapResult.Failed(
                operation,
                PairingConstants.NetworkIdentityUnavailableErrorCode,
                installation.ErrorMessage ?? "Installation Identity is unavailable."));
        }

        var network = await _networkIdentityResolver.ResolveAsync(
            installation.Identity!,
            cancellationToken);
        if (network.Status != NetworkIdentityStatus.Ready)
        {
            return LocalIdentityResolution.Failed(PairingFileBootstrapResult.Failed(
                operation,
                network.ErrorCode ?? PairingConstants.NetworkIdentityUnavailableErrorCode,
                network.ErrorMessage ?? "Client Network Identity is unavailable."));
        }

        return LocalIdentityResolution.Success(installation.Identity!, network.Metadata!);
    }

    private static async Task WriteJsonFileAsync(
        string outputPath,
        object payload,
        CancellationToken cancellationToken)
    {
        var targetPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Output path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(payload, WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken);
            DurableFileWriter.ReplaceOrMove(tempPath, targetPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed record LocalIdentityResolution(
        bool Succeeded,
        InstallationIdentity? InstallationIdentity,
        NetworkIdentityMetadata? NetworkIdentity,
        PairingFileBootstrapResult? Error)
    {
        public static LocalIdentityResolution Success(
            InstallationIdentity installationIdentity,
            NetworkIdentityMetadata networkIdentity)
        {
            return new LocalIdentityResolution(true, installationIdentity, networkIdentity, null);
        }

        public static LocalIdentityResolution Failed(PairingFileBootstrapResult error)
        {
            return new LocalIdentityResolution(false, null, null, error);
        }
    }
}
