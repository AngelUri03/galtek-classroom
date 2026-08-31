using System.Diagnostics;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public sealed record RuntimeDiagnosticsSnapshot
{
    [JsonPropertyName("product")]
    [JsonPropertyOrder(0)]
    public string Product { get; init; } = ProductInfo.ProductCode;

    [JsonPropertyName("component")]
    [JsonPropertyOrder(1)]
    public string Component { get; init; } = string.Empty;

    [JsonPropertyName("capturedAtUtc")]
    [JsonPropertyOrder(2)]
    public DateTimeOffset CapturedAtUtc { get; init; }

    [JsonPropertyName("samplingMode")]
    [JsonPropertyOrder(3)]
    public string SamplingMode { get; init; } = "ON_DEMAND";

    [JsonPropertyName("processId")]
    [JsonPropertyOrder(4)]
    public int ProcessId { get; init; }

    [JsonPropertyName("processName")]
    [JsonPropertyOrder(5)]
    public string ProcessName { get; init; } = string.Empty;

    [JsonPropertyName("startedAtUtc")]
    [JsonPropertyOrder(6)]
    public DateTimeOffset? StartedAtUtc { get; init; }

    [JsonPropertyName("uptime")]
    [JsonPropertyOrder(7)]
    public string? Uptime { get; init; }

    [JsonPropertyName("uptimeMs")]
    [JsonPropertyOrder(8)]
    public long? UptimeMs { get; init; }

    [JsonPropertyName("totalProcessorTime")]
    [JsonPropertyOrder(9)]
    public string? TotalProcessorTime { get; init; }

    [JsonPropertyName("totalProcessorTimeMs")]
    [JsonPropertyOrder(10)]
    public long? TotalProcessorTimeMs { get; init; }

    [JsonPropertyName("workingSetBytes")]
    [JsonPropertyOrder(11)]
    public long? WorkingSetBytes { get; init; }

    [JsonPropertyName("privateMemoryBytes")]
    [JsonPropertyOrder(12)]
    public long? PrivateMemoryBytes { get; init; }

    [JsonPropertyName("threadCount")]
    [JsonPropertyOrder(13)]
    public int? ThreadCount { get; init; }

    [JsonPropertyName("managedMemoryBytes")]
    [JsonPropertyOrder(14)]
    public long ManagedMemoryBytes { get; init; }

    public static RuntimeDiagnosticsSnapshot CaptureCurrentProcess(string component)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var capturedAtUtc = DateTimeOffset.UtcNow;
        var startedAtUtc = TryGetStartTimeUtc(process);
        TimeSpan? uptime = startedAtUtc is null
            ? null
            : capturedAtUtc - startedAtUtc.Value;
        var processorTime = TryGetTotalProcessorTime(process);

        return new RuntimeDiagnosticsSnapshot
        {
            Component = string.IsNullOrWhiteSpace(component) ? process.ProcessName : component.Trim(),
            CapturedAtUtc = capturedAtUtc,
            ProcessId = Environment.ProcessId,
            ProcessName = process.ProcessName,
            StartedAtUtc = startedAtUtc,
            Uptime = FormatTimeSpan(uptime),
            UptimeMs = ToMilliseconds(uptime),
            TotalProcessorTime = FormatTimeSpan(processorTime),
            TotalProcessorTimeMs = ToMilliseconds(processorTime),
            WorkingSetBytes = TryGetInt64(() => process.WorkingSet64),
            PrivateMemoryBytes = TryGetInt64(() => process.PrivateMemorySize64),
            ThreadCount = TryGetInt32(() => process.Threads.Count),
            ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false)
        };
    }

    private static DateTimeOffset? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime).ToUniversalTime();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static TimeSpan? TryGetTotalProcessorTime(Process process)
    {
        try
        {
            return process.TotalProcessorTime;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static long? TryGetInt64(Func<long> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static int? TryGetInt32(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? FormatTimeSpan(TimeSpan? value)
    {
        return value is null ? null : value.Value.ToString("c");
    }

    private static long? ToMilliseconds(TimeSpan? value)
    {
        return value is null ? null : Math.Max(0, (long)value.Value.TotalMilliseconds);
    }
}
