using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GaltekClassroom.Agent.Service.BrowserPolicy;

internal static class BrowserDownloadRegistryConstants
{
    public const string DownloadRestrictionsValueName = "DownloadRestrictions";
    public const string DwordValueKindName = "DWord";
}

public sealed record ChromiumDownloadPolicyCompilerResult(
    bool Succeeded,
    CompiledBrowserDownloadPolicy? Policy,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static ChromiumDownloadPolicyCompilerResult Success(CompiledBrowserDownloadPolicy policy)
    {
        return new ChromiumDownloadPolicyCompilerResult(true, policy, NetworkOperationErrorCode.Unspecified, string.Empty);
    }

    public static ChromiumDownloadPolicyCompilerResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new ChromiumDownloadPolicyCompilerResult(false, null, errorCode, message);
    }
}

public sealed record CompiledBrowserDownloadPolicy(
    string PolicyId,
    long PolicyVersion,
    BrowserDownloadRestrictionMode RestrictionMode,
    BrowserPolicyAccountScope AccountScope,
    bool RemoveGaltekPolicy,
    int? NativeDownloadRestrictionsValue,
    string ContentHash);

public sealed class ChromiumDownloadPolicyCompiler
{
    public ChromiumDownloadPolicyCompilerResult Compile(ApplyBrowserDownloadPolicyOperationParameters? parameters)
    {
        if (parameters is null)
        {
            return Invalid("Browser download policy parameters are required.");
        }

        if (parameters.RestrictionMode == BrowserDownloadRestrictionMode.Unspecified)
        {
            return Invalid("Browser download restriction mode is required.");
        }

        if (parameters.AccountScope == BrowserPolicyAccountScope.Unspecified)
        {
            return Invalid("Browser download policy account scope is required.");
        }

        if (parameters.ImplicitNoSpecialRestrictions
            && parameters.RestrictionMode != BrowserDownloadRestrictionMode.NoSpecialRestrictions)
        {
            return Invalid("Implicit browser download policy must use NO_SPECIAL_RESTRICTIONS.");
        }

        if (!parameters.ImplicitNoSpecialRestrictions)
        {
            if (string.IsNullOrWhiteSpace(parameters.PolicyId))
            {
                return Invalid("Browser download policy id is required.");
            }

            if (parameters.PolicyVersion <= 0)
            {
                return Invalid("Browser download policy version is invalid.");
            }
        }
        else if (parameters.PolicyVersion < 0)
        {
            return Invalid("Browser download policy version is invalid.");
        }

        int? nativeValue = parameters.ImplicitNoSpecialRestrictions
            ? null
            : NativeValue(parameters.RestrictionMode);
        if (!parameters.ImplicitNoSpecialRestrictions && nativeValue is null)
        {
            return Invalid("Browser download restriction mode is invalid.");
        }

        bool removeGaltekPolicy = parameters.ImplicitNoSpecialRestrictions;
        string policyId = removeGaltekPolicy && string.IsNullOrWhiteSpace(parameters.PolicyId)
            ? "implicit-no-special-restrictions"
            : parameters.PolicyId.Trim();
        string contentHash = Hash(removeGaltekPolicy, nativeValue, parameters.AccountScope);

        return ChromiumDownloadPolicyCompilerResult.Success(new CompiledBrowserDownloadPolicy(
            policyId,
            parameters.PolicyVersion,
            parameters.RestrictionMode,
            parameters.AccountScope,
            removeGaltekPolicy,
            nativeValue,
            contentHash));
    }

    private static int? NativeValue(BrowserDownloadRestrictionMode restrictionMode)
    {
        return restrictionMode switch
        {
            BrowserDownloadRestrictionMode.NoSpecialRestrictions => 0,
            BrowserDownloadRestrictionMode.BlockDangerous => 1,
            BrowserDownloadRestrictionMode.BlockPotentiallyDangerous => 2,
            BrowserDownloadRestrictionMode.BlockAll => 3,
            BrowserDownloadRestrictionMode.BlockMalicious => 4,
            _ => null
        };
    }

