using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public sealed record ManagedWindowsCredentialStoreOptions(string DataDirectory);

public enum ManagedWindowsCredentialStoreReadStatus
{
    Loaded,
    Invalid
}

public sealed record ManagedWindowsCredentialStoreReadResult(
    ManagedWindowsCredentialStoreReadStatus Status,
    IReadOnlyList<ManagedWindowsCredentialEntry> Entries,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Loaded => Status == ManagedWindowsCredentialStoreReadStatus.Loaded;

    public static ManagedWindowsCredentialStoreReadResult LoadedEntries(
        IReadOnlyList<ManagedWindowsCredentialEntry> entries,
        string filePath)
    {
        return new ManagedWindowsCredentialStoreReadResult(
            ManagedWindowsCredentialStoreReadStatus.Loaded,
            entries,
            filePath,
            null,
            null);
    }

    public static ManagedWindowsCredentialStoreReadResult Invalid(
        string filePath,
        string errorMessage)
    {
        return new ManagedWindowsCredentialStoreReadResult(
            ManagedWindowsCredentialStoreReadStatus.Invalid,
            Array.Empty<ManagedWindowsCredentialEntry>(),
            filePath,
            ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
            errorMessage);
    }
}

public enum ManagedWindowsCredentialStatus
{
    Usable,
    CredentialNotConfigured,
    AccountNotConfigured,
    AccountNotFound,
    BindingStoreInvalid,
    StoreInvalid,
    Invalid
}

public sealed record ManagedWindowsCredentialStatusResult(
    ManagedWindowsCredentialStatus Status,
    bool CredentialConfigured,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ManagedWindowsCredentialStatusResult Success(string filePath)
    {
        return new ManagedWindowsCredentialStatusResult(
            ManagedWindowsCredentialStatus.Usable,
            true,
            filePath,
            null,
            null);
    }

    public static ManagedWindowsCredentialStatusResult Failure(
        ManagedWindowsCredentialStatus status,
        string filePath,
        string errorCode,
        string errorMessage)
    {
        return new ManagedWindowsCredentialStatusResult(
            status,
            false,
            filePath,
            errorCode,
            errorMessage);
    }
}

public enum ManagedWindowsCredentialWriteStatus
{
    Configured,
    Removed,
    AlreadyExists,
    NotFound,
    AccountNotConfigured,
    AccountNotFound,
    BindingStoreInvalid,
    StoreInvalid,
    ProtectionFailed,
    Invalid,
    VerificationFailed
}

public sealed record ManagedWindowsCredentialWriteResult(
    ManagedWindowsCredentialWriteStatus Status,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => Status is ManagedWindowsCredentialWriteStatus.Configured
        or ManagedWindowsCredentialWriteStatus.Removed;

    public static ManagedWindowsCredentialWriteResult Success(
        ManagedWindowsCredentialWriteStatus status,
        string filePath)
    {
        return new ManagedWindowsCredentialWriteResult(status, filePath, null, null);
    }

    public static ManagedWindowsCredentialWriteResult Failure(
        ManagedWindowsCredentialWriteStatus status,
        string filePath,
        string errorCode,
        string errorMessage)
    {
        return new ManagedWindowsCredentialWriteResult(status, filePath, errorCode, errorMessage);
    }
}

public enum ManagedWindowsCredentialAcquireStatus
{
    Acquired,
    CredentialNotConfigured,
    AccountNotConfigured,
    AccountNotFound,
    BindingStoreInvalid,
    StoreInvalid,
    ProtectionFailed,
    Invalid
}

public sealed record ManagedWindowsCredentialAcquireResult(
    ManagedWindowsCredentialAcquireStatus Status,
    ManagedWindowsCredentialLease? Lease,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => Status == ManagedWindowsCredentialAcquireStatus.Acquired;

    public static ManagedWindowsCredentialAcquireResult Acquired(
        ManagedWindowsCredentialLease lease,
        string filePath)
    {
        return new ManagedWindowsCredentialAcquireResult(
            ManagedWindowsCredentialAcquireStatus.Acquired,
            lease,
            filePath,
            null,
            null);
    }

    public static ManagedWindowsCredentialAcquireResult Failure(
        ManagedWindowsCredentialAcquireStatus status,
        string filePath,
        string errorCode,
        string errorMessage)
    {
        return new ManagedWindowsCredentialAcquireResult(status, null, filePath, errorCode, errorMessage);
    }
}

public sealed class ManagedWindowsCredentialLease : IDisposable
{
    private byte[] _passwordUtf16LittleEndian;
    private bool _disposed;

