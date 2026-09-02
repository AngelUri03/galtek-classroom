using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Win32;

namespace GaltekClassroom.Agent.Service.BrowserPolicy;

public sealed record ChromiumBrowserPolicyCompilerResult(
    bool Succeeded,
    CompiledBrowserPolicy? Policy,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static ChromiumBrowserPolicyCompilerResult Success(CompiledBrowserPolicy policy)
    {
        return new ChromiumBrowserPolicyCompilerResult(true, policy, NetworkOperationErrorCode.Unspecified, string.Empty);
    }

    public static ChromiumBrowserPolicyCompilerResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new ChromiumBrowserPolicyCompilerResult(false, null, errorCode, message);
    }
}

public sealed record CompiledBrowserPolicy(
    string PolicyId,
    long PolicyVersion,
    BrowserPolicyMode Mode,
    string[] BlockFilters,
    string[] AllowFilters,
    string ContentHash);

public sealed class ChromiumBrowserPolicyCompiler
{
    public const int MaxFiltersPerList = 1000;

    public ChromiumBrowserPolicyCompilerResult Compile(ApplyBrowserPolicyOperationParameters? parameters)
    {
        if (parameters is null)
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyInvalid,
                "Browser policy parameters are required.");
        }

        if (parameters.Mode == BrowserPolicyMode.Unspecified)
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyInvalid,
                "Browser policy mode is required.");
        }

        if (string.IsNullOrWhiteSpace(parameters.PolicyId) && !parameters.ImplicitUnrestricted)
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyInvalid,
                "Browser policy id is required.");
        }

        var blockFilters = new List<string>();
        var allowFilters = new List<string>();

        if (parameters.Mode == BrowserPolicyMode.Allowlist)
        {
            blockFilters.Add("*");
        }

        if (parameters.Mode != BrowserPolicyMode.Unrestricted)
        {
            foreach (BrowserPolicyRuleParameters rule in parameters.Rules)
            {
                if (!rule.Enabled)
                {
                    continue;
                }

                var compiledRule = CompileRule(rule);
                if (!compiledRule.Succeeded)
                {
                    return compiledRule;
                }

                if (rule.Action == BrowserPolicyRuleAction.Block)
                {
                    blockFilters.Add(compiledRule.Policy!.BlockFilters[0]);
                }
                else if (rule.Action == BrowserPolicyRuleAction.Allow)
                {
                    allowFilters.Add(compiledRule.Policy!.AllowFilters[0]);
                }
                else
                {
                    return ChromiumBrowserPolicyCompilerResult.Failure(
                        NetworkOperationErrorCode.BrowserPolicyInvalid,
                        "Browser policy rule action is required.");
                }
            }
        }

        string[] canonicalBlock = Canonicalize(blockFilters);
        string[] canonicalAllow = Canonicalize(allowFilters);
        if (canonicalBlock.Length > MaxFiltersPerList || canonicalAllow.Length > MaxFiltersPerList)
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyTooLarge,
                "Browser policy exceeds the supported native policy size.");
        }

        string policyId = parameters.ImplicitUnrestricted && string.IsNullOrWhiteSpace(parameters.PolicyId)
            ? "implicit-unrestricted"
            : parameters.PolicyId.Trim();
        string contentHash = Hash(parameters.Mode, canonicalBlock, canonicalAllow);
        return ChromiumBrowserPolicyCompilerResult.Success(new CompiledBrowserPolicy(
            policyId,
            parameters.PolicyVersion,
            parameters.Mode,
            canonicalBlock,
            canonicalAllow,
            contentHash));
    }

    private ChromiumBrowserPolicyCompilerResult CompileRule(BrowserPolicyRuleParameters rule)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyInvalid,
                "Browser policy rule id and pattern are required.");
        }

        if (rule.MatchType == BrowserPolicyRuleMatchType.ExactUrl)
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyNotNativeEnforceable,
                "EXACT_URL cannot be safely represented by native Chromium URLBlocklist/URLAllowlist policy.");
        }

        string? filter = rule.MatchType switch
        {
            BrowserPolicyRuleMatchType.HostExact => CompileHostExact(rule.Pattern),
            BrowserPolicyRuleMatchType.HostSuffix => CompileHostSuffix(rule.Pattern),
            BrowserPolicyRuleMatchType.UrlPrefix => CompileUrlPrefix(rule.Pattern),
            _ => null
        };

        if (filter is null)
        {
            return ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyInvalid,
                "Browser policy rule match type or pattern is invalid.");
        }

        return rule.Action switch
        {
            BrowserPolicyRuleAction.Allow => ChromiumBrowserPolicyCompilerResult.Success(new CompiledBrowserPolicy(
                string.Empty, 0, BrowserPolicyMode.Blocklist, [], [filter], string.Empty)),
            BrowserPolicyRuleAction.Block => ChromiumBrowserPolicyCompilerResult.Success(new CompiledBrowserPolicy(
                string.Empty, 0, BrowserPolicyMode.Blocklist, [filter], [], string.Empty)),
            _ => ChromiumBrowserPolicyCompilerResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyInvalid,
                "Browser policy rule action is required.")
        };
    }

    private static string? CompileHostExact(string pattern)
    {
        string? host = NormalizeHost(pattern);
        if (host is null)
        {
            return null;
        }

        return IsIpAddress(host) ? host : "." + host;
    }

    private static string? CompileHostSuffix(string pattern)
    {
        return NormalizeHost(pattern);
    }

    private static string? CompileUrlPrefix(string pattern)
    {
        if (!TryNormalizeUri(pattern, allowQuery: false, out Uri? uri, out string? pathAndQuery))
        {
            return null;
        }

        string host = uri!.Host.ToLowerInvariant().TrimEnd('.');
        string nativeHost = IsIpAddress(host) ? host : "." + host;
        string port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return $"{uri.Scheme.ToLowerInvariant()}://{nativeHost}{port}{pathAndQuery}";
    }

    private static bool TryNormalizeUri(
        string pattern,
        bool allowQuery,
        out Uri? uri,
        out string? pathAndQuery)
    {
        uri = null;
        pathAndQuery = null;
        if (ContainsControlCharacters(pattern)
            || !Uri.TryCreate(pattern.Trim(), UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || (!allowQuery && !string.IsNullOrEmpty(parsed.Query)))
        {
            return false;
        }

        uri = parsed;
        string path = string.IsNullOrEmpty(parsed.AbsolutePath) ? "/" : parsed.AbsolutePath;
        pathAndQuery = path + (allowQuery ? parsed.Query : string.Empty);
        return true;
    }

    private static string? NormalizeHost(string pattern)
    {
        string host = pattern.Trim().ToLowerInvariant().TrimEnd('.');
        if (host.Length == 0
            || host.Contains('*', StringComparison.Ordinal)
            || host.Contains('/', StringComparison.Ordinal)
            || host.Contains(':', StringComparison.Ordinal)
            || ContainsControlCharacters(host))
        {
            return null;
        }

        return host;
    }

    private static string[] Canonicalize(IEnumerable<string> filters)
    {
        return filters
            .Select(filter => filter.Trim())
            .Where(filter => filter.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(filter => filter, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Hash(BrowserPolicyMode mode, IReadOnlyList<string> blockFilters, IReadOnlyList<string> allowFilters)
    {
        var builder = new StringBuilder();
        builder.Append("mode=").Append(mode).Append('\n');
        AppendList(builder, "block", blockFilters);
        AppendList(builder, "allow", allowFilters);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void AppendList(StringBuilder builder, string name, IReadOnlyList<string> filters)
    {
        builder.Append(name).Append('=').Append(filters.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (string filter in filters)
        {
            builder.Append(filter).Append('\n');
        }
    }

    private static bool ContainsControlCharacters(string value)
    {
        return value.Any(char.IsControl);
    }

    private static bool IsIpAddress(string host)
    {
        return IPAddress.TryParse(host.Trim('[', ']'), out _);
    }
}

public enum BrowserPolicyNavigationOutcome
{
    Allow,
    Block
}

public sealed record BrowserPolicyNavigationDecision(
    BrowserPolicyNavigationOutcome Outcome,
    string? MatchedFilter,
    BrowserPolicyRuleAction? MatchedAction)
{
    public static BrowserPolicyNavigationDecision Allow(string? filter = null, BrowserPolicyRuleAction? action = null)
    {
        return new BrowserPolicyNavigationDecision(BrowserPolicyNavigationOutcome.Allow, filter, action);
    }

    public static BrowserPolicyNavigationDecision Block(string? filter, BrowserPolicyRuleAction? action)
    {
        return new BrowserPolicyNavigationDecision(BrowserPolicyNavigationOutcome.Block, filter, action);
    }
}

public sealed class ChromiumBrowserPolicyEvaluator
{
    public BrowserPolicyNavigationDecision Evaluate(CompiledBrowserPolicy? policy, string url)
    {
        if (policy is null || policy.Mode == BrowserPolicyMode.Unrestricted)
        {
            return BrowserPolicyNavigationDecision.Allow();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return BrowserPolicyNavigationDecision.Block(null, BrowserPolicyRuleAction.Block);
        }

        var matches = new List<NativeFilterMatch>();
        AddMatches(matches, policy.BlockFilters, BrowserPolicyRuleAction.Block, uri);
        AddMatches(matches, policy.AllowFilters, BrowserPolicyRuleAction.Allow, uri);

        if (matches.Count == 0)
        {
            return BrowserPolicyNavigationDecision.Allow();
        }

        matches.Sort(static (left, right) =>
        {
            int specificity = left.Specificity.CompareTo(right.Specificity);
            if (specificity != 0)
            {
                return specificity;
            }

            if (left.Action == right.Action)
            {
                return 0;
            }

            return left.Action == BrowserPolicyRuleAction.Allow ? 1 : -1;
        });

        NativeFilterMatch best = matches[^1];
        return best.Action == BrowserPolicyRuleAction.Allow
            ? BrowserPolicyNavigationDecision.Allow(best.Filter, best.Action)
            : BrowserPolicyNavigationDecision.Block(best.Filter, best.Action);
    }

    private static void AddMatches(
        List<NativeFilterMatch> matches,
        IEnumerable<string> filters,
        BrowserPolicyRuleAction action,
        Uri uri)
    {
        foreach (string filter in filters)
        {
            FilterSpecificity? specificity = Match(filter, uri);
            if (specificity is not null)
            {
                matches.Add(new NativeFilterMatch(filter, action, specificity.Value));
            }
        }
    }

    private static FilterSpecificity? Match(string filter, Uri uri)
    {
        if (filter == "*")
        {
            return new FilterSpecificity(0, 0, 0, 0);
        }

        if (filter.Contains("://", StringComparison.Ordinal))
        {
            return MatchUrlFilter(filter, uri);
        }

        bool exact = filter.StartsWith(".", StringComparison.Ordinal);
        string hostPattern = exact ? filter[1..] : filter;
        string actualHost = uri.Host.ToLowerInvariant().TrimEnd('.');
        bool hostMatches = exact
            ? string.Equals(actualHost, hostPattern, StringComparison.Ordinal)
            : string.Equals(actualHost, hostPattern, StringComparison.Ordinal)
              || actualHost.EndsWith("." + hostPattern, StringComparison.Ordinal);
        return hostMatches
            ? new FilterSpecificity(HostScore(hostPattern, exact), 0, 0, 0)
            : null;
    }

    private static FilterSpecificity? MatchUrlFilter(string filter, Uri uri)
    {
        if (!Uri.TryCreate(filter.Replace("://.", "://", StringComparison.Ordinal), UriKind.Absolute, out Uri? filterUri))
        {
            return null;
        }

        bool exactHost = filter.Contains("://.", StringComparison.Ordinal);
        if (!string.Equals(uri.Scheme, filterUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string actualHost = uri.Host.ToLowerInvariant().TrimEnd('.');
        string filterHost = filterUri.Host.ToLowerInvariant().TrimEnd('.');
        bool hostMatches = exactHost
            ? string.Equals(actualHost, filterHost, StringComparison.Ordinal)
            : string.Equals(actualHost, filterHost, StringComparison.Ordinal)
              || actualHost.EndsWith("." + filterHost, StringComparison.Ordinal);
        if (!hostMatches || (!filterUri.IsDefaultPort && uri.Port != filterUri.Port))
        {
            return null;
        }

        string actualPath = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        string filterPath = string.IsNullOrEmpty(filterUri.AbsolutePath) ? "/" : filterUri.AbsolutePath;
        if (!actualPath.StartsWith(filterPath, StringComparison.Ordinal))
        {
            return null;
        }

        string filterQuery = filterUri.Query;
        if (!string.IsNullOrEmpty(filterQuery) && !uri.Query.Contains(filterQuery.TrimStart('?'), StringComparison.Ordinal))
        {
            return null;
        }

        int schemePort = 1 + (filterUri.IsDefaultPort ? 0 : 1);
        return new FilterSpecificity(HostScore(filterHost, exactHost), schemePort, filterPath.Length, filterQuery.Length);
    }

    private static int HostScore(string host, bool exact)
    {
        return host.Split('.', StringSplitOptions.RemoveEmptyEntries).Length * 2 + (exact ? 1 : 0);
    }

    private readonly record struct NativeFilterMatch(
        string Filter,
        BrowserPolicyRuleAction Action,
        FilterSpecificity Specificity);

    private readonly record struct FilterSpecificity(
        int Host,
        int SchemePort,
        int Path,
        int Query) : IComparable<FilterSpecificity>
    {
        public int CompareTo(FilterSpecificity other)
        {
            int host = Host.CompareTo(other.Host);
            if (host != 0)
            {
                return host;
            }

            int schemePort = SchemePort.CompareTo(other.SchemePort);
            if (schemePort != 0)
            {
                return schemePort;
            }

            int path = Path.CompareTo(other.Path);
            if (path != 0)
            {
                return path;
            }

            return Query.CompareTo(other.Query);
        }
    }
}

public sealed record InteractiveUserIdentityResult(
    bool Succeeded,
    string? WindowsSid,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static InteractiveUserIdentityResult Success(string windowsSid)
    {
        return new InteractiveUserIdentityResult(true, windowsSid, NetworkOperationErrorCode.Unspecified, string.Empty);
    }

    public static InteractiveUserIdentityResult Unavailable()
    {
        return new InteractiveUserIdentityResult(
            false,
            null,
            NetworkOperationErrorCode.BrowserPolicyUserUnavailable,
            "Interactive Windows user is unavailable.");
    }
}

public interface IInteractiveUserIdentityResolver
{
    Task<InteractiveUserIdentityResult> ResolveAsync(CancellationToken cancellationToken);
}

public sealed class WindowsInteractiveUserIdentityResolver : IInteractiveUserIdentityResolver
{
    private const uint WtsNoSession = 0xFFFFFFFF;
    private const int TokenUserClass = 1;

    [SupportedOSPlatform("windows")]
    public Task<InteractiveUserIdentityResult> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(InteractiveUserIdentityResult.Unavailable());
        }

        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId is 0 or WtsNoSession)
        {
            return Task.FromResult(InteractiveUserIdentityResult.Unavailable());
        }

        if (!WTSQueryUserToken(sessionId, out IntPtr token) || token == IntPtr.Zero)
        {
            return Task.FromResult(InteractiveUserIdentityResult.Unavailable());
        }

        try
        {
            return Task.FromResult(ReadTokenUserSid(token));
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [SupportedOSPlatform("windows")]
    private static InteractiveUserIdentityResult ReadTokenUserSid(IntPtr token)
    {
        _ = GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out int requiredLength);
        if (requiredLength <= 0)
        {
            return InteractiveUserIdentityResult.Unavailable();
        }

        IntPtr buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            if (!GetTokenInformation(token, TokenUserClass, buffer, requiredLength, out _))
            {
                return InteractiveUserIdentityResult.Unavailable();
            }

            var tokenUser = Marshal.PtrToStructure<TokenUser>(buffer);
            if (tokenUser.User.Sid == IntPtr.Zero)
            {
                return InteractiveUserIdentityResult.Unavailable();
            }

            var sid = new SecurityIdentifier(tokenUser.User.Sid);
            return InteractiveUserIdentityResult.Success(sid.Value);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SidAndAttributes
    {
        public readonly IntPtr Sid;
        public readonly int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TokenUser
    {
        public readonly SidAndAttributes User;
    }
}

public sealed record BrowserRegistrySnapshot(
    string[] ChromeBlock,
    string[] ChromeAllow,
    string[] EdgeBlock,
    string[] EdgeAllow)
{
    public static BrowserRegistrySnapshot Empty { get; } = new([], [], [], []);

    public bool IsEmpty =>
        ChromeBlock.Length == 0
        && ChromeAllow.Length == 0
        && EdgeBlock.Length == 0
        && EdgeAllow.Length == 0;

    public bool ContentEquals(BrowserRegistrySnapshot other)
    {
        return ChromeBlock.SequenceEqual(other.ChromeBlock, StringComparer.Ordinal)
            && ChromeAllow.SequenceEqual(other.ChromeAllow, StringComparer.Ordinal)
            && EdgeBlock.SequenceEqual(other.EdgeBlock, StringComparer.Ordinal)
            && EdgeAllow.SequenceEqual(other.EdgeAllow, StringComparer.Ordinal);
    }

    public static BrowserRegistrySnapshot FromPolicy(CompiledBrowserPolicy policy)
    {
        return new BrowserRegistrySnapshot(
            policy.BlockFilters,
            policy.AllowFilters,
            policy.BlockFilters,
            policy.AllowFilters);
    }
}

public interface IBrowserPolicyRegistryStore
{
    Task<BrowserRegistrySnapshot> ReadUserPolicyAsync(string windowsSid, CancellationToken cancellationToken);

    Task<bool> HasMachineLevelPolicyAsync(CancellationToken cancellationToken);

    Task<BrowserPolicyRegistryApplyResult> ApplyUserPolicyAsync(
        string windowsSid,
        BrowserRegistrySnapshot desired,
        CancellationToken cancellationToken);
}

public sealed record BrowserPolicyRegistryApplyResult(
    bool Succeeded,
    bool RollbackSucceeded,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static BrowserPolicyRegistryApplyResult Success()
    {
        return new BrowserPolicyRegistryApplyResult(true, true, NetworkOperationErrorCode.Unspecified, string.Empty);
    }

    public static BrowserPolicyRegistryApplyResult Failure(
        bool rollbackSucceeded,
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new BrowserPolicyRegistryApplyResult(false, rollbackSucceeded, errorCode, message);
    }
}

public sealed class BrowserPolicyRegistryException : Exception
{
    public BrowserPolicyRegistryException(NetworkOperationErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public NetworkOperationErrorCode ErrorCode { get; }
}

public sealed class WindowsBrowserPolicyRegistryStore : IBrowserPolicyRegistryStore
{
    private static readonly BrowserRegistryList[] Lists =
    [
        new("Software\\Policies\\Google\\Chrome\\URLBlocklist", BrowserListKind.ChromeBlock),
        new("Software\\Policies\\Google\\Chrome\\URLAllowlist", BrowserListKind.ChromeAllow),
        new("Software\\Policies\\Microsoft\\Edge\\URLBlocklist", BrowserListKind.EdgeBlock),
        new("Software\\Policies\\Microsoft\\Edge\\URLAllowlist", BrowserListKind.EdgeAllow)
    ];

    [SupportedOSPlatform("windows")]
    public Task<BrowserRegistrySnapshot> ReadUserPolicyAsync(string windowsSid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? users = Registry.Users;
        using RegistryKey? sidRoot = users.OpenSubKey(windowsSid, writable: false);
        if (sidRoot is null)
        {
            throw new BrowserPolicyRegistryException(
                NetworkOperationErrorCode.BrowserPolicyUserHiveUnavailable,
                "Interactive user policy hive is unavailable.");
        }

        var values = new Dictionary<BrowserListKind, string[]>();
        foreach (BrowserRegistryList list in Lists)
        {
            using RegistryKey? key = sidRoot.OpenSubKey(list.RelativePath, writable: false);
            values[list.Kind] = ReadSequentialValues(key);
        }

        return Task.FromResult(ToSnapshot(values));
    }

    [SupportedOSPlatform("windows")]
    public Task<bool> HasMachineLevelPolicyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (BrowserRegistryList list in Lists)
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(list.RelativePath, writable: false);
            if (ReadSequentialValues(key).Length > 0)
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    [SupportedOSPlatform("windows")]
    public async Task<BrowserPolicyRegistryApplyResult> ApplyUserPolicyAsync(
        string windowsSid,
        BrowserRegistrySnapshot desired,
        CancellationToken cancellationToken)
    {
        BrowserRegistrySnapshot previous = await ReadUserPolicyAsync(windowsSid, cancellationToken).ConfigureAwait(false);
        try
        {
            WriteUserPolicy(windowsSid, desired);
            BrowserRegistrySnapshot verified = await ReadUserPolicyAsync(windowsSid, cancellationToken).ConfigureAwait(false);
            if (!verified.ContentEquals(desired))
            {
                TryWriteUserPolicy(windowsSid, previous);
                return BrowserPolicyRegistryApplyResult.Failure(
                    rollbackSucceeded: true,
                    NetworkOperationErrorCode.BrowserPolicyApplyFailed,
                    "Browser policy registry verification failed.");
            }

            return BrowserPolicyRegistryApplyResult.Success();
        }
        catch (BrowserPolicyRegistryException exception)
        {
            bool rollbackSucceeded = TryWriteUserPolicy(windowsSid, previous);
            return BrowserPolicyRegistryApplyResult.Failure(
                rollbackSucceeded,
                exception.ErrorCode,
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            bool rollbackSucceeded = TryWriteUserPolicy(windowsSid, previous);
            return BrowserPolicyRegistryApplyResult.Failure(
                rollbackSucceeded,
                rollbackSucceeded
                    ? NetworkOperationErrorCode.BrowserPolicyApplyFailed
                    : NetworkOperationErrorCode.BrowserPolicyRollbackFailed,
                rollbackSucceeded
                    ? "Browser policy registry apply failed and rollback succeeded."
                    : "Browser policy registry apply failed and rollback also failed.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteUserPolicy(string windowsSid, BrowserRegistrySnapshot snapshot)
    {
        using RegistryKey users = Registry.Users;
        using RegistryKey? sidRoot = users.OpenSubKey(windowsSid, writable: true);
        if (sidRoot is null)
        {
            throw new BrowserPolicyRegistryException(
                NetworkOperationErrorCode.BrowserPolicyUserHiveUnavailable,
                "Interactive user policy hive is unavailable.");
        }

        WriteList(sidRoot, windowsSid, Lists[0], snapshot.ChromeBlock);
        WriteList(sidRoot, windowsSid, Lists[2], snapshot.EdgeBlock);
        WriteList(sidRoot, windowsSid, Lists[1], snapshot.ChromeAllow);
        WriteList(sidRoot, windowsSid, Lists[3], snapshot.EdgeAllow);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryWriteUserPolicy(string windowsSid, BrowserRegistrySnapshot snapshot)
    {
        try
        {
            WriteUserPolicy(windowsSid, snapshot);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteList(RegistryKey sidRoot, string windowsSid, BrowserRegistryList list, string[] values)
    {
        using RegistryKey key = sidRoot.CreateSubKey(
            list.RelativePath,
            RegistryKeyPermissionCheck.ReadWriteSubTree);
        if (key is null)
        {
            throw new IOException("User policy hive is unavailable.");
        }

        foreach (string valueName in key.GetValueNames())
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }

        for (int i = 0; i < values.Length; i++)
        {
            key.SetValue((i + 1).ToString(CultureInfo.InvariantCulture), values[i], RegistryValueKind.String);
        }

        ApplyAcl(key, windowsSid);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyAcl(RegistryKey key, string windowsSid)
    {
        var security = new RegistrySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            RegistryRights.FullControl,
            InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            RegistryRights.FullControl,
            InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(windowsSid),
            RegistryRights.ReadKey,
            InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        key.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static string[] ReadSequentialValues(RegistryKey? key)
    {
        if (key is null)
        {
            return [];
        }

        return key.GetValueNames()
            .Select(name => new
            {
                Parsed = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : int.MaxValue,
                Name = name
            })
            .OrderBy(value => value.Parsed)
            .ThenBy(value => value.Name, StringComparer.Ordinal)
            .Select(value => key.GetValue(value.Name) as string)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static BrowserRegistrySnapshot ToSnapshot(Dictionary<BrowserListKind, string[]> values)
    {
        return new BrowserRegistrySnapshot(
            values.GetValueOrDefault(BrowserListKind.ChromeBlock) ?? [],
            values.GetValueOrDefault(BrowserListKind.ChromeAllow) ?? [],
            values.GetValueOrDefault(BrowserListKind.EdgeBlock) ?? [],
            values.GetValueOrDefault(BrowserListKind.EdgeAllow) ?? []);
    }

    private sealed record BrowserRegistryList(string RelativePath, BrowserListKind Kind);

    private enum BrowserListKind
    {
        ChromeBlock,
        ChromeAllow,
        EdgeBlock,
        EdgeAllow
    }
}

public sealed record BrowserNavigationPolicyStateEntry(
    string WindowsSid,
    string PolicyId,
    long PolicyVersion,
    string ContentHash,
    BrowserPolicyMode Mode,
    string[] BlockFilters,
    string[] AllowFilters,
    DateTimeOffset AppliedAtUtc);

public sealed record BrowserPolicyApplyJournalEntry(
    string WindowsSid,
    BrowserRegistrySnapshot Previous,
    BrowserNavigationPolicyStateEntry? PreviousState,
    BrowserNavigationPolicyStateEntry Desired,
    BrowserRegistrySnapshot DesiredRegistry,
    DateTimeOffset StartedAtUtc);

public sealed class BrowserNavigationPolicyStateStore
{
    public const string StateFileName = "browser-navigation-policy-state.json";
    public const string JournalFileName = "browser-navigation-policy-apply.json";
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

    public BrowserNavigationPolicyStateStore(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _statePath = Path.Combine(_dataDirectory, StateFileName);
        _journalPath = Path.Combine(_dataDirectory, JournalFileName);
    }

    public async Task<BrowserNavigationPolicyStateEntry?> TryGetUserStateAsync(
        string windowsSid,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BrowserNavigationPolicyStateDocument document = await ReadStateDocumentAsync(cancellationToken).ConfigureAwait(false);
            return document.Users.FirstOrDefault(user => string.Equals(user.WindowsSid, windowsSid, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveUserStateAsync(
        BrowserNavigationPolicyStateEntry entry,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BrowserNavigationPolicyStateDocument document = await ReadStateDocumentAsync(cancellationToken).ConfigureAwait(false);
            var users = document.Users
                .Where(user => !string.Equals(user.WindowsSid, entry.WindowsSid, StringComparison.Ordinal))
                .Append(entry)
                .OrderBy(user => user.WindowsSid, StringComparer.Ordinal)
                .ToArray();
            await WriteJsonAsync(_statePath, new BrowserNavigationPolicyStateDocument(SchemaVersion, users), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserPolicyApplyJournalEntry?> TryReadJournalAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_journalPath))
            {
                return null;
            }

            await using var stream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            var document = await JsonSerializer.DeserializeAsync<BrowserPolicyApplyJournalDocument>(
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

    public async Task SaveJournalAsync(BrowserPolicyApplyJournalEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteJsonAsync(
                _journalPath,
                new BrowserPolicyApplyJournalDocument(SchemaVersion, entry),
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

    private async Task<BrowserNavigationPolicyStateDocument> ReadStateDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new BrowserNavigationPolicyStateDocument(SchemaVersion, []);
        }

        await using var stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        var document = await JsonSerializer.DeserializeAsync<BrowserNavigationPolicyStateDocument>(
            stream,
            ReadOptions,
            cancellationToken).ConfigureAwait(false);
        return document?.SchemaVersion == SchemaVersion && document.Users is not null
            ? document
            : new BrowserNavigationPolicyStateDocument(SchemaVersion, []);
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

    private sealed record BrowserNavigationPolicyStateDocument(
        int SchemaVersion,
        BrowserNavigationPolicyStateEntry[] Users);

    private sealed record BrowserPolicyApplyJournalDocument(
        int SchemaVersion,
        BrowserPolicyApplyJournalEntry? Entry);
}

public sealed record BrowserPolicyApplyResult(
    bool Succeeded,
    bool NoChange,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static BrowserPolicyApplyResult Success(bool noChange = false)
    {
        return new BrowserPolicyApplyResult(true, noChange, NetworkOperationErrorCode.Unspecified, string.Empty);
    }

    public static BrowserPolicyApplyResult Failure(NetworkOperationErrorCode errorCode, string message)
    {
        return new BrowserPolicyApplyResult(false, false, errorCode, message);
    }
}

public sealed class BrowserNavigationPolicyApplyService
{
    private readonly IBrowserPolicyRegistryStore _registry;
    private readonly BrowserNavigationPolicyStateStore _stateStore;
    private readonly ISystemClock _clock;

    public BrowserNavigationPolicyApplyService(
        IBrowserPolicyRegistryStore registry,
        BrowserNavigationPolicyStateStore stateStore,
        ISystemClock clock)
    {
        _registry = registry;
        _stateStore = stateStore;
        _clock = clock;
    }

    public async Task<BrowserPolicyApplyResult> ApplyAsync(
        string windowsSid,
        CompiledBrowserPolicy desiredPolicy,
        CancellationToken cancellationToken)
    {
        BrowserPolicyApplyResult recovery = await RecoverIfNeededAsync(cancellationToken).ConfigureAwait(false);
        if (!recovery.Succeeded)
        {
            return recovery;
        }

        if (await _registry.HasMachineLevelPolicyAsync(cancellationToken).ConfigureAwait(false))
        {
            return BrowserPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyExternalConflict,
                "Machine-level browser policy already exists.");
        }

        BrowserRegistrySnapshot current = await _registry.ReadUserPolicyAsync(windowsSid, cancellationToken).ConfigureAwait(false);
        BrowserNavigationPolicyStateEntry? previousState = await _stateStore.TryGetUserStateAsync(windowsSid, cancellationToken)
            .ConfigureAwait(false);

        if (previousState is null && !current.IsEmpty)
        {
            return BrowserPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyExternalConflict,
                "Existing browser policy is not owned by Galtek.");
        }

        if (previousState is not null
            && !current.ContentEquals(SnapshotFromState(previousState)))
        {
            return BrowserPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyExternalConflict,
                "Browser policy registry diverged from Galtek state.");
        }

        BrowserRegistrySnapshot desiredRegistry = BrowserRegistrySnapshot.FromPolicy(desiredPolicy);
        if (previousState is not null
            && string.Equals(previousState.ContentHash, desiredPolicy.ContentHash, StringComparison.Ordinal)
            && current.ContentEquals(desiredRegistry))
        {
            return BrowserPolicyApplyResult.Success(noChange: true);
        }

        var desiredState = new BrowserNavigationPolicyStateEntry(
            windowsSid,
            desiredPolicy.PolicyId,
            desiredPolicy.PolicyVersion,
            desiredPolicy.ContentHash,
            desiredPolicy.Mode,
            desiredPolicy.BlockFilters,
            desiredPolicy.AllowFilters,
            _clock.UtcNow.ToUniversalTime());
        await _stateStore.SaveJournalAsync(
            new BrowserPolicyApplyJournalEntry(
                windowsSid,
                current,
                previousState,
                desiredState,
                desiredRegistry,
                _clock.UtcNow.ToUniversalTime()),
            cancellationToken).ConfigureAwait(false);

        BrowserPolicyRegistryApplyResult apply = await _registry.ApplyUserPolicyAsync(
            windowsSid,
            desiredRegistry,
            cancellationToken).ConfigureAwait(false);
        if (!apply.Succeeded)
        {
            return BrowserPolicyApplyResult.Failure(apply.ErrorCode, apply.Message);
        }

        BrowserRegistrySnapshot verified = await _registry.ReadUserPolicyAsync(windowsSid, cancellationToken).ConfigureAwait(false);
        if (!verified.ContentEquals(desiredRegistry))
        {
            return BrowserPolicyApplyResult.Failure(
                NetworkOperationErrorCode.BrowserPolicyApplyFailed,
                "Browser policy registry verification failed.");
        }

        await _stateStore.SaveUserStateAsync(desiredState, cancellationToken).ConfigureAwait(false);
        _stateStore.DeleteJournal();
        return BrowserPolicyApplyResult.Success();
    }

    public async Task<CompiledBrowserPolicy?> TryGetAppliedPolicyForUserAsync(
        string windowsSid,
        CancellationToken cancellationToken)
    {
        BrowserNavigationPolicyStateEntry? state = await _stateStore.TryGetUserStateAsync(windowsSid, cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        BrowserRegistrySnapshot current = await _registry.ReadUserPolicyAsync(windowsSid, cancellationToken).ConfigureAwait(false);
        if (!current.ContentEquals(SnapshotFromState(state)))
        {
            return null;
        }

        return new CompiledBrowserPolicy(
            state.PolicyId,
            state.PolicyVersion,
            state.Mode,
            state.BlockFilters,
            state.AllowFilters,
            state.ContentHash);
    }

    private async Task<BrowserPolicyApplyResult> RecoverIfNeededAsync(CancellationToken cancellationToken)
    {
        BrowserPolicyApplyJournalEntry? journal = await _stateStore.TryReadJournalAsync(cancellationToken).ConfigureAwait(false);
        if (journal is null)
        {
            return BrowserPolicyApplyResult.Success(noChange: true);
        }

        BrowserRegistrySnapshot current = await _registry.ReadUserPolicyAsync(journal.WindowsSid, cancellationToken).ConfigureAwait(false);
        if (current.ContentEquals(journal.DesiredRegistry))
        {
            await _stateStore.SaveUserStateAsync(journal.Desired, cancellationToken).ConfigureAwait(false);
            _stateStore.DeleteJournal();
            return BrowserPolicyApplyResult.Success(noChange: true);
        }

        if (current.ContentEquals(journal.Previous))
        {
            _stateStore.DeleteJournal();
            return BrowserPolicyApplyResult.Success(noChange: true);
        }

        if (journal.PreviousState is not null && SnapshotContainsOnlyKnownValues(current, journal.Previous, journal.DesiredRegistry))
        {
            BrowserPolicyRegistryApplyResult rollback = await _registry.ApplyUserPolicyAsync(
                journal.WindowsSid,
                journal.Previous,
                cancellationToken).ConfigureAwait(false);
            if (!rollback.Succeeded)
            {
                return BrowserPolicyApplyResult.Failure(
                    rollback.ErrorCode == NetworkOperationErrorCode.BrowserPolicyRollbackFailed
                        ? rollback.ErrorCode
                        : NetworkOperationErrorCode.BrowserPolicyRecoveryRequired,
                    "Browser policy recovery rollback failed.");
            }

            _stateStore.DeleteJournal();
            return BrowserPolicyApplyResult.Success(noChange: true);
        }

        return BrowserPolicyApplyResult.Failure(
            NetworkOperationErrorCode.BrowserPolicyRecoveryRequired,
            "Browser policy registry requires manual recovery.");
    }

    private static bool SnapshotContainsOnlyKnownValues(
        BrowserRegistrySnapshot current,
        BrowserRegistrySnapshot previous,
        BrowserRegistrySnapshot desired)
    {
        return Known(current.ChromeBlock, previous.ChromeBlock, desired.ChromeBlock)
            && Known(current.ChromeAllow, previous.ChromeAllow, desired.ChromeAllow)
            && Known(current.EdgeBlock, previous.EdgeBlock, desired.EdgeBlock)
            && Known(current.EdgeAllow, previous.EdgeAllow, desired.EdgeAllow);

        static bool Known(string[] current, string[] previous, string[] desired)
        {
            var known = previous.Concat(desired).ToHashSet(StringComparer.Ordinal);
            return current.All(known.Contains);
        }
    }

    private static BrowserRegistrySnapshot SnapshotFromState(BrowserNavigationPolicyStateEntry state)
    {
        return new BrowserRegistrySnapshot(
            state.BlockFilters,
            state.AllowFilters,
            state.BlockFilters,
            state.AllowFilters);
    }
}

public sealed class AppliedBrowserPolicyEvaluator
{
    private readonly IInteractiveUserIdentityResolver _identityResolver;
    private readonly BrowserNavigationPolicyApplyService _policyService;
    private readonly ChromiumBrowserPolicyEvaluator _evaluator;

    public AppliedBrowserPolicyEvaluator(
        IInteractiveUserIdentityResolver identityResolver,
        BrowserNavigationPolicyApplyService policyService,
        ChromiumBrowserPolicyEvaluator evaluator)
    {
        _identityResolver = identityResolver;
        _policyService = policyService;
        _evaluator = evaluator;
    }

    public async Task<BrowserPolicyNavigationDecision?> EvaluateCurrentUserAsync(
        string url,
        CancellationToken cancellationToken)
    {
        InteractiveUserIdentityResult identity = await _identityResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!identity.Succeeded || string.IsNullOrWhiteSpace(identity.WindowsSid))
        {
            return null;
        }

        CompiledBrowserPolicy? policy;
        try
        {
            policy = await _policyService.TryGetAppliedPolicyForUserAsync(
                identity.WindowsSid,
                cancellationToken).ConfigureAwait(false);
        }
        catch (BrowserPolicyRegistryException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        return policy is null ? null : _evaluator.Evaluate(policy, url);
    }
}

public sealed class ApplyBrowserPolicyOperationHandler : IRemoteOperationHandler
{
    private readonly ChromiumBrowserPolicyCompiler _compiler;
    private readonly IInteractiveUserIdentityResolver _identityResolver;
    private readonly BrowserNavigationPolicyApplyService _applyService;
    private readonly ILogger<ApplyBrowserPolicyOperationHandler> _logger;

    public ApplyBrowserPolicyOperationHandler(
        ChromiumBrowserPolicyCompiler compiler,
        IInteractiveUserIdentityResolver identityResolver,
        BrowserNavigationPolicyApplyService applyService,
        ILogger<ApplyBrowserPolicyOperationHandler> logger)
    {
        _compiler = compiler;
        _identityResolver = identityResolver;
        _applyService = applyService;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.ApplyBrowserNavigationPolicy;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        ApplyBrowserPolicyOperationParameters? parameters = request.ApplyBrowserPolicy;
        if (parameters is null)
        {
            return Failed(NetworkOperationErrorCode.BrowserPolicyInvalid, "Browser policy parameters are required.");
        }

        if (parameters.AccountScope is BrowserPolicyAccountScope.Primary or BrowserPolicyAccountScope.Secondary)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserAccountScopeUnresolved,
                "Browser policy account scope is not resolved on this Agent.");
        }

        if (parameters.AccountScope != BrowserPolicyAccountScope.Any)
        {
            return Failed(NetworkOperationErrorCode.BrowserPolicyInvalid, "Browser policy account scope is invalid.");
        }

        InteractiveUserIdentityResult identity = await _identityResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!identity.Succeeded || string.IsNullOrWhiteSpace(identity.WindowsSid))
        {
            return Failed(identity.ErrorCode, identity.Message);
        }

        ChromiumBrowserPolicyCompilerResult compiled = _compiler.Compile(parameters);
        if (!compiled.Succeeded || compiled.Policy is null)
        {
            return Failed(compiled.ErrorCode, compiled.Message);
        }

        BrowserPolicyApplyResult applied;
        try
        {
            applied = await _applyService.ApplyAsync(
                identity.WindowsSid,
                compiled.Policy,
                cancellationToken).ConfigureAwait(false);
        }
        catch (BrowserPolicyRegistryException exception)
        {
            return Failed(exception.ErrorCode, exception.Message);
        }
        catch (JsonException)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserPolicyRecoveryRequired,
                "Browser policy local state requires recovery.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(
                NetworkOperationErrorCode.BrowserPolicyApplyFailed,
                "Browser policy local state could not be persisted.");
        }

        if (!applied.Succeeded)
        {
            return Failed(applied.ErrorCode, applied.Message);
        }

        _logger.LogInformation(
            "Applied browser navigation policy. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}; PolicyId: {PolicyId}; NoChange: {NoChange}",
            request.OperationId,
            request.TargetDeviceId,
            compiled.Policy.PolicyId,
            applied.NoChange);

        return RemoteOperationHandlerResult.Success(
            applied.NoChange
                ? "Browser navigation policy was already applied."
                : "Browser navigation policy was applied.");
    }

    private static RemoteOperationHandlerResult Failed(NetworkOperationErrorCode errorCode, string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            errorCode,
            message);
    }
}