    private static string Hash(
        bool implicitNoSpecialRestrictions,
        int? nativeValue,
        BrowserPolicyAccountScope accountScope)
    {
        var builder = new StringBuilder();
        builder.Append("schema=1\n");
        builder.Append("implicit=").Append(implicitNoSpecialRestrictions ? "true" : "false").Append('\n');
        builder.Append("accountScope=").Append(accountScope).Append('\n');
        if (nativeValue is not null)
        {
            builder.Append("nativeValue=").Append(nativeValue.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ChromiumDownloadPolicyCompilerResult Invalid(string message)
    {
        return ChromiumDownloadPolicyCompilerResult.Failure(
            NetworkOperationErrorCode.BrowserDownloadPolicyInvalid,
            message);
    }
}

public enum BrowserDownloadPolicyBrowser
{
    Chrome,
    Edge
}

public sealed record BrowserDownloadBrowserValue(
    bool Exists,
    string? ValueKind,
    int? DwordValue)
{
    public static BrowserDownloadBrowserValue Missing { get; } = new(false, null, null);

    public bool MatchesDesired(int? desiredValue)
    {
        return desiredValue is null
            ? !Exists
            : Exists
              && string.Equals(ValueKind, BrowserDownloadRegistryConstants.DwordValueKindName, StringComparison.Ordinal)
              && DwordValue == desiredValue.Value;
    }

    public bool ContentEquals(BrowserDownloadBrowserValue other)
    {
        return Exists == other.Exists
            && string.Equals(ValueKind, other.ValueKind, StringComparison.Ordinal)
            && DwordValue == other.DwordValue;
    }
}

public sealed record BrowserDownloadParentSnapshot(
    bool Exists,
    string[] ValueNames,
    string[] SubKeyNames,
    bool AclSafe,
    string? SecurityDescriptor);

public sealed record BrowserDownloadBrowserSnapshot(
    BrowserDownloadBrowserValue Value,
    BrowserDownloadParentSnapshot Parent);

public sealed record BrowserDownloadRegistrySnapshot(
    BrowserDownloadBrowserSnapshot Chrome,
    BrowserDownloadBrowserSnapshot Edge)
{
    public bool HasAnyDownloadRestrictions => Chrome.Value.Exists || Edge.Value.Exists;

    public bool ValuesEqual(BrowserDownloadRegistrySnapshot other)
    {
        return Chrome.Value.ContentEquals(other.Chrome.Value)
            && Edge.Value.ContentEquals(other.Edge.Value);
    }

    public bool MatchesDesired(CompiledBrowserDownloadPolicy desired)
    {
        return Chrome.Value.MatchesDesired(desired.NativeDownloadRestrictionsValue)
            && Edge.Value.MatchesDesired(desired.NativeDownloadRestrictionsValue);
    }

    public static BrowserDownloadRegistrySnapshot FromPolicy(CompiledBrowserDownloadPolicy policy)
    {
        BrowserDownloadBrowserValue value = policy.RemoveGaltekPolicy
            ? BrowserDownloadBrowserValue.Missing
            : new BrowserDownloadBrowserValue(true, BrowserDownloadRegistryConstants.DwordValueKindName, policy.NativeDownloadRestrictionsValue);
        return new BrowserDownloadRegistrySnapshot(
            new BrowserDownloadBrowserSnapshot(value, EmptyParentSnapshot),
            new BrowserDownloadBrowserSnapshot(value, EmptyParentSnapshot));
    }

    private static BrowserDownloadParentSnapshot EmptyParentSnapshot { get; } = new(false, [], [], false, null);
}

public sealed record BrowserDownloadParentPlan(
    bool ParentExistedBeforeGaltek,
    bool ParentAclHardenedByGaltek,
    string? PreviousParentSecurityDescriptor);

public sealed record BrowserDownloadPolicyBrowserState(
    bool ParentExistedBeforeGaltek,
    bool ParentAclHardenedByGaltek,
    string? PreviousParentSecurityDescriptor);

public sealed record BrowserDownloadPolicyStateEntry(
    string WindowsSid,
    string PolicyId,
    long PolicyVersion,
    string ContentHash,
    bool ImplicitRemoval,
    int? NativeValue,
    DateTimeOffset AppliedAtUtc,
    BrowserDownloadPolicyBrowserState Chrome,
    BrowserDownloadPolicyBrowserState Edge);

public sealed record BrowserDownloadPolicyApplyJournalEntry(
    string WindowsSid,
    string DesiredContentHash,
    BrowserDownloadPolicyStateEntry? PreviousState,
    BrowserDownloadPolicyStateEntry? DesiredState,
    BrowserDownloadRegistrySnapshot PreviousRegistry,
    BrowserDownloadRegistrySnapshot DesiredRegistry,
    string Phase,
    DateTimeOffset StartedAtUtc);

public interface IBrowserNavigationOwnershipReader
{
    Task<bool> IsGaltekOwnedNavigationSubkeyAsync(
        string windowsSid,
        string subKeyName,
        CancellationToken cancellationToken);
}

public sealed class BrowserNavigationOwnershipReader : IBrowserNavigationOwnershipReader
{
    private static readonly HashSet<string> NavigationSubKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "URLBlocklist",
        "URLAllowlist"
    };

    private readonly BrowserNavigationPolicyStateStore _stateStore;

    public BrowserNavigationOwnershipReader(BrowserNavigationPolicyStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<bool> IsGaltekOwnedNavigationSubkeyAsync(
        string windowsSid,
        string subKeyName,
        CancellationToken cancellationToken)
    {
        if (!NavigationSubKeys.Contains(subKeyName))
        {
            return false;
        }

        BrowserNavigationPolicyStateEntry? state = await _stateStore.TryGetUserStateAsync(
            windowsSid,
            cancellationToken).ConfigureAwait(false);
        return state is not null;
    }
}

public interface IBrowserDownloadPolicyRegistryStore
{
    Task<BrowserDownloadRegistrySnapshot> ReadUserPolicyAsync(
        string windowsSid,
        CancellationToken cancellationToken);

    Task<bool> HasMachineLevelDownloadRestrictionsAsync(CancellationToken cancellationToken);

    Task<BrowserDownloadParentPlan> PlanParentAclAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        CancellationToken cancellationToken);

    Task WriteDownloadRestrictionsAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        int? nativeValue,
        BrowserDownloadParentPlan parentPlan,
        CancellationToken cancellationToken);

    Task RestoreBrowserAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        BrowserDownloadBrowserSnapshot snapshot,
        bool restoreParentAcl,
        CancellationToken cancellationToken);
}

public sealed class BrowserDownloadPolicyRegistryException : Exception
{
    public BrowserDownloadPolicyRegistryException(NetworkOperationErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public NetworkOperationErrorCode ErrorCode { get; }
}

public sealed class WindowsBrowserDownloadPolicyRegistryStore : IBrowserDownloadPolicyRegistryStore
{
    private const string ValueName = BrowserDownloadRegistryConstants.DownloadRestrictionsValueName;

    private static readonly IReadOnlyDictionary<BrowserDownloadPolicyBrowser, string> ParentPaths =
        new Dictionary<BrowserDownloadPolicyBrowser, string>
        {
            [BrowserDownloadPolicyBrowser.Chrome] = "Software\\Policies\\Google\\Chrome",
            [BrowserDownloadPolicyBrowser.Edge] = "Software\\Policies\\Microsoft\\Edge"
        };

    [SupportedOSPlatform("windows")]
    public Task<BrowserDownloadRegistrySnapshot> ReadUserPolicyAsync(
        string windowsSid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? sidRoot = OpenUserSidRoot(windowsSid, writable: false);
        return Task.FromResult(new BrowserDownloadRegistrySnapshot(
            ReadBrowser(sidRoot, BrowserDownloadPolicyBrowser.Chrome, windowsSid),
            ReadBrowser(sidRoot, BrowserDownloadPolicyBrowser.Edge, windowsSid)));
    }

