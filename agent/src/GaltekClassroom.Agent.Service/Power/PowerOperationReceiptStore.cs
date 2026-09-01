using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Power;

public sealed record PowerOperationReceiptStoreOptions(
    string DataDirectory,
    int MaxReceipts = 256,
    TimeSpan? Retention = null)
{
    public TimeSpan EffectiveRetention => Retention ?? TimeSpan.FromDays(7);
}

public sealed record PowerOperationReceipt(
    string OperationId,
    NetworkOperationType OperationType,
    string TargetDeviceId,
    DateTimeOffset AcceptedAtUtc,
    OperationExecutionStatus Result);

public sealed class PowerOperationReceiptStore
{
    public const string FileName = "power-operation-receipts.json";
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

    private readonly PowerOperationReceiptStoreOptions _options;
    private readonly ISystemClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataDirectory;
    private readonly string _filePath;

    public PowerOperationReceiptStore(
        PowerOperationReceiptStoreOptions options,
        ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options;
        _clock = clock;
        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, FileName);
    }

    public string FilePath => _filePath;

    public async Task SaveAcceptedAsync(
        OperationRequest request,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsPowerOperation(request.OperationType))
        {
            return;
        }

        var receipt = new PowerOperationReceipt(
            request.OperationId ?? string.Empty,
            request.OperationType,
            request.TargetDeviceId ?? string.Empty,
            acceptedAtUtc.ToUniversalTime(),
            OperationExecutionStatus.Success);

        if (!IsValid(receipt))
        {
            throw new InvalidOperationException("Cannot persist invalid power operation receipt.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<PowerOperationReceipt> receipts = await ReadCurrentReceiptsAsync(cancellationToken)
                .ConfigureAwait(false);
            receipts.RemoveAll(existing => string.Equals(
                existing.OperationId,
                receipt.OperationId,
                StringComparison.Ordinal));
            receipts.Add(receipt);

            receipts = Bound(Cleanup(receipts, _clock.UtcNow)).ToList();
            await SaveDocumentAsync(receipts, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult?> TryGetSuccessResultAsync(
        string operationId,
        string targetDeviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(targetDeviceId))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<PowerOperationReceipt> receipts = await ReadCurrentReceiptsAsync(cancellationToken)
                .ConfigureAwait(false);
            List<PowerOperationReceipt> cleaned = Cleanup(receipts, _clock.UtcNow).ToList();
            if (cleaned.Count != receipts.Count)
            {
                await SaveDocumentAsync(Bound(cleaned).ToList(), cancellationToken).ConfigureAwait(false);
            }

            PowerOperationReceipt? receipt = cleaned.FirstOrDefault(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal)
                && string.Equals(candidate.TargetDeviceId, targetDeviceId, StringComparison.Ordinal));
            if (receipt is null)
            {
                return null;
            }

            return new OperationResult
            {
                OperationId = receipt.OperationId,
                OperationType = receipt.OperationType,
                TargetDeviceId = receipt.TargetDeviceId,
                ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
                Status = OperationExecutionStatus.Success,
                ErrorCode = NetworkOperationErrorCode.Unspecified,
                Message = "Power control request was accepted by Windows.",
                StartedAtUnixMs = receipt.AcceptedAtUtc.ToUnixTimeMilliseconds(),
                CompletedAtUnixMs = receipt.AcceptedAtUtc.ToUnixTimeMilliseconds()
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PowerOperationReceipt>> ReadCurrentReceiptsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);

        var document = await JsonSerializer.DeserializeAsync<PowerOperationReceiptDocument>(
            stream,
            ReadOptions,
            cancellationToken).ConfigureAwait(false);

        if (document?.SchemaVersion != SchemaVersion || document.Receipts is null)
        {
            return [];
        }

        return document.Receipts
            .Where(IsValid)
            .ToList();
    }

    private async Task SaveDocumentAsync(
        IReadOnlyList<PowerOperationReceipt> receipts,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);

        var tempPath = Path.Combine(_dataDirectory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(
                new PowerOperationReceiptDocument(SchemaVersion, receipts.ToArray()),
                WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
            DurableFileWriter.ReplaceOrMove(tempPath, _filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private IEnumerable<PowerOperationReceipt> Cleanup(
        IEnumerable<PowerOperationReceipt> receipts,
        DateTimeOffset nowUtc)
    {
        TimeSpan retention = _options.EffectiveRetention;
        DateTimeOffset cutoff = retention <= TimeSpan.Zero
            ? nowUtc.ToUniversalTime()
            : nowUtc.ToUniversalTime().Subtract(retention);

        return receipts
            .Where(receipt => receipt.AcceptedAtUtc > cutoff)
            .OrderBy(receipt => receipt.AcceptedAtUtc);
    }

    private IEnumerable<PowerOperationReceipt> Bound(IEnumerable<PowerOperationReceipt> receipts)
    {
        int maxReceipts = Math.Max(1, _options.MaxReceipts);
        return receipts
            .OrderBy(receipt => receipt.AcceptedAtUtc)
            .TakeLast(maxReceipts);
    }

    private static bool IsValid(PowerOperationReceipt? receipt)
    {
        return receipt is not null
            && !string.IsNullOrWhiteSpace(receipt.OperationId)
            && IsPowerOperation(receipt.OperationType)
            && !string.IsNullOrWhiteSpace(receipt.TargetDeviceId)
            && receipt.AcceptedAtUtc != default
            && receipt.AcceptedAtUtc.Offset == TimeSpan.Zero
            && receipt.Result == OperationExecutionStatus.Success;
    }

    private static bool IsPowerOperation(NetworkOperationType operationType)
    {
        return operationType is NetworkOperationType.Shutdown or NetworkOperationType.Restart;
    }

    private sealed record PowerOperationReceiptDocument(
        int SchemaVersion,
        PowerOperationReceipt[] Receipts);
}