    internal ManagedWindowsCredentialLease(
        string accountId,
        string windowsSid,
        byte[] passwordUtf16LittleEndian)
    {
        AccountId = accountId;
        WindowsSid = windowsSid;
        _passwordUtf16LittleEndian = passwordUtf16LittleEndian;
    }

    public string AccountId { get; }

    public string WindowsSid { get; }

    public ReadOnlyMemory<byte> PasswordUtf16LittleEndian
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _passwordUtf16LittleEndian;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_passwordUtf16LittleEndian);
        _passwordUtf16LittleEndian = Array.Empty<byte>();
        _disposed = true;
    }

    public override string ToString()
    {
        return $"ManagedWindowsCredentialLease(AccountId={AccountId}, Disposed={_disposed})";
    }
}

public interface IManagedWindowsCredentialStore
{
    string FilePath { get; }

    Task<ManagedWindowsCredentialStatusResult> GetStatusAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialWriteResult> AddAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<char> secret,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialWriteResult> AddUtf16LittleEndianAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<byte> secretUtf16LittleEndian,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialWriteResult> ReplaceAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<char> secret,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialWriteResult> ReplaceUtf16LittleEndianAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<byte> secretUtf16LittleEndian,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialWriteResult> RemoveAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialAcquireResult> AcquireAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken);

    Task<ManagedWindowsCredentialAcquireResult> AcquireForWindowsSidAsync(
        Guid currentInstallationId,
        string accountId,
        string expectedWindowsSid,
        CancellationToken cancellationToken);
}