    [SupportedOSPlatform("windows")]
    public Task<bool> HasMachineLevelDownloadRestrictionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (string parentPath in ParentPaths.Values)
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(parentPath, writable: false);
            if (key?.GetValue(ValueName) is not null)
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    [SupportedOSPlatform("windows")]
    public Task<BrowserDownloadParentPlan> PlanParentAclAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? sidRoot = OpenUserSidRoot(windowsSid, writable: false);
        using RegistryKey? parent = sidRoot.OpenSubKey(ParentPaths[browser], writable: false);
        if (parent is null)
        {
            return Task.FromResult(new BrowserDownloadParentPlan(false, true, null));
        }

        string? descriptor = ReadSecurityDescriptor(parent);
        bool alreadySafe = IsParentAclSafe(parent, windowsSid);
        return Task.FromResult(new BrowserDownloadParentPlan(true, !alreadySafe, alreadySafe ? null : descriptor));
    }

    [SupportedOSPlatform("windows")]
    public Task WriteDownloadRestrictionsAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        int? nativeValue,
        BrowserDownloadParentPlan parentPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? sidRoot = OpenUserSidRoot(windowsSid, writable: true);
        using RegistryKey? parent = sidRoot.CreateSubKey(ParentPaths[browser], RegistryKeyPermissionCheck.ReadWriteSubTree);
        if (parent is null)
        {
            throw new BrowserDownloadPolicyRegistryException(
                NetworkOperationErrorCode.BrowserPolicyUserHiveUnavailable,
                "Interactive user policy hive is unavailable.");
        }

        if (parentPlan.ParentAclHardenedByGaltek)
        {
            ApplyParentOnlyAcl(parent, windowsSid);
        }

        if (nativeValue is null)
        {
            parent.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            parent.SetValue(ValueName, nativeValue.Value, RegistryValueKind.DWord);
        }

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    public Task RestoreBrowserAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        BrowserDownloadBrowserSnapshot snapshot,
        bool restoreParentAcl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? sidRoot = OpenUserSidRoot(windowsSid, writable: true);
        if (!snapshot.Parent.Exists)
        {
            using RegistryKey? parent = sidRoot.OpenSubKey(ParentPaths[browser], writable: true);
            parent?.DeleteValue(ValueName, throwOnMissingValue: false);
            TryDeleteEmptyParent(sidRoot, browser);
            return Task.CompletedTask;
        }

        using RegistryKey? restored = sidRoot.CreateSubKey(
            ParentPaths[browser],
            RegistryKeyPermissionCheck.ReadWriteSubTree);
        if (restored is null)
        {
            throw new IOException("User policy hive is unavailable.");
        }

