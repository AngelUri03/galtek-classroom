using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;
using Microsoft.IdentityModel.Tokens;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed class CommercialLicenseValidator
{
    private const string ProductClaimName = "product";
    private const string SchemaVersionClaimName = "schemaVersion";
    private const string OrganizationIdClaimName = "organizationId";
    private const string CpuHashClaimName = "cpuHash";
    private const string MotherboardHashClaimName = "motherboardHash";
    private const string MacHashClaimName = "macHash";
    private const string DiskHashClaimName = "diskHash";
    private const string RolesClaimName = "roles";
    private const string FeaturesClaimName = "features";

    private readonly ILicensePublicKeyProvider _publicKeyProvider;
    private readonly ISystemClock _clock;
    private readonly IHardwareFingerprintProvider _hardwareFingerprintProvider;
    private readonly JwtSecurityTokenHandler _tokenHandler = new()
    {
        MapInboundClaims = false
    };

    public CommercialLicenseValidator(
        ILicensePublicKeyProvider publicKeyProvider,
        ISystemClock clock,
        IHardwareFingerprintProvider hardwareFingerprintProvider)
    {
        _publicKeyProvider = publicKeyProvider;
        _clock = clock;
        _hardwareFingerprintProvider = hardwareFingerprintProvider;
    }

    public async Task<CommercialLicenseValidationResult> ValidateAsync(
        string token,
        InstallationIdentity installationIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);

        var validatedAtUtc = _clock.UtcNow.ToUniversalTime();

        if (string.IsNullOrWhiteSpace(token))
        {
            return Blocked(
                CommercialLicenseStatus.LicenseMalformed,
                validatedAtUtc,
                "License token is empty.");
        }

        JwtSecurityToken jwt;

        try
        {
            jwt = _tokenHandler.ReadJwtToken(token);
        }
        catch (ArgumentException exception)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseMalformed,
                validatedAtUtc,
                $"License token is not a readable JWT: {exception.Message}");
        }

        if (!string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            return Blocked(
                CommercialLicenseStatus.LicenseTampered,
                validatedAtUtc,
                "License token uses an unsupported JWT algorithm. Only RS256 is accepted.");
        }

        var publicKeyResult = await _publicKeyProvider.GetPublicKeyAsync(cancellationToken);

        if (!publicKeyResult.IsConfigured)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseKeyNotConfigured,
                validatedAtUtc,
                publicKeyResult.ErrorMessage ?? "Galtek Hub public key is not configured.");
        }

        try
        {
            _tokenHandler.ValidateToken(
                token,
                CreateTokenValidationParameters(publicKeyResult.SecurityKey!),
                out var validatedToken);

            if (validatedToken is not JwtSecurityToken validatedJwt)
            {
                return Blocked(
                    CommercialLicenseStatus.LicenseInvalid,
                    validatedAtUtc,
                    "License token did not validate as a JWT.");
            }

            if (!string.Equals(validatedJwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            {
                return Blocked(
                    CommercialLicenseStatus.LicenseTampered,
                    validatedAtUtc,
                    "License token changed algorithm during validation.");
            }

            jwt = validatedJwt;
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return Blocked(
                CommercialLicenseStatus.IssuerMismatch,
                validatedAtUtc,
                "License issuer is not galtek-hub.");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return Blocked(
                CommercialLicenseStatus.AudienceMismatch,
                validatedAtUtc,
                "License audience is not galtek-classroom.");
        }
        catch (SecurityTokenExpiredException)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseExpired,
                validatedAtUtc,
                "License is expired.",
                TryReadExpiration(jwt));
        }
        catch (SecurityTokenNoExpirationException)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseMalformed,
                validatedAtUtc,
                "License expiration claim is missing.");
        }
        catch (SecurityTokenInvalidLifetimeException)
        {
            var expiresAtUtc = TryReadExpiration(jwt);

            return Blocked(
                expiresAtUtc.HasValue && validatedAtUtc >= expiresAtUtc.Value
                    ? CommercialLicenseStatus.LicenseExpired
                    : CommercialLicenseStatus.LicenseInvalid,
                validatedAtUtc,
                expiresAtUtc.HasValue && validatedAtUtc >= expiresAtUtc.Value
                    ? "License is expired."
                    : "License lifetime is invalid.",
                expiresAtUtc);
        }
        catch (SecurityTokenInvalidAlgorithmException)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseTampered,
                validatedAtUtc,
                "License token uses an unsupported JWT algorithm. Only RS256 is accepted.");
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseTampered,
                validatedAtUtc,
                "License signature does not match the configured Galtek Hub public key.");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseTampered,
                validatedAtUtc,
                "License signature is invalid.");
        }
        catch (SecurityTokenException exception)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseInvalid,
                validatedAtUtc,
                $"License token is invalid: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseMalformed,
                validatedAtUtc,
                $"License token is malformed: {exception.Message}");
        }

        return await ValidateStructuredClaimsAsync(
            jwt,
            installationIdentity,
            validatedAtUtc,
            cancellationToken);
    }

    private TokenValidationParameters CreateTokenValidationParameters(SecurityKey securityKey)
    {
        return new TokenValidationParameters
        {
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ValidateIssuer = true,
            ValidIssuer = CommercialLicenseConstants.Issuer,
            ValidateAudience = true,
            ValidAudience = CommercialLicenseConstants.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            LifetimeValidator = ValidateLifetimeWithInjectedClock
        };
    }

    private bool ValidateLifetimeWithInjectedClock(
        DateTime? notBefore,
        DateTime? expires,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters)
    {
        if (!expires.HasValue)
        {
            return false;
        }

        var nowUtc = _clock.UtcNow.UtcDateTime;

        if (notBefore.HasValue && nowUtc < EnsureUtc(notBefore.Value))
        {
            return false;
        }

        return nowUtc < EnsureUtc(expires.Value);
    }

    private async Task<CommercialLicenseValidationResult> ValidateStructuredClaimsAsync(
        JwtSecurityToken jwt,
        InstallationIdentity installationIdentity,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(jwt.Payload.SerializeToJson());
        var root = payload.RootElement;

        if (!TryGetRequiredString(root, JwtRegisteredClaimNames.Iss, out var issuer, out var error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!string.Equals(issuer, CommercialLicenseConstants.Issuer, StringComparison.Ordinal))
        {
            return Blocked(
                CommercialLicenseStatus.IssuerMismatch,
                validatedAtUtc,
                "License issuer is not galtek-hub.");
        }

        if (!TryGetRequiredAudience(root, out var audience, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!string.Equals(audience, CommercialLicenseConstants.Audience, StringComparison.Ordinal))
        {
            return Blocked(
                CommercialLicenseStatus.AudienceMismatch,
                validatedAtUtc,
                "License audience is not galtek-classroom.");
        }

        if (!TryGetRequiredString(root, JwtRegisteredClaimNames.Sub, out var subject, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!Guid.TryParse(subject, out var licenseInstallationId))
        {
            return Malformed(validatedAtUtc, "License subject must be an installation UUID.");
        }

        if (licenseInstallationId != installationIdentity.InstallationId)
        {
            return Blocked(
                CommercialLicenseStatus.InstallationMismatch,
                validatedAtUtc,
                "License is bound to a different installationId.");
        }

        if (!TryGetRequiredString(root, JwtRegisteredClaimNames.Jti, out var licenseId, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!TryGetRequiredString(root, ProductClaimName, out var product, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!string.Equals(product, ProductInfo.ProductCode, StringComparison.Ordinal))
        {
            return Blocked(
                CommercialLicenseStatus.ProductMismatch,
                validatedAtUtc,
                "License product is not GALTEK_CLASSROOM.");
        }

        if (!TryGetRequiredInt32(root, SchemaVersionClaimName, out var schemaVersion, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (schemaVersion != CommercialLicenseConstants.SchemaVersion)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseSchemaUnsupported,
                validatedAtUtc,
                $"Commercial license schemaVersion {schemaVersion} is not supported.");
        }

        if (!TryGetRequiredHash(root, CpuHashClaimName, out var cpuHash, out error)
            || !TryGetRequiredHash(root, MotherboardHashClaimName, out var motherboardHash, out error)
            || !TryGetRequiredHash(root, MacHashClaimName, out var macHash, out error)
            || !TryGetRequiredHash(root, DiskHashClaimName, out var diskHash, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        var currentHardware = await _hardwareFingerprintProvider.GetCurrentAsync(cancellationToken);
        var hardwareMatches = 0;
        hardwareMatches += string.Equals(cpuHash, currentHardware.CpuHash, StringComparison.Ordinal) ? 1 : 0;
        hardwareMatches += string.Equals(motherboardHash, currentHardware.MotherboardHash, StringComparison.Ordinal) ? 1 : 0;
        hardwareMatches += string.Equals(macHash, currentHardware.MacHash, StringComparison.Ordinal) ? 1 : 0;
        hardwareMatches += string.Equals(diskHash, currentHardware.DiskHash, StringComparison.Ordinal) ? 1 : 0;

        if (hardwareMatches < 3)
        {
            return Blocked(
                CommercialLicenseStatus.HardwareMismatch,
                validatedAtUtc,
                "License hardware binding does not match this installation.");
        }

        if (!TryGetRoles(root, out var roles, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!roles.Contains(CommercialLicenseConstants.ClientRole, StringComparer.Ordinal))
        {
            return Blocked(
                CommercialLicenseStatus.RoleNotAllowed,
                validatedAtUtc,
                "License does not include the CLIENT role required by this Agent.");
        }

        if (!TryGetFeatures(root, out var features, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!TryGetRequiredUnixTime(root, JwtRegisteredClaimNames.Iat, out var issuedAtUtc, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (!TryGetRequiredUnixTime(root, JwtRegisteredClaimNames.Exp, out var expiresAtUtc, out error))
        {
            return Malformed(validatedAtUtc, error);
        }

        if (issuedAtUtc >= expiresAtUtc)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseInvalid,
                validatedAtUtc,
                "License issued-at time must be earlier than expiration time.",
                expiresAtUtc);
        }

        if (validatedAtUtc >= expiresAtUtc)
        {
            return Blocked(
                CommercialLicenseStatus.LicenseExpired,
                validatedAtUtc,
                "License is expired.",
                expiresAtUtc,
                licenseId,
                TryGetOptionalString(root, OrganizationIdClaimName),
                roles,
                features);
        }

        return new CommercialLicenseValidationResult(
            LicenseState.ActiveState(
                licenseId,
                TryGetOptionalString(root, OrganizationIdClaimName),
                roles,
                features,
                expiresAtUtc,
                validatedAtUtc));
    }

    private CommercialLicenseValidationResult Malformed(DateTimeOffset validatedAtUtc, string error)
    {
        return Blocked(
            CommercialLicenseStatus.LicenseMalformed,
            validatedAtUtc,
            error);
    }

    private static CommercialLicenseValidationResult Blocked(
        CommercialLicenseStatus status,
        DateTimeOffset validatedAtUtc,
        string blockingReason,
        DateTimeOffset? expiresAtUtc = null,
        string? licenseId = null,
        string? organizationId = null,
        IReadOnlyList<string>? roles = null,
        CommercialLicenseFeatures? features = null)
    {
        return new CommercialLicenseValidationResult(
            LicenseState.Blocked(
                status,
                validatedAtUtc,
                blockingReason,
                expiresAtUtc,
                licenseId,
                organizationId,
                roles,
                features));
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string value,
        out string error)
    {
        value = string.Empty;

        if (!root.TryGetProperty(propertyName, out var element))
        {
            error = $"License claim '{propertyName}' is missing.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"License claim '{propertyName}' must be a string.";
            return false;
        }

        value = element.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"License claim '{propertyName}' is empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryGetRequiredAudience(
        JsonElement root,
        out string value,
        out string error)
    {
        value = string.Empty;

        if (!root.TryGetProperty(JwtRegisteredClaimNames.Aud, out var element))
        {
            error = "License claim 'aud' is missing.";
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(value))
            {
                error = string.Empty;
                return true;
            }

            error = "License claim 'aud' is empty.";
            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var audiences = element
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();

            if (audiences.Length == 1)
            {
                value = audiences[0]!;
                error = string.Empty;
                return true;
            }
        }

        error = "License claim 'aud' must contain exactly one audience.";
        return false;
    }

    private static string? TryGetOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null
            || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;
    }

    private static bool TryGetRequiredInt32(
        JsonElement root,
        string propertyName,
        out int value,
        out string error)
    {
        value = default;

        if (!root.TryGetProperty(propertyName, out var element))
        {
            error = $"License claim '{propertyName}' is missing.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            error = $"License claim '{propertyName}' must be a 32-bit integer.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryGetRequiredHash(
        JsonElement root,
        string propertyName,
        out string value,
        out string error)
    {
        if (!TryGetRequiredString(root, propertyName, out value, out error))
        {
            return false;
        }

        if (!InstallationIdentityValidator.IsValidSha256Hex(value))
        {
            error = $"License claim '{propertyName}' must be a lowercase SHA-256 hexadecimal hash.";
            return false;
        }

        return true;
    }

    private static bool TryGetRoles(
        JsonElement root,
        out IReadOnlyList<string> knownRoles,
        out string error)
    {
        knownRoles = Array.Empty<string>();

        if (!root.TryGetProperty(RolesClaimName, out var element))
        {
            error = "License claim 'roles' is missing.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = "License claim 'roles' must be an array.";
            return false;
        }

        var roles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var roleElement in element.EnumerateArray())
        {
            if (roleElement.ValueKind != JsonValueKind.String)
            {
                error = "License claim 'roles' must contain strings only.";
                return false;
            }

            var role = roleElement.GetString();

            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            if ((string.Equals(role, CommercialLicenseConstants.ClientRole, StringComparison.Ordinal)
                    || string.Equals(role, CommercialLicenseConstants.MasterRole, StringComparison.Ordinal))
                && seen.Add(role))
            {
                roles.Add(role);
            }
        }

        knownRoles = roles;
        error = string.Empty;
        return true;
    }

    private static bool TryGetFeatures(
        JsonElement root,
        out CommercialLicenseFeatures features,
        out string error)
    {
        features = CommercialLicenseFeatures.Empty;

        if (!root.TryGetProperty(FeaturesClaimName, out var element)
            || element.ValueKind == JsonValueKind.Null
            || element.ValueKind == JsonValueKind.Undefined)
        {
            error = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "License claim 'features' must be an object when present.";
            return false;
        }

        var values = new Dictionary<string, CommercialLicenseFeatureValue>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!string.IsNullOrWhiteSpace(property.Name))
            {
                values[property.Name] = CommercialLicenseFeatureValue.FromJsonElement(property.Value);
            }
        }

        features = new CommercialLicenseFeatures(values);
        error = string.Empty;
        return true;
    }

    private static bool TryGetRequiredUnixTime(
        JsonElement root,
        string propertyName,
        out DateTimeOffset value,
        out string error)
    {
        value = default;

        if (!root.TryGetProperty(propertyName, out var element))
        {
            error = $"License claim '{propertyName}' is missing.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var unixTimeSeconds))
        {
            error = $"License claim '{propertyName}' must be a NumericDate.";
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            error = $"License claim '{propertyName}' is outside the supported date range: {exception.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static DateTimeOffset? TryReadExpiration(JwtSecurityToken jwt)
    {
        try
        {
            using var payload = JsonDocument.Parse(jwt.Payload.SerializeToJson());

            return TryGetRequiredUnixTime(
                payload.RootElement,
                JwtRegisteredClaimNames.Exp,
                out var expiresAtUtc,
                out _)
                ? expiresAtUtc
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