public sealed class ManagedWindowsCredentialStore : IManagedWindowsCredentialStore
{
    private static readonly byte[] PayloadMagic = "GCMWC"u8.ToArray();
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ManagedWindowsCredentialDocumentGuardConverter() }
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _filePath;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly IWindowsAccountResolver _accountResolver;
    private readonly IManagedWindowsCredentialProtector _protector;
    private readonly IManagedWindowsCredentialFileSecurity _fileSecurity;
    private readonly ISystemClock _clock;

    public ManagedWindowsCredentialStore(
        ManagedWindowsCredentialStoreOptions options,
        IManagedWindowsAccountBindingStore bindingStore,
        IWindowsAccountResolver accountResolver,
        IManagedWindowsCredentialProtector protector,
        IManagedWindowsCredentialFileSecurity fileSecurity,
        ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(bindingStore);
        ArgumentNullException.ThrowIfNull(accountResolver);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(fileSecurity);
        ArgumentNullException.ThrowIfNull(clock);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, ManagedWindowsCredentialConstants.FileName);
        _bindingStore = bindingStore;
        _accountResolver = accountResolver;
        _protector = protector;
        _fileSecurity = fileSecurity;
        _clock = clock;
    }

    public string FilePath => _filePath;

    public async Task<ManagedWindowsCredentialStatusResult> GetStatusAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return ManagedWindowsCredentialStatusResult.Failure(
                ManagedWindowsCredentialStatus.Invalid,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialInvalid,
                idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return ManagedWindowsCredentialStatusResult.Failure(
                ManagedWindowsCredentialStatus.StoreInvalid,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                load.ErrorMessage ?? "Managed Windows credential store is invalid.");
        }

        var context = await ResolveCredentialContextAsync(
            currentInstallationId,
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
        {
            return ToStatusFailure(context);
        }

        var entry = Find(load.Entries, context.Binding!.AccountId);
        if (entry is null)
        {
            return ManagedWindowsCredentialStatusResult.Failure(
                ManagedWindowsCredentialStatus.CredentialNotConfigured,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "Managed Windows credential is not configured.");
        }

        var acquire = UnprotectEntry(currentInstallationId, context.Binding, entry);
        if (!acquire.Succeeded)
        {
            return acquire.Status == ManagedWindowsCredentialAcquireStatus.CredentialNotConfigured
                ? ManagedWindowsCredentialStatusResult.Failure(
                    ManagedWindowsCredentialStatus.CredentialNotConfigured,
                    _filePath,
                    ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                    "Managed Windows credential is not usable for the current binding.")
                : ManagedWindowsCredentialStatusResult.Failure(
                    ManagedWindowsCredentialStatus.StoreInvalid,
                    _filePath,
                    acquire.ErrorCode ?? ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                    acquire.ErrorMessage ?? "Managed Windows credential store is invalid.");
        }

        acquire.Lease!.Dispose();
        return ManagedWindowsCredentialStatusResult.Success(_filePath);
    }

    public Task<ManagedWindowsCredentialWriteResult> AddAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<char> secret,
        CancellationToken cancellationToken)
    {
        return AddOrReplaceAsync(currentInstallationId, accountId, secret, replaceExisting: false, cancellationToken);
    }

    public Task<ManagedWindowsCredentialWriteResult> AddUtf16LittleEndianAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<byte> secretUtf16LittleEndian,
        CancellationToken cancellationToken)
    {
        return AddOrReplaceUtf16LittleEndianAsync(
            currentInstallationId,
            accountId,
            secretUtf16LittleEndian,
            replaceExisting: false,
            cancellationToken);
    }

    public Task<ManagedWindowsCredentialWriteResult> ReplaceAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<char> secret,
        CancellationToken cancellationToken)
    {
        return AddOrReplaceAsync(currentInstallationId, accountId, secret, replaceExisting: true, cancellationToken);
    }

    public Task<ManagedWindowsCredentialWriteResult> ReplaceUtf16LittleEndianAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<byte> secretUtf16LittleEndian,
        CancellationToken cancellationToken)
    {
        return AddOrReplaceUtf16LittleEndianAsync(
            currentInstallationId,
            accountId,
            secretUtf16LittleEndian,
            replaceExisting: true,
            cancellationToken);
    }

    public async Task<ManagedWindowsCredentialWriteResult> RemoveAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return InvalidWrite(idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalidWrite(load);
        }

        if (Find(load.Entries, normalizedAccountId) is null)
        {
            return ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.NotFound,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "Managed Windows credential was not found.");
        }

        var entries = load.Entries
            .Where(entry => !SameAccountId(entry.AccountId, normalizedAccountId))
            .ToArray();

        return await PersistAsync(
            currentInstallationId,
            entries,
            ManagedWindowsCredentialWriteStatus.Removed,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManagedWindowsCredentialAcquireResult> AcquireAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        return await AcquireCoreAsync(
            currentInstallationId,
            accountId,
            expectedWindowsSid: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManagedWindowsCredentialAcquireResult> AcquireForWindowsSidAsync(
        Guid currentInstallationId,
        string accountId,
        string expectedWindowsSid,
        CancellationToken cancellationToken)
    {
        if (!MasterBindingValidator.IsValidSid(expectedWindowsSid))
        {
            return ManagedWindowsCredentialAcquireResult.Failure(
                ManagedWindowsCredentialAcquireStatus.Invalid,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialInvalid,
                "Managed Windows expected SID is invalid.");
        }

        return await AcquireCoreAsync(
            currentInstallationId,
            accountId,
            expectedWindowsSid,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedWindowsCredentialAcquireResult> AcquireCoreAsync(
        Guid currentInstallationId,
        string accountId,
        string? expectedWindowsSid,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return ManagedWindowsCredentialAcquireResult.Failure(
                ManagedWindowsCredentialAcquireStatus.Invalid,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialInvalid,
                idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return ManagedWindowsCredentialAcquireResult.Failure(
                ManagedWindowsCredentialAcquireStatus.StoreInvalid,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                load.ErrorMessage ?? "Managed Windows credential store is invalid.");
        }

        var context = await ResolveCredentialContextAsync(
            currentInstallationId,
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
        {
            return ToAcquireFailure(context);
        }

        if (expectedWindowsSid is not null
            && !string.Equals(context.Binding!.WindowsSid, expectedWindowsSid, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedWindowsCredentialAcquireResult.Failure(
                ManagedWindowsCredentialAcquireStatus.CredentialNotConfigured,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "Managed Windows credential is not usable for the expected SID.");
        }

        var entry = Find(load.Entries, context.Binding!.AccountId);
        if (entry is null)
        {
            return ManagedWindowsCredentialAcquireResult.Failure(
                ManagedWindowsCredentialAcquireStatus.CredentialNotConfigured,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "Managed Windows credential is not configured.");
        }

        return UnprotectEntry(currentInstallationId, context.Binding, entry);
    }

    private async Task<ManagedWindowsCredentialWriteResult> AddOrReplaceAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<char> secret,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return InvalidWrite(idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalidWrite(load);
        }

        var context = await ResolveCredentialContextAsync(
            currentInstallationId,
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
        {
            return ToWriteFailure(context);
        }

        if (secret.Length > ManagedWindowsCredentialConstants.MaximumPasswordCharacters)
        {
            return InvalidWrite("Managed Windows credential exceeds maximum length.");
        }

        var existing = Find(load.Entries, context.Binding!.AccountId);
        if (existing is not null && !replaceExisting)
        {
            return ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.AlreadyExists,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialAlreadyExists,
                "Managed Windows credential already exists. Use explicit replace to overwrite it.");
        }

        byte[]? payload = null;
        byte[]? entropy = null;
        try
        {
            payload = BuildPayload(context.Binding.AccountId, context.Binding.WindowsSid, secret.Span);
            entropy = DeriveOptionalEntropy(currentInstallationId, context.Binding.AccountId);
            var protectedResult = _protector.Protect(payload, entropy);
            if (!protectedResult.Succeeded || protectedResult.Data.Length == 0)
            {
                return ManagedWindowsCredentialWriteResult.Failure(
                    ManagedWindowsCredentialWriteStatus.ProtectionFailed,
                    _filePath,
                    ManagedWindowsCredentialErrorCodes.ManagedCredentialProtectionFailed,
                    "Managed Windows credential protection failed.");
            }

            var now = _clock.UtcNow.ToUniversalTime();
            var entry = new ManagedWindowsCredentialEntry(
                context.Binding.AccountId,
                Convert.ToBase64String(protectedResult.Data),
                existing?.CreatedAtUtc ?? now,
                now);

            var entries = existing is null
                ? load.Entries.Concat([entry]).ToArray()
                : load.Entries
                    .Select(current => SameAccountId(current.AccountId, context.Binding.AccountId) ? entry : current)
                    .ToArray();

            return await PersistAsync(
                currentInstallationId,
                entries,
                ManagedWindowsCredentialWriteStatus.Configured,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }

            if (entropy is not null)
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
        }
    }

    private async Task<ManagedWindowsCredentialWriteResult> AddOrReplaceUtf16LittleEndianAsync(
        Guid currentInstallationId,
        string accountId,
        ReadOnlyMemory<byte> secretUtf16LittleEndian,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return InvalidWrite(idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalidWrite(load);
        }

        var context = await ResolveCredentialContextAsync(
            currentInstallationId,
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
        {
            return ToWriteFailure(context);
        }

        var secretValidation = ValidateUtf16LittleEndianSecret(secretUtf16LittleEndian);
        if (!secretValidation.IsValid)
        {
            return InvalidWrite(secretValidation.ErrorMessage ?? "Managed Windows credential is invalid.");
        }

        var existing = Find(load.Entries, context.Binding!.AccountId);
        if (existing is not null && !replaceExisting)
        {
            return ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.AlreadyExists,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialAlreadyExists,
                "Managed Windows credential already exists. Use explicit replace to overwrite it.");
        }

        byte[]? payload = null;
        byte[]? entropy = null;
        try
        {
            payload = BuildPayloadFromUtf16LittleEndian(
                context.Binding.AccountId,
                context.Binding.WindowsSid,
                secretUtf16LittleEndian.Span);
            entropy = DeriveOptionalEntropy(currentInstallationId, context.Binding.AccountId);
            var protectedResult = _protector.Protect(payload, entropy);
            if (!protectedResult.Succeeded || protectedResult.Data.Length == 0)
            {
                return ManagedWindowsCredentialWriteResult.Failure(
                    ManagedWindowsCredentialWriteStatus.ProtectionFailed,
                    _filePath,
                    ManagedWindowsCredentialErrorCodes.ManagedCredentialProtectionFailed,
                    "Managed Windows credential protection failed.");
            }

            var now = _clock.UtcNow.ToUniversalTime();
            var entry = new ManagedWindowsCredentialEntry(
                context.Binding.AccountId,
                Convert.ToBase64String(protectedResult.Data),
                existing?.CreatedAtUtc ?? now,
                now);

            var entries = existing is null
                ? load.Entries.Concat([entry]).ToArray()
                : load.Entries
                    .Select(current => SameAccountId(current.AccountId, context.Binding.AccountId) ? entry : current)
                    .ToArray();

            return await PersistAsync(
                currentInstallationId,
                entries,
                ManagedWindowsCredentialWriteStatus.Configured,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }

            if (entropy is not null)
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
        }
    }

    private async Task<ManagedWindowsCredentialContextResult> ResolveCredentialContextAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return ManagedWindowsCredentialContextResult.Failure(
                ManagedWindowsCredentialStatus.Invalid,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialInvalid,
                idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var bindings = await _bindingStore.LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!bindings.Loaded)
        {
            return ManagedWindowsCredentialContextResult.Failure(
                ManagedWindowsCredentialStatus.BindingStoreInvalid,
                ManagedWindowsCredentialErrorCodes.ManagedAccountBindingsInvalid,
                bindings.ErrorMessage ?? "Managed Windows account bindings are invalid.");
        }

        var binding = bindings.Bindings.FirstOrDefault(current => SameAccountId(current.AccountId, normalizedAccountId));
        if (binding is null)
        {
            return ManagedWindowsCredentialContextResult.Failure(
                ManagedWindowsCredentialStatus.AccountNotConfigured,
                ManagedWindowsCredentialErrorCodes.AccountNotConfigured,
                "Managed Windows account binding is not configured.");
        }

        var resolved = _accountResolver.ResolveSid(binding.WindowsSid);
        var foundUser = resolved.Found
            && resolved.Identity is not null
            && resolved.Identity.SidNameUse == WindowsAccountSidNameUse.User
            && string.Equals(resolved.Identity.WindowsSid, binding.WindowsSid, StringComparison.OrdinalIgnoreCase);
        if (!foundUser)
        {
            return ManagedWindowsCredentialContextResult.Failure(
                ManagedWindowsCredentialStatus.AccountNotFound,
                ManagedWindowsCredentialErrorCodes.AccountNotFound,
                "Managed Windows account SID no longer resolves to a User account.");
        }

        return ManagedWindowsCredentialContextResult.Success(binding);
    }

    private ManagedWindowsCredentialAcquireResult UnprotectEntry(
        Guid currentInstallationId,
        ManagedWindowsAccountBinding binding,
        ManagedWindowsCredentialEntry entry)
    {
        byte[] protectedData;
        try
        {
            protectedData = Convert.FromBase64String(entry.ProtectedData);
        }
        catch (FormatException)
        {
            return ManagedWindowsCredentialAcquireResult.Failure(
                ManagedWindowsCredentialAcquireStatus.StoreInvalid,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                "Managed Windows credential store is invalid.");
        }

        byte[]? entropy = null;
        byte[]? payload = null;
        try
        {
            entropy = DeriveOptionalEntropy(currentInstallationId, binding.AccountId);
            var unprotected = _protector.Unprotect(protectedData, entropy);
            if (!unprotected.Succeeded || unprotected.Data.Length == 0)
            {
                return ManagedWindowsCredentialAcquireResult.Failure(
                    ManagedWindowsCredentialAcquireStatus.ProtectionFailed,
                    _filePath,
                    ManagedWindowsCredentialErrorCodes.ManagedCredentialProtectionFailed,
                    "Managed Windows credential protection failed.");
            }

            payload = unprotected.Data;
            var parsed = TryParsePayload(payload, out var payloadAccountId, out var payloadWindowsSid, out var passwordBytes);
            if (!parsed)
            {
                return ManagedWindowsCredentialAcquireResult.Failure(
                    ManagedWindowsCredentialAcquireStatus.StoreInvalid,
                    _filePath,
                    ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                    "Managed Windows credential protected payload is invalid.");
            }

            if (!SameAccountId(payloadAccountId!, binding.AccountId)
                || !string.Equals(payloadWindowsSid, binding.WindowsSid, StringComparison.OrdinalIgnoreCase))
            {
                CryptographicOperations.ZeroMemory(passwordBytes!);
                return ManagedWindowsCredentialAcquireResult.Failure(
                    ManagedWindowsCredentialAcquireStatus.CredentialNotConfigured,
                    _filePath,
                    ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                    "Managed Windows credential is not usable for the current binding.");
            }

            return ManagedWindowsCredentialAcquireResult.Acquired(
                new ManagedWindowsCredentialLease(binding.AccountId, binding.WindowsSid, passwordBytes!),
                _filePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedData);

            if (entropy is not null)
            {
                CryptographicOperations.ZeroMemory(entropy);
            }

            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private async Task<ManagedWindowsCredentialStoreReadResult> LoadAsync(
        Guid currentInstallationId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return ManagedWindowsCredentialStoreReadResult.LoadedEntries(
                Array.Empty<ManagedWindowsCredentialEntry>(),
                _filePath);
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

            var document = await JsonSerializer.DeserializeAsync<ManagedWindowsCredentialCatalogDocument>(
                stream,
                ReadOptions,
                cancellationToken).ConfigureAwait(false);

            var validation = ValidateDocument(document, currentInstallationId);
            if (!validation.IsValid)
            {
                return ManagedWindowsCredentialStoreReadResult.Invalid(
                    _filePath,
                    validation.ErrorMessage ?? "managed-windows-credentials.dat is invalid.");
            }

            return ManagedWindowsCredentialStoreReadResult.LoadedEntries(
                OrderEntries(document!.Entries),
                _filePath);
        }
        catch (JsonException exception)
        {
            return ManagedWindowsCredentialStoreReadResult.Invalid(
                _filePath,
                $"managed-windows-credentials.dat is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return ManagedWindowsCredentialStoreReadResult.Invalid(
                _filePath,
                $"managed-windows-credentials.dat could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ManagedWindowsCredentialStoreReadResult.Invalid(
                _filePath,
                $"managed-windows-credentials.dat could not be read: {exception.Message}");
        }
    }

    private async Task<ManagedWindowsCredentialWriteResult> PersistAsync(
        Guid currentInstallationId,
        IReadOnlyList<ManagedWindowsCredentialEntry> entries,
        ManagedWindowsCredentialWriteStatus successStatus,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);

        var document = new ManagedWindowsCredentialCatalogDocument(
            ManagedWindowsCredentialConstants.SchemaVersion,
            currentInstallationId,
            OrderEntries(entries));
        var validation = ValidateDocument(document, currentInstallationId);
        if (!validation.IsValid)
        {
            return InvalidWrite(validation.ErrorMessage ?? "Managed Windows credential store document is invalid.");
        }

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{ManagedWindowsCredentialConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(document, WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
            _fileSecurity.Apply(tempPath);
            DurableFileWriter.ReplaceOrMove(tempPath, _filePath);
            _fileSecurity.Apply(_filePath);

            var verification = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
            if (verification.Loaded && EntriesEqual(document.Entries, verification.Entries))
            {
                return ManagedWindowsCredentialWriteResult.Success(successStatus, _filePath);
            }

            return ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.VerificationFailed,
                _filePath,
                ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                verification.ErrorMessage ?? "Managed Windows credential store write verification failed.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static byte[] DeriveOptionalEntropy(Guid installationId, string accountId)
    {
        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        return Encoding.UTF8.GetBytes(
            $"{ManagedWindowsCredentialConstants.EntropyContextPrefix}|{installationId:D}|{normalizedAccountId}");
    }

    private static byte[] BuildPayload(
        string accountId,
        string windowsSid,
        ReadOnlySpan<char> password)
    {
        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var accountBytes = Encoding.UTF8.GetBytes(normalizedAccountId);
        var sidBytes = Encoding.UTF8.GetBytes(windowsSid);
        var passwordBytes = new byte[Encoding.Unicode.GetByteCount(password)];
        Encoding.Unicode.GetBytes(password, passwordBytes);
        try
        {
            var length = PayloadMagic.Length
                + sizeof(ushort)
                + sizeof(ushort)
                + accountBytes.Length
                + sizeof(ushort)
                + sidBytes.Length
                + sizeof(int)
                + passwordBytes.Length;
            var payload = new byte[length];
            var offset = 0;

            PayloadMagic.CopyTo(payload, offset);
            offset += PayloadMagic.Length;

            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), 1);
            offset += sizeof(ushort);

            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), checked((ushort)accountBytes.Length));
            offset += sizeof(ushort);
            accountBytes.CopyTo(payload.AsSpan(offset));
            offset += accountBytes.Length;

            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), checked((ushort)sidBytes.Length));
            offset += sizeof(ushort);
            sidBytes.CopyTo(payload.AsSpan(offset));
            offset += sidBytes.Length;

            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, sizeof(int)), passwordBytes.Length);
            offset += sizeof(int);
            passwordBytes.CopyTo(payload.AsSpan(offset));

            return payload;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] BuildPayloadFromUtf16LittleEndian(
        string accountId,
        string windowsSid,
        ReadOnlySpan<byte> passwordUtf16LittleEndian)
    {
        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var accountBytes = Encoding.UTF8.GetBytes(normalizedAccountId);
        var sidBytes = Encoding.UTF8.GetBytes(windowsSid);
        var length = PayloadMagic.Length
            + sizeof(ushort)
            + sizeof(ushort)
            + accountBytes.Length
            + sizeof(ushort)
            + sidBytes.Length
            + sizeof(int)
            + passwordUtf16LittleEndian.Length;
        var payload = new byte[length];
        var offset = 0;

        PayloadMagic.CopyTo(payload, offset);
        offset += PayloadMagic.Length;

        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), 1);
        offset += sizeof(ushort);

        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), checked((ushort)accountBytes.Length));
        offset += sizeof(ushort);
        accountBytes.CopyTo(payload.AsSpan(offset));
        offset += accountBytes.Length;

        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), checked((ushort)sidBytes.Length));
        offset += sizeof(ushort);
        sidBytes.CopyTo(payload.AsSpan(offset));
        offset += sidBytes.Length;

        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, sizeof(int)), passwordUtf16LittleEndian.Length);
        offset += sizeof(int);
        passwordUtf16LittleEndian.CopyTo(payload.AsSpan(offset));

        return payload;
    }

    private static ManagedWindowsCredentialValidationResult ValidateUtf16LittleEndianSecret(
        ReadOnlyMemory<byte> secretUtf16LittleEndian)
    {
        if (secretUtf16LittleEndian.Length == 0)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "Managed Windows credential is empty.");
        }

        if (secretUtf16LittleEndian.Length % 2 != 0)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "Managed Windows credential UTF-16LE bytes are invalid.");
        }

        if (secretUtf16LittleEndian.Length > ManagedWindowsCredentialConstants.MaximumPasswordCharacters * 2)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "Managed Windows credential exceeds maximum length.");
        }

        return ManagedWindowsCredentialValidationResult.Valid();
    }

    private static bool TryParsePayload(
        byte[] payload,
        out string? accountId,
        out string? windowsSid,
        out byte[]? passwordUtf16LittleEndian)
    {
        accountId = null;
        windowsSid = null;
        passwordUtf16LittleEndian = null;

        try
        {
            var offset = 0;
            if (payload.Length < PayloadMagic.Length + sizeof(ushort))
            {
                return false;
            }

            if (!payload.AsSpan(offset, PayloadMagic.Length).SequenceEqual(PayloadMagic))
            {
                return false;
            }

            offset += PayloadMagic.Length;
            var version = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            if (version != 1)
            {
                return false;
            }

            if (!TryReadUtf8String(payload, ref offset, out accountId)
                || !ManagedWindowsAccountBinding.IsValidAccountId(accountId))
            {
                return false;
            }

            if (!TryReadUtf8String(payload, ref offset, out windowsSid)
                || !MasterBindingValidator.IsValidSid(windowsSid))
            {
                return false;
            }

            if (payload.Length - offset < sizeof(int))
            {
                return false;
            }

            var passwordLength = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset, sizeof(int)));
            offset += sizeof(int);
            if (passwordLength < 0
                || passwordLength % 2 != 0
                || passwordLength > ManagedWindowsCredentialConstants.MaximumPasswordCharacters * 2
                || payload.Length - offset != passwordLength)
            {
                return false;
            }

            passwordUtf16LittleEndian = payload.AsSpan(offset, passwordLength).ToArray();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadUtf8String(byte[] payload, ref int offset, out string? value)
    {
        value = null;
        if (payload.Length - offset < sizeof(ushort))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        if (length == 0 || payload.Length - offset < length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(payload.AsSpan(offset, length));
        offset += length;
        return true;
    }

    private static ManagedWindowsCredentialValidationResult ValidateDocument(
        ManagedWindowsCredentialCatalogDocument? document,
        Guid currentInstallationId)
    {
        if (document is null)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat is empty.");
        }

        if (document.SchemaVersion != ManagedWindowsCredentialConstants.SchemaVersion)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat uses an unsupported schemaVersion.");
        }

        if (document.InstallationId == Guid.Empty)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat installationId is missing.");
        }

        if (document.InstallationId != currentInstallationId)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat belongs to another installationId.");
        }

        if (document.Entries is null)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat entries are missing.");
        }

        if (document.Entries.Count > ManagedWindowsAccountBindingValidator.OrderedSlots.Count)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat contains too many entries.");
        }

        var accountIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            var validation = ValidateEntry(entry);
            if (!validation.IsValid)
            {
                return validation;
            }

            if (!accountIds.Add(entry.AccountId))
            {
                return ManagedWindowsCredentialValidationResult.Invalid(
                    $"managed-windows-credentials.dat contains duplicate {entry.AccountId} credential.");
            }
        }

        return ManagedWindowsCredentialValidationResult.Valid();
    }

    private static ManagedWindowsCredentialValidationResult ValidateEntry(
        ManagedWindowsCredentialEntry? entry)
    {
        if (entry is null)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat contains an empty entry.");
        }

        if (!ManagedWindowsAccountBinding.IsValidAccountId(entry.AccountId)
            || !string.Equals(
                entry.AccountId,
                ManagedWindowsAccountBinding.NormalizeAccountId(entry.AccountId),
                StringComparison.Ordinal))
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat accountId must be canonical PRIMARY or SECONDARY.");
        }

        if (string.IsNullOrWhiteSpace(entry.ProtectedData))
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat protectedData is missing.");
        }

        try
        {
            if (Convert.FromBase64String(entry.ProtectedData).Length == 0)
            {
                return ManagedWindowsCredentialValidationResult.Invalid(
                    "managed-windows-credentials.dat protectedData is empty.");
            }
        }
        catch (FormatException)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat protectedData is not valid Base64.");
        }

        if (entry.CreatedAtUtc == default)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat createdAtUtc is missing.");
        }

        if (entry.UpdatedAtUtc == default)
        {
            return ManagedWindowsCredentialValidationResult.Invalid(
                "managed-windows-credentials.dat updatedAtUtc is missing.");
        }

        return ManagedWindowsCredentialValidationResult.Valid();
    }

    private ManagedWindowsCredentialWriteResult InvalidWrite(string message)
    {
        return ManagedWindowsCredentialWriteResult.Failure(
            ManagedWindowsCredentialWriteStatus.Invalid,
            _filePath,
            ManagedWindowsCredentialErrorCodes.ManagedCredentialInvalid,
            message);
    }

    private ManagedWindowsCredentialWriteResult StoreInvalidWrite(
        ManagedWindowsCredentialStoreReadResult load)
    {
        return ManagedWindowsCredentialWriteResult.Failure(
            ManagedWindowsCredentialWriteStatus.StoreInvalid,
            _filePath,
            ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
            load.ErrorMessage ?? "Managed Windows credential store is invalid.");
    }

    private ManagedWindowsCredentialWriteResult ToWriteFailure(
        ManagedWindowsCredentialContextResult context)
    {
        return ManagedWindowsCredentialWriteResult.Failure(
            context.Status switch
            {
                ManagedWindowsCredentialStatus.AccountNotConfigured => ManagedWindowsCredentialWriteStatus.AccountNotConfigured,
                ManagedWindowsCredentialStatus.AccountNotFound => ManagedWindowsCredentialWriteStatus.AccountNotFound,
                ManagedWindowsCredentialStatus.BindingStoreInvalid => ManagedWindowsCredentialWriteStatus.BindingStoreInvalid,
                _ => ManagedWindowsCredentialWriteStatus.Invalid
            },
            _filePath,
            context.ErrorCode,
            context.ErrorMessage);
    }

    private ManagedWindowsCredentialAcquireResult ToAcquireFailure(
        ManagedWindowsCredentialContextResult context)
    {
        return ManagedWindowsCredentialAcquireResult.Failure(
            context.Status switch
            {
                ManagedWindowsCredentialStatus.AccountNotConfigured => ManagedWindowsCredentialAcquireStatus.AccountNotConfigured,
                ManagedWindowsCredentialStatus.AccountNotFound => ManagedWindowsCredentialAcquireStatus.AccountNotFound,
                ManagedWindowsCredentialStatus.BindingStoreInvalid => ManagedWindowsCredentialAcquireStatus.BindingStoreInvalid,
                _ => ManagedWindowsCredentialAcquireStatus.Invalid
            },
            _filePath,
            context.ErrorCode,
            context.ErrorMessage);
    }

    private ManagedWindowsCredentialStatusResult ToStatusFailure(
        ManagedWindowsCredentialContextResult context)
    {
        return ManagedWindowsCredentialStatusResult.Failure(
            context.Status,
            _filePath,
            context.ErrorCode,
            context.ErrorMessage);
    }

    private static ManagedWindowsCredentialEntry? Find(
        IEnumerable<ManagedWindowsCredentialEntry> entries,
        string accountId)
    {
        return entries.FirstOrDefault(entry => SameAccountId(entry.AccountId, accountId));
    }

    private static bool SameAccountId(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static ManagedWindowsCredentialEntry[] OrderEntries(
        IEnumerable<ManagedWindowsCredentialEntry> entries)
    {
        return entries
            .OrderBy(entry => entry.AccountId == ClassroomManagedWindowsAccountTypes.Primary ? 0 : 1)
            .ToArray();
    }

    private static bool EntriesEqual(
        IReadOnlyList<ManagedWindowsCredentialEntry> expected,
        IReadOnlyList<ManagedWindowsCredentialEntry> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (expected[index] != actual[index])
            {
                return false;
            }
        }

        return true;
    }

    private sealed record ManagedWindowsCredentialContextResult(
        bool Succeeded,
        ManagedWindowsCredentialStatus Status,
        ManagedWindowsAccountBinding? Binding,
        string ErrorCode,
        string ErrorMessage)
    {
        public static ManagedWindowsCredentialContextResult Success(ManagedWindowsAccountBinding binding)
        {
            return new ManagedWindowsCredentialContextResult(
                true,
                ManagedWindowsCredentialStatus.Usable,
                binding,
                string.Empty,
                string.Empty);
        }

        public static ManagedWindowsCredentialContextResult Failure(
            ManagedWindowsCredentialStatus status,
            string errorCode,
            string errorMessage)
        {
            return new ManagedWindowsCredentialContextResult(
                false,
                status,
                null,
                errorCode,
                errorMessage);
        }
    }

    private sealed record ManagedWindowsCredentialValidationResult(bool IsValid, string? ErrorMessage)
    {
        public static ManagedWindowsCredentialValidationResult Valid()
        {
            return new ManagedWindowsCredentialValidationResult(true, null);
        }

        public static ManagedWindowsCredentialValidationResult Invalid(string errorMessage)
        {
            return new ManagedWindowsCredentialValidationResult(false, errorMessage);
        }
    }
}
