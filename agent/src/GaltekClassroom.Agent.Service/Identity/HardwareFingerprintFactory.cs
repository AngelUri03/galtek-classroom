using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public static class HardwareFingerprintFactory
{
    public static HardwareFingerprint FromRawValues(
        IEnumerable<string?> cpuIdentifiers,
        IEnumerable<string?> motherboardIdentifiers,
        IEnumerable<string?> macIdentifiers,
        IEnumerable<string?> diskIdentifiers)
    {
        return new HardwareFingerprint(
            HashComponent(cpuIdentifiers),
            HashComponent(motherboardIdentifiers),
            HashComponent(macIdentifiers),
            HashComponent(diskIdentifiers));
    }

    private static string HashComponent(IEnumerable<string?> values)
    {
        var normalized = HardwareIdentifierNormalizer.NormalizeCollectionOrUnavailable(values);

        return Sha256Hasher.Hash(normalized);
    }
}
