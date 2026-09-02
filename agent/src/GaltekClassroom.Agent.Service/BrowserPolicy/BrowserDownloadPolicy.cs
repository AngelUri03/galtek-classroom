using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.BrowserPolicy;

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