        if (snapshot.Value.Exists && snapshot.Value.DwordValue is not null)
        {
            restored.SetValue(ValueName, snapshot.Value.DwordValue.Value, RegistryValueKind.DWord);
        }
        else
        {
            restored.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        if (restoreParentAcl && !string.IsNullOrWhiteSpace(snapshot.Parent.SecurityDescriptor))
        {
            RestoreSecurityDescriptor(restored, snapshot.Parent.SecurityDescriptor);
        }

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey OpenUserSidRoot(string windowsSid, bool writable)
    {
        using RegistryKey users = Registry.Users;
        RegistryKey? sidRoot = users.OpenSubKey(windowsSid, writable);
        if (sidRoot is null)
        {
            throw new BrowserDownloadPolicyRegistryException(
                NetworkOperationErrorCode.BrowserPolicyUserHiveUnavailable,
                "Interactive user policy hive is unavailable.");
        }

        return sidRoot;
    }

    [SupportedOSPlatform("windows")]
    private static BrowserDownloadBrowserSnapshot ReadBrowser(
        RegistryKey sidRoot,
        BrowserDownloadPolicyBrowser browser,
        string windowsSid)
    {
        using RegistryKey? parent = sidRoot.OpenSubKey(ParentPaths[browser], writable: false);
        if (parent is null)
        {
            return new BrowserDownloadBrowserSnapshot(
                BrowserDownloadBrowserValue.Missing,
                new BrowserDownloadParentSnapshot(false, [], [], false, null));
        }

        object? value = parent.GetValue(ValueName);
        RegistryValueKind kind = value is null
            ? RegistryValueKind.Unknown
            : parent.GetValueKind(ValueName);
        var browserValue = value is null
            ? BrowserDownloadBrowserValue.Missing
            : new BrowserDownloadBrowserValue(
                true,
                kind.ToString(),
                kind == RegistryValueKind.DWord ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null);

        return new BrowserDownloadBrowserSnapshot(
            browserValue,
            new BrowserDownloadParentSnapshot(
                true,
                parent.GetValueNames().OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                parent.GetSubKeyNames().OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                IsParentAclSafe(parent, windowsSid),
                ReadSecurityDescriptor(parent)));
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadSecurityDescriptor(RegistryKey key)
    {
        return key.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
    }

    [SupportedOSPlatform("windows")]
    private static void RestoreSecurityDescriptor(RegistryKey key, string descriptor)
    {
        var security = new RegistrySecurity();
        security.SetSecurityDescriptorSddlForm(descriptor, AccessControlSections.All);
        key.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsParentAclSafe(RegistryKey key, string? windowsSid)
    {
        if (string.IsNullOrWhiteSpace(windowsSid))
        {
            return false;
        }

        RegistrySecurity security = key.GetAccessControl();
        var targetSid = new SecurityIdentifier(windowsSid);
        bool systemFull = false;
        bool adminsFull = false;
        bool userRead = false;
        bool userWrites = false;
        bool broadWriter = false;

        foreach (RegistryAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var identity = (SecurityIdentifier)rule.IdentityReference;
            RegistryRights rights = rule.RegistryRights;
            if (identity.IsWellKnown(WellKnownSidType.LocalSystemSid) && rights.HasFlag(RegistryRights.FullControl))
            {
                systemFull = true;
            }

            if (identity.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && rights.HasFlag(RegistryRights.FullControl))
            {
                adminsFull = true;
            }

            if (identity.Equals(targetSid))
            {
                if (rights.HasFlag(RegistryRights.ReadKey))
                {
                    userRead = true;
                }

                if (HasWriteRights(rights))
                {
                    userWrites = true;
                }
            }

            if ((identity.IsWellKnown(WellKnownSidType.WorldSid)
                 || identity.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)
                 || identity.IsWellKnown(WellKnownSidType.BuiltinUsersSid))
                && HasWriteRights(rights))
            {
                broadWriter = true;
            }
        }

        return systemFull && adminsFull && userRead && !userWrites && !broadWriter;
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyParentOnlyAcl(RegistryKey key, string windowsSid)
    {
        var security = new RegistrySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            RegistryRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            RegistryRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(windowsSid),
            RegistryRights.ReadKey,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        key.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool HasWriteRights(RegistryRights rights)
    {
        const RegistryRights writeMask =
            RegistryRights.SetValue
            | RegistryRights.CreateSubKey
            | RegistryRights.WriteKey
            | RegistryRights.ChangePermissions
            | RegistryRights.TakeOwnership
            | RegistryRights.FullControl;
        return (rights & writeMask) != 0;
    }

    [SupportedOSPlatform("windows")]
    private static void TryDeleteEmptyParent(RegistryKey sidRoot, BrowserDownloadPolicyBrowser browser)
    {
        try
        {
            using RegistryKey? parent = sidRoot.OpenSubKey(ParentPaths[browser], writable: false);
            if (parent is null || parent.GetValueNames().Length > 0 || parent.GetSubKeyNames().Length > 0)
            {
                return;
            }

            sidRoot.DeleteSubKey(ParentPaths[browser], throwOnMissingSubKey: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }

}

public sealed class BrowserDownloadPolicyStateStore
{
    public const string StateFileName = "browser-download-policy-state.json";
    public const string JournalFileName = "browser-download-policy-apply.json";
    private const int SchemaVersion = 1;
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _statePath;
    private readonly string _journalPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BrowserDownloadPolicyStateStore(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _statePath = Path.Combine(_dataDirectory, StateFileName);
        _journalPath = Path.Combine(_dataDirectory, JournalFileName);
    }

    public async Task<BrowserDownloadPolicyStateEntry?> TryGetUserStateAsync(
        string windowsSid,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BrowserDownloadPolicyStateDocument document = await ReadStateDocumentAsync(cancellationToken).ConfigureAwait(false);
            return document.Users.FirstOrDefault(user => string.Equals(user.WindowsSid, windowsSid, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveUserStateAsync(
        BrowserDownloadPolicyStateEntry entry,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BrowserDownloadPolicyStateDocument document = await ReadStateDocumentAsync(cancellationToken).ConfigureAwait(false);
            BrowserDownloadPolicyStateEntry[] users = document.Users
                .Where(user => !string.Equals(user.WindowsSid, entry.WindowsSid, StringComparison.Ordinal))
                .Append(entry)
                .OrderBy(user => user.WindowsSid, StringComparer.Ordinal)
                .ToArray();
            await WriteJsonAsync(_statePath, new BrowserDownloadPolicyStateDocument(SchemaVersion, users), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserDownloadPolicyApplyJournalEntry?> TryReadJournalAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_journalPath))
            {
                return null;
            }

            await using var stream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            BrowserDownloadPolicyApplyJournalDocument? document = await JsonSerializer.DeserializeAsync<BrowserDownloadPolicyApplyJournalDocument>(
                stream,
                ReadOptions,
                cancellationToken).ConfigureAwait(false);
            return document?.SchemaVersion == SchemaVersion ? document.Entry : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveJournalAsync(
        BrowserDownloadPolicyApplyJournalEntry entry,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteJsonAsync(
                _journalPath,
                new BrowserDownloadPolicyApplyJournalDocument(SchemaVersion, entry),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void DeleteJournal()
    {
        if (File.Exists(_journalPath))
        {
            File.Delete(_journalPath);
        }
    }

    private async Task<BrowserDownloadPolicyStateDocument> ReadStateDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new BrowserDownloadPolicyStateDocument(SchemaVersion, []);
        }

        await using var stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        BrowserDownloadPolicyStateDocument? document = await JsonSerializer.DeserializeAsync<BrowserDownloadPolicyStateDocument>(
            stream,
            ReadOptions,
            cancellationToken).ConfigureAwait(false);
        return document?.SchemaVersion == SchemaVersion && document.Users is not null
            ? document
            : new BrowserDownloadPolicyStateDocument(SchemaVersion, []);
    }

    private async Task WriteJsonAsync<T>(string targetPath, T document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        string tempPath = Path.Combine(_dataDirectory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            string json = JsonSerializer.Serialize(document, WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
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

    private sealed record BrowserDownloadPolicyStateDocument(
        int SchemaVersion,
        BrowserDownloadPolicyStateEntry[] Users);

    private sealed record BrowserDownloadPolicyApplyJournalDocument(
        int SchemaVersion,
        BrowserDownloadPolicyApplyJournalEntry? Entry);
}

public sealed record BrowserDownloadPolicyApplyResult(
    bool Succeeded,
    bool NoChange,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static BrowserDownloadPolicyApplyResult Success(bool noChange = false)
    {
        return new BrowserDownloadPolicyApplyResult(true, noChange, NetworkOperationErrorCode.Unspecified, string.Empty);
    }

    public static BrowserDownloadPolicyApplyResult Failure(NetworkOperationErrorCode errorCode, string message)
    {
        return new BrowserDownloadPolicyApplyResult(false, false, errorCode, message);
    }
}

public sealed class BrowserDownloadPolicyApplyService
{
    private const string PhasePlanned = "planned";
    private const string PhaseApplying = "applying";
    private static readonly HashSet<string> GaltekNavigationSubKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "URLBlocklist",
        "URLAllowlist"
    };

    private readonly IBrowserDownloadPolicyRegistryStore _registry;
    private readonly BrowserDownloadPolicyStateStore _stateStore;
    private readonly IBrowserNavigationOwnershipReader _navigationOwnership;
    private readonly ISystemClock _clock;

    public BrowserDownloadPolicyApplyService(
        IBrowserDownloadPolicyRegistryStore registry,
        BrowserDownloadPolicyStateStore stateStore,
        IBrowserNavigationOwnershipReader navigationOwnership,
        ISystemClock clock)
    {
        _registry = registry;
        _stateStore = stateStore;
        _navigationOwnership = navigationOwnership;
        _clock = clock;
    }

    public async Task<BrowserDownloadPolicyApplyResult> ApplyAsync(
        string windowsSid,
        CompiledBrowserDownloadPolicy desiredPolicy,
        CancellationToken cancellationToken)
    {
        BrowserDownloadPolicyApplyResult recovery = await RecoverIfNeededAsync(cancellationToken).ConfigureAwait(false);
        if (!recovery.Succeeded)
        {
            return recovery;
        }

        BrowserDownloadPolicyStateEntry? previousState = await _stateStore.TryGetUserStateAsync(
            windowsSid,
            cancellationToken).ConfigureAwait(false);
        BrowserDownloadRegistrySnapshot current = await _registry.ReadUserPolicyAsync(
            windowsSid,
            cancellationToken).ConfigureAwait(false);

        if (!desiredPolicy.RemoveGaltekPolicy
            && await _registry.HasMachineLevelDownloadRestrictionsAsync(cancellationToken).ConfigureAwait(false))
        {
            return BrowserDownloadPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict,
                "Machine-level browser download policy already exists.");
        }

        BrowserDownloadRegistrySnapshot expectedFromState = previousState is null
            ? MissingPolicy()
            : SnapshotFromState(previousState);

        if (previousState is null)
        {
            if (desiredPolicy.RemoveGaltekPolicy && !current.HasAnyDownloadRestrictions)
            {
                return BrowserDownloadPolicyApplyResult.Success(noChange: true);
            }

            if (current.HasAnyDownloadRestrictions)
            {
                return BrowserDownloadPolicyApplyResult.Failure(
                    NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict,
                    "Existing browser download policy is not owned by Galtek.");
            }

            BrowserDownloadPolicyApplyResult parentSafety = await EnsureParentsSafeForFirstTakeoverAsync(
                windowsSid,
                current,
                cancellationToken).ConfigureAwait(false);
            if (!parentSafety.Succeeded)
            {
                return parentSafety;
            }
        }
        else if (!current.ValuesEqual(expectedFromState))
        {
            return BrowserDownloadPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict,
                "Browser download policy registry diverged from Galtek state.");
        }

        BrowserDownloadRegistrySnapshot desiredRegistry = BrowserDownloadRegistrySnapshot.FromPolicy(desiredPolicy);
        if (previousState is not null
            && string.Equals(previousState.ContentHash, desiredPolicy.ContentHash, StringComparison.Ordinal)
            && current.ValuesEqual(desiredRegistry)
            && ParentAclStateMatches(current, previousState))
        {
            return BrowserDownloadPolicyApplyResult.Success(noChange: true);
        }

        BrowserDownloadParentPlan chromePlan = await _registry.PlanParentAclAsync(
            windowsSid,
            BrowserDownloadPolicyBrowser.Chrome,
            cancellationToken).ConfigureAwait(false);
        BrowserDownloadParentPlan edgePlan = await _registry.PlanParentAclAsync(
            windowsSid,
            BrowserDownloadPolicyBrowser.Edge,
            cancellationToken).ConfigureAwait(false);

        BrowserDownloadPolicyStateEntry desiredState = BuildState(
            windowsSid,
            desiredPolicy,
            chromePlan,
            edgePlan);

        await _stateStore.SaveJournalAsync(
            new BrowserDownloadPolicyApplyJournalEntry(
                windowsSid,
                desiredPolicy.ContentHash,
                previousState,
                desiredState,
                current,
                desiredRegistry,
                PhasePlanned,
                _clock.UtcNow.ToUniversalTime()),
            cancellationToken).ConfigureAwait(false);

        BrowserDownloadPolicyApplyResult applied = await ApplyWithRollbackAsync(
            windowsSid,
            desiredPolicy.NativeDownloadRestrictionsValue,
            current,
            chromePlan,
            edgePlan,
            cancellationToken).ConfigureAwait(false);
        if (!applied.Succeeded)
        {
            return applied;
        }

        BrowserDownloadRegistrySnapshot verified = await _registry.ReadUserPolicyAsync(
            windowsSid,
            cancellationToken).ConfigureAwait(false);
        if (!verified.ValuesEqual(desiredRegistry))
        {
            BrowserDownloadPolicyApplyResult rollback = await RollbackAsync(
                windowsSid,
                current,
                previousState,
                cancellationToken).ConfigureAwait(false);
            return rollback.Succeeded
                ? BrowserDownloadPolicyApplyResult.Failure(
                    NetworkOperationErrorCode.BrowserDownloadPolicyApplyFailed,
                    "Browser download policy registry verification failed.")
                : rollback;
        }

        if (desiredPolicy.RemoveGaltekPolicy
            && previousState is not null
            && NeedsAclRestore(previousState)
            && !await IsSafeToRestoreParentAclsAsync(windowsSid, verified, cancellationToken).ConfigureAwait(false))
        {
            await _stateStore.SaveJournalAsync(
                new BrowserDownloadPolicyApplyJournalEntry(
                    windowsSid,
                    desiredPolicy.ContentHash,
                    previousState,
                    desiredState,
                    current,
                    desiredRegistry,
                    PhaseApplying,
                    _clock.UtcNow.ToUniversalTime()),
                cancellationToken).ConfigureAwait(false);
            return BrowserDownloadPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserDownloadPolicyRecoveryRequired,
                "Browser download policy ACL restore requires manual recovery.");
        }

        if (desiredPolicy.RemoveGaltekPolicy
            && previousState is not null
            && NeedsAclRestore(previousState))
        {
            BrowserDownloadPolicyApplyResult aclRestore = await RestorePreviousParentAclsAfterRemovalAsync(
                windowsSid,
                verified,
                previousState,
                desiredRegistry,
                cancellationToken).ConfigureAwait(false);
            if (!aclRestore.Succeeded)
            {
                await _stateStore.SaveJournalAsync(
                    new BrowserDownloadPolicyApplyJournalEntry(
                        windowsSid,
                        desiredPolicy.ContentHash,
                        previousState,
                        desiredState,
                        current,
                        desiredRegistry,
                        PhaseApplying,
                        _clock.UtcNow.ToUniversalTime()),
                    cancellationToken).ConfigureAwait(false);
                return aclRestore;
            }
        }

        await _stateStore.SaveUserStateAsync(desiredState, cancellationToken).ConfigureAwait(false);
        _stateStore.DeleteJournal();
        return BrowserDownloadPolicyApplyResult.Success();
    }

    private async Task<BrowserDownloadPolicyApplyResult> EnsureParentsSafeForFirstTakeoverAsync(
        string windowsSid,
        BrowserDownloadRegistrySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        BrowserDownloadPolicyApplyResult chrome = await EnsureParentSafeAsync(
            windowsSid,
            snapshot.Chrome.Parent,
            cancellationToken).ConfigureAwait(false);
        if (!chrome.Succeeded)
        {
            return chrome;
        }

        return await EnsureParentSafeAsync(windowsSid, snapshot.Edge.Parent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BrowserDownloadPolicyApplyResult> EnsureParentSafeAsync(
        string windowsSid,
        BrowserDownloadParentSnapshot parent,
        CancellationToken cancellationToken)
    {
        if (!parent.Exists)
        {
            return BrowserDownloadPolicyApplyResult.Success(noChange: true);
        }

        string[] unknownValues = parent.ValueNames
            .Where(name => !string.Equals(name, "DownloadRestrictions", StringComparison.Ordinal))
            .ToArray();
        if (unknownValues.Length > 0)
        {
            return BrowserDownloadPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict,
                "Browser download policy parent key contains external values.");
        }

        foreach (string subKeyName in parent.SubKeyNames)
        {
            if (!GaltekNavigationSubKeys.Contains(subKeyName)
                || !await _navigationOwnership.IsGaltekOwnedNavigationSubkeyAsync(
                    windowsSid,
                    subKeyName,
                    cancellationToken).ConfigureAwait(false))
            {
                return BrowserDownloadPolicyApplyResult.Failure(
                    NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict,
                    "Browser download policy parent key contains external subkeys.");
            }
        }

        return BrowserDownloadPolicyApplyResult.Success(noChange: true);
    }

    private async Task<BrowserDownloadPolicyApplyResult> ApplyWithRollbackAsync(
        string windowsSid,
        int? nativeValue,
        BrowserDownloadRegistrySnapshot previous,
        BrowserDownloadParentPlan chromePlan,
        BrowserDownloadParentPlan edgePlan,
        CancellationToken cancellationToken)
    {
        try
        {
            await _registry.WriteDownloadRestrictionsAsync(
                windowsSid,
                BrowserDownloadPolicyBrowser.Chrome,
                nativeValue,
                chromePlan,
                cancellationToken).ConfigureAwait(false);
            await VerifyBrowserAsync(windowsSid, BrowserDownloadPolicyBrowser.Chrome, nativeValue, cancellationToken)
                .ConfigureAwait(false);

            await _registry.WriteDownloadRestrictionsAsync(
                windowsSid,
                BrowserDownloadPolicyBrowser.Edge,
                nativeValue,
                edgePlan,
                cancellationToken).ConfigureAwait(false);
            await VerifyBrowserAsync(windowsSid, BrowserDownloadPolicyBrowser.Edge, nativeValue, cancellationToken)
                .ConfigureAwait(false);
            return BrowserDownloadPolicyApplyResult.Success();
        }
        catch (Exception exception) when (exception is BrowserDownloadPolicyRegistryException or IOException or UnauthorizedAccessException or SecurityException)
        {
            BrowserDownloadPolicyApplyResult rollback = await RollbackAsync(
                windowsSid,
                previous,
                null,
                cancellationToken).ConfigureAwait(false);
            return rollback.Succeeded
                ? BrowserDownloadPolicyApplyResult.Failure(
                    NetworkOperationErrorCode.BrowserDownloadPolicyApplyFailed,
                    "Browser download policy registry apply failed and rollback succeeded.")
                : rollback;
        }
    }

    private async Task VerifyBrowserAsync(
        string windowsSid,
        BrowserDownloadPolicyBrowser browser,
        int? nativeValue,
        CancellationToken cancellationToken)
    {
        BrowserDownloadRegistrySnapshot snapshot = await _registry.ReadUserPolicyAsync(
            windowsSid,
            cancellationToken).ConfigureAwait(false);
        BrowserDownloadBrowserValue value = browser == BrowserDownloadPolicyBrowser.Chrome
            ? snapshot.Chrome.Value
            : snapshot.Edge.Value;
        if (!value.MatchesDesired(nativeValue))
        {
            throw new BrowserDownloadPolicyRegistryException(
                NetworkOperationErrorCode.BrowserDownloadPolicyApplyFailed,
                "Browser download policy registry verification failed.");
        }
    }

    private async Task<BrowserDownloadPolicyApplyResult> RollbackAsync(
        string windowsSid,
        BrowserDownloadRegistrySnapshot previous,
        BrowserDownloadPolicyStateEntry? previousState,
        CancellationToken cancellationToken)
    {
        bool restoreChromeAcl = previousState?.Chrome.ParentAclHardenedByGaltek == true;
        bool restoreEdgeAcl = previousState?.Edge.ParentAclHardenedByGaltek == true;

        try
        {
            await _registry.RestoreBrowserAsync(
                windowsSid,
                BrowserDownloadPolicyBrowser.Chrome,
                previous.Chrome,
                restoreChromeAcl,
                cancellationToken).ConfigureAwait(false);
            await _registry.RestoreBrowserAsync(
                windowsSid,
                BrowserDownloadPolicyBrowser.Edge,
                previous.Edge,
                restoreEdgeAcl,
                cancellationToken).ConfigureAwait(false);
            BrowserDownloadRegistrySnapshot verified = await _registry.ReadUserPolicyAsync(
                windowsSid,
                cancellationToken).ConfigureAwait(false);
            return verified.ValuesEqual(previous)
                ? BrowserDownloadPolicyApplyResult.Success(noChange: true)
                : BrowserDownloadPolicyApplyResult.Failure(
                    NetworkOperationErrorCode.BrowserDownloadPolicyRollbackFailed,
                    "Browser download policy rollback could not be verified.");
        }
        catch (Exception exception) when (exception is BrowserDownloadPolicyRegistryException or IOException or UnauthorizedAccessException or SecurityException)
        {
            return BrowserDownloadPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserDownloadPolicyRollbackFailed,
                "Browser download policy rollback failed.");
        }
    }

    private async Task<BrowserDownloadPolicyApplyResult> RecoverIfNeededAsync(CancellationToken cancellationToken)
    {
        BrowserDownloadPolicyApplyJournalEntry? journal = await _stateStore.TryReadJournalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (journal is null)
        {
            return BrowserDownloadPolicyApplyResult.Success(noChange: true);
        }

        BrowserDownloadRegistrySnapshot current = await _registry.ReadUserPolicyAsync(
            journal.WindowsSid,
            cancellationToken).ConfigureAwait(false);
        if (current.ValuesEqual(journal.DesiredRegistry) && journal.DesiredState is not null)
        {
            await _stateStore.SaveUserStateAsync(journal.DesiredState, cancellationToken).ConfigureAwait(false);
            _stateStore.DeleteJournal();
            return BrowserDownloadPolicyApplyResult.Success(noChange: true);
        }

        if (current.ValuesEqual(journal.PreviousRegistry))
        {
            _stateStore.DeleteJournal();
            return BrowserDownloadPolicyApplyResult.Success(noChange: true);
        }

        if (SnapshotContainsOnlyKnownDownloadValues(current, journal.PreviousRegistry, journal.DesiredRegistry))
        {
            BrowserDownloadPolicyApplyResult rollback = await RollbackAsync(
                journal.WindowsSid,
                journal.PreviousRegistry,
                journal.PreviousState,
                cancellationToken).ConfigureAwait(false);
            if (rollback.Succeeded)
            {
                _stateStore.DeleteJournal();
                return BrowserDownloadPolicyApplyResult.Success(noChange: true);
            }
        }

        return BrowserDownloadPolicyApplyResult.Failure(
            NetworkOperationErrorCode.BrowserDownloadPolicyRecoveryRequired,
            "Browser download policy registry requires manual recovery.");
    }

    private async Task<bool> IsSafeToRestoreParentAclsAsync(
        string windowsSid,
        BrowserDownloadRegistrySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        BrowserDownloadPolicyApplyResult chrome = await EnsureParentSafeAsync(
            windowsSid,
            snapshot.Chrome.Parent,
            cancellationToken).ConfigureAwait(false);
        if (!chrome.Succeeded)
        {
            return false;
        }

        BrowserDownloadPolicyApplyResult edge = await EnsureParentSafeAsync(
            windowsSid,
            snapshot.Edge.Parent,
            cancellationToken).ConfigureAwait(false);
        return edge.Succeeded;
    }

    private async Task<BrowserDownloadPolicyApplyResult> RestorePreviousParentAclsAfterRemovalAsync(
        string windowsSid,
        BrowserDownloadRegistrySnapshot current,
        BrowserDownloadPolicyStateEntry previousState,
        BrowserDownloadRegistrySnapshot desiredRegistry,
        CancellationToken cancellationToken)
    {
        try
        {
            if (previousState.Chrome.ParentAclHardenedByGaltek
                && !string.IsNullOrWhiteSpace(previousState.Chrome.PreviousParentSecurityDescriptor))
            {
                await _registry.RestoreBrowserAsync(
                    windowsSid,
                    BrowserDownloadPolicyBrowser.Chrome,
                    current.Chrome with
                    {
                        Parent = current.Chrome.Parent with
                        {
                            SecurityDescriptor = previousState.Chrome.PreviousParentSecurityDescriptor
                        }
                    },
                    restoreParentAcl: true,
                    cancellationToken).ConfigureAwait(false);
            }

            if (previousState.Edge.ParentAclHardenedByGaltek
                && !string.IsNullOrWhiteSpace(previousState.Edge.PreviousParentSecurityDescriptor))
            {
                await _registry.RestoreBrowserAsync(
                    windowsSid,
                    BrowserDownloadPolicyBrowser.Edge,
                    current.Edge with
                    {
                        Parent = current.Edge.Parent with
                        {
                            SecurityDescriptor = previousState.Edge.PreviousParentSecurityDescriptor
                        }
                    },
                    restoreParentAcl: true,
                    cancellationToken).ConfigureAwait(false);
            }

            BrowserDownloadRegistrySnapshot verified = await _registry.ReadUserPolicyAsync(
                windowsSid,
                cancellationToken).ConfigureAwait(false);
            return verified.ValuesEqual(desiredRegistry)
                ? BrowserDownloadPolicyApplyResult.Success(noChange: true)
                : BrowserDownloadPolicyApplyResult.Failure(
                    NetworkOperationErrorCode.BrowserDownloadPolicyRecoveryRequired,
                    "Browser download policy ACL restore verification failed.");
        }
        catch (Exception exception) when (exception is BrowserDownloadPolicyRegistryException or IOException or UnauthorizedAccessException or SecurityException)
        {
            return BrowserDownloadPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserDownloadPolicyRecoveryRequired,
                "Browser download policy ACL restore failed.");
        }
    }

    private BrowserDownloadPolicyStateEntry BuildState(
        string windowsSid,
        CompiledBrowserDownloadPolicy policy,
        BrowserDownloadParentPlan chromePlan,
        BrowserDownloadParentPlan edgePlan)
    {
        return new BrowserDownloadPolicyStateEntry(
            windowsSid,
            policy.PolicyId,
            policy.PolicyVersion,
            policy.ContentHash,
            policy.RemoveGaltekPolicy,
            policy.NativeDownloadRestrictionsValue,
            _clock.UtcNow.ToUniversalTime(),
            new BrowserDownloadPolicyBrowserState(
                chromePlan.ParentExistedBeforeGaltek,
                !policy.RemoveGaltekPolicy && chromePlan.ParentAclHardenedByGaltek,
                !policy.RemoveGaltekPolicy ? chromePlan.PreviousParentSecurityDescriptor : null),
            new BrowserDownloadPolicyBrowserState(
                edgePlan.ParentExistedBeforeGaltek,
                !policy.RemoveGaltekPolicy && edgePlan.ParentAclHardenedByGaltek,
                !policy.RemoveGaltekPolicy ? edgePlan.PreviousParentSecurityDescriptor : null));
    }

    private static bool ParentAclStateMatches(
        BrowserDownloadRegistrySnapshot current,
        BrowserDownloadPolicyStateEntry state)
    {
        return (!state.Chrome.ParentAclHardenedByGaltek || current.Chrome.Parent.AclSafe)
            && (!state.Edge.ParentAclHardenedByGaltek || current.Edge.Parent.AclSafe);
    }

    private static bool NeedsAclRestore(BrowserDownloadPolicyStateEntry state)
    {
        return state.Chrome.ParentAclHardenedByGaltek || state.Edge.ParentAclHardenedByGaltek;
    }

    private static bool SnapshotContainsOnlyKnownDownloadValues(
        BrowserDownloadRegistrySnapshot current,
        BrowserDownloadRegistrySnapshot previous,
        BrowserDownloadRegistrySnapshot desired)
    {
        return Known(current.Chrome.Value, previous.Chrome.Value, desired.Chrome.Value)
            && Known(current.Edge.Value, previous.Edge.Value, desired.Edge.Value);

        static bool Known(
            BrowserDownloadBrowserValue current,
            BrowserDownloadBrowserValue previous,
            BrowserDownloadBrowserValue desired)
        {
            return current.ContentEquals(previous) || current.ContentEquals(desired);
        }
    }

    private static BrowserDownloadRegistrySnapshot SnapshotFromState(BrowserDownloadPolicyStateEntry state)
    {
        BrowserDownloadBrowserValue value = state.ImplicitRemoval
            ? BrowserDownloadBrowserValue.Missing
            : new BrowserDownloadBrowserValue(true, BrowserDownloadRegistryConstants.DwordValueKindName, state.NativeValue);
        return new BrowserDownloadRegistrySnapshot(
            new BrowserDownloadBrowserSnapshot(value, EmptyParentSnapshot(state.Chrome)),
            new BrowserDownloadBrowserSnapshot(value, EmptyParentSnapshot(state.Edge)));
    }

    private static BrowserDownloadRegistrySnapshot MissingPolicy()
    {
        return new BrowserDownloadRegistrySnapshot(
            new BrowserDownloadBrowserSnapshot(BrowserDownloadBrowserValue.Missing, new BrowserDownloadParentSnapshot(false, [], [], false, null)),
            new BrowserDownloadBrowserSnapshot(BrowserDownloadBrowserValue.Missing, new BrowserDownloadParentSnapshot(false, [], [], false, null)));
    }

    private static BrowserDownloadParentSnapshot EmptyParentSnapshot(BrowserDownloadPolicyBrowserState state)
    {
        return new BrowserDownloadParentSnapshot(
            state.ParentExistedBeforeGaltek,
            [],
            [],
            state.ParentAclHardenedByGaltek,
            state.PreviousParentSecurityDescriptor);
    }
}

public sealed class ApplyBrowserDownloadPolicyOperationHandler : IRemoteOperationHandler
{
    private readonly ChromiumDownloadPolicyCompiler _compiler;
    private readonly IInteractiveUserIdentityResolver _identityResolver;
    private readonly BrowserDownloadPolicyApplyService _applyService;
    private readonly ILogger<ApplyBrowserDownloadPolicyOperationHandler> _logger;

    public ApplyBrowserDownloadPolicyOperationHandler(
        ChromiumDownloadPolicyCompiler compiler,
        IInteractiveUserIdentityResolver identityResolver,
        BrowserDownloadPolicyApplyService applyService,
        ILogger<ApplyBrowserDownloadPolicyOperationHandler> logger)
    {
        _compiler = compiler;
        _identityResolver = identityResolver;
        _applyService = applyService;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.ApplyBrowserDownloadPolicy;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        ApplyBrowserDownloadPolicyOperationParameters? parameters = request.ApplyBrowserDownloadPolicy;
        if (parameters is null)
        {
            return Failed(NetworkOperationErrorCode.BrowserDownloadPolicyInvalid, "Browser download policy parameters are required.");
        }

        if (parameters.AccountScope is BrowserPolicyAccountScope.Primary or BrowserPolicyAccountScope.Secondary)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserAccountScopeUnresolved,
                "Browser download policy account scope is not resolved on this Agent.");
        }

        if (parameters.AccountScope != BrowserPolicyAccountScope.Any)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserDownloadPolicyInvalid,
                "Browser download policy account scope is invalid.");
        }

        ChromiumDownloadPolicyCompilerResult compiled = _compiler.Compile(parameters);
        if (!compiled.Succeeded || compiled.Policy is null)
        {
            return Failed(compiled.ErrorCode, compiled.Message);
        }

        InteractiveUserIdentityResult identity = await _identityResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!identity.Succeeded || string.IsNullOrWhiteSpace(identity.WindowsSid))
        {
            return Failed(identity.ErrorCode, identity.Message);
        }

        BrowserDownloadPolicyApplyResult applied;
        try
        {
            applied = await _applyService.ApplyAsync(
                identity.WindowsSid,
                compiled.Policy,
                cancellationToken).ConfigureAwait(false);
        }
        catch (BrowserDownloadPolicyRegistryException exception)
        {
            return Failed(exception.ErrorCode, exception.Message);
        }
        catch (JsonException)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserDownloadPolicyRecoveryRequired,
                "Browser download policy local state requires recovery.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserDownloadPolicyApplyFailed,
                "Browser download policy local state could not be persisted.");
        }

        if (!applied.Succeeded)
        {
            return Failed(applied.ErrorCode, applied.Message);
        }

        _logger.LogInformation(
            "Applied browser download policy. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}; PolicyId: {PolicyId}; NoChange: {NoChange}",
            request.OperationId,
            request.TargetDeviceId,
            compiled.Policy.PolicyId,
            applied.NoChange);

        return RemoteOperationHandlerResult.Success(
            applied.NoChange
                ? "Browser download policy was already applied."
                : "Browser download policy was applied.");
    }

    private static RemoteOperationHandlerResult Failed(NetworkOperationErrorCode errorCode, string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            errorCode,
            message);
    }
}
