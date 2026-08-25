namespace GaltekClassroom.Agent.Shared;

public sealed record HardwareFingerprint(
    string CpuHash,
    string MotherboardHash,
    string MacHash,
    string DiskHash);
