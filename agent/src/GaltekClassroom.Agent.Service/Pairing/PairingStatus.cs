namespace GaltekClassroom.Agent.Service.Pairing;

public enum PairingStatus
{
    Unpaired,
    PairingPending,
    Paired,
    Revoked
}

public static class PairingStatusExtensions
{
    public static string ToCode(this PairingStatus status)
    {
        return status switch
        {
            PairingStatus.Unpaired => "UNPAIRED",
            PairingStatus.PairingPending => "PAIRING_PENDING",
            PairingStatus.Paired => "PAIRED",
            PairingStatus.Revoked => "REVOKED",
            _ => "UNPAIRED"
        };
    }

    public static bool TryParseCode(string? value, out PairingStatus status)
    {
        status = value switch
        {
            "UNPAIRED" => PairingStatus.Unpaired,
            "PAIRING_PENDING" => PairingStatus.PairingPending,
            "PAIRED" => PairingStatus.Paired,
            "REVOKED" => PairingStatus.Revoked,
            _ => PairingStatus.Unpaired
        };

        return value is "UNPAIRED" or "PAIRING_PENDING" or "PAIRED" or "REVOKED";
    }
}
