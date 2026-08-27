namespace GaltekClassroom.Agent.Service.Pairing;

public static class PairingConstants
{
    public const int SchemaVersion = 1;
    public const string Purpose = "GALTEK_CLASSROOM_MASTER_CLIENT_PAIRING_V1";
    public const string AuthorizedMastersFileName = "authorized-masters.json";
    public const int PairingChallengeTtlMinutes = 5;
    public const int NonceSizeBytes = 32;

    public const string ExplicitApprovalRequiredErrorCode = "PAIRING_EXPLICIT_APPROVAL_REQUIRED";
    public const string ChallengeInvalidErrorCode = "PAIRING_CHALLENGE_INVALID";
    public const string ChallengeExpiredErrorCode = "PAIRING_CHALLENGE_EXPIRED";
    public const string ReplayRejectedErrorCode = "PAIRING_REPLAY_REJECTED";
    public const string SignatureInvalidErrorCode = "PAIRING_SIGNATURE_INVALID";
    public const string FingerprintMismatchErrorCode = "PAIRING_FINGERPRINT_MISMATCH";
    public const string MasterRevokedErrorCode = "PAIRING_MASTER_REVOKED";
    public const string TrustStoreInvalidErrorCode = "PAIRING_TRUST_STORE_INVALID";
    public const string NetworkIdentityUnavailableErrorCode = "PAIRING_NETWORK_IDENTITY_UNAVAILABLE";
}
